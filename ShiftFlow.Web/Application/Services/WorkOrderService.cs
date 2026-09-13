using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Application.Services;

public class WorkOrderService : IWorkOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ISparePartService _spareParts;
    private readonly IAssetScopeService _scope;
    private readonly ILanguageService _loc;
    private readonly IPermissionService _permissions;
    public WorkOrderService(ApplicationDbContext db, IAuditService audit, ISparePartService spareParts, IAssetScopeService scope, ILanguageService loc, IPermissionService permissions)
    { _db = db; _audit = audit; _spareParts = spareParts; _scope = scope; _loc = loc; _permissions = permissions; }

    // Manager-ness is resolved here rather than taken as a caller-supplied bool — a controller (or
    // any other caller) passing `false` must not be able to pick the weaker authorization path.
    private Task<bool> IsManagerAsync(string userId) =>
        _permissions.HasPermissionAsync(userId, ShiftFlow.Web.Authorization.PermissionCatalog.WorkOrderManage);

    private static void ValidatePriority(string priority)
    {
        if (!WorkOrder.Priorities.Contains(priority)) throw new InvalidOperationException("Invalid priority.");
    }

    // The Assets/Details "New Work Order"/"Report Action" buttons are now hidden for a Retired
    // asset, but that's UI-only — enforce it here too (both CreateAsync and ReportAsync route
    // through this) so a direct POST, or another caller like OrdersController's vendor-required
    // Maintenance branch, can't open new work against equipment that's already decommissioned.
    private async Task EnsureAssetNotRetiredAsync(int assetId)
    {
        if (await _db.Assets.AnyAsync(a => a.Id == assetId && a.Status == AssetStatuses.Retired))
            throw new InvalidOperationException("This asset is retired and can't have new work orders opened against it.");
    }

    // AssetsController's Details/Edit/etc. all route through ScopedAssetsAsync so a UserAssetScope-
    // restricted user can't even see an out-of-scope asset — but WorkOrdersController.Create/Report
    // loaded the asset directly, so that same user could still open a Work Order against an asset
    // they can't view, just by knowing/guessing its ID. Checked here so no caller (controller, the
    // AI assistant, or anything else that reaches this service) can bypass it.
    private async Task EnsureAssetInScopeAsync(int assetId, string userId)
    {
        var asset = await _db.Assets.Include(a => a.Zone).Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == assetId)
            ?? throw new InvalidOperationException("Asset not found.");
        if (!await _scope.IsInScopeAsync(asset, userId))
            throw new InvalidOperationException("Asset not found.");
    }

    // Details (round 12) blocks a scoped user from even viewing an out-of-scope Work Order, but
    // every mutating action below still took only a bare workOrderId — a scoped user who can't
    // view a Work Order via Details could still Accept/Cancel/Reassign/etc. it via a direct POST
    // with a guessed ID, since none of these re-checked scope. Skipped for VendorFixAsync/
    // VendorBlockAsync since UserAssetScope is a staff concept — a vendor portal user is never
    // scoped this way (see VendorPortalController's own vendor-ownership checks instead).
    private async Task EnsureWorkOrderInScopeAsync(int workOrderId, string userId)
    {
        var assetId = await _db.WorkOrders.Where(w => w.Id == workOrderId).Select(w => (int?)w.AssetId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Work order not found.");
        await EnsureAssetInScopeAsync(assetId, userId);
    }

    // Used only by the assignee's own actions (EmployeeFix/AdvanceWithoutVendor) — a scope narrowed
    // or added *after* a Work Order was already assigned must not silently lock the legitimate
    // assignee out of finishing their own already-assigned work; scope restricts what a user can
    // newly see/take on, not access already legitimately granted. Manager-only actions (Accept,
    // AssignEmployee, ForceClose, etc.) keep the strict, unconditional check above.
    private async Task EnsureWorkOrderActionableAsync(int workOrderId, string userId)
    {
        var assignedToUserId = await _db.WorkOrders.Where(w => w.Id == workOrderId).Select(w => w.AssignedToUserId).FirstOrDefaultAsync();
        if (assignedToUserId == userId) return;
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
    }

    public async Task<WorkOrder> CreateAsync(WorkOrder workOrder, string userId)
    {
        await EnsureAssetNotRetiredAsync(workOrder.AssetId);
        await EnsureAssetInScopeAsync(workOrder.AssetId, userId);
        // Validate the assignee up front so a stale/tampered id surfaces as a friendly error
        // rather than a raw FK-constraint DbUpdateException on save.
        if (!string.IsNullOrWhiteSpace(workOrder.AssignedToUserId) && !await _db.Users.AnyAsync(u => u.Id == workOrder.AssignedToUserId))
            throw new InvalidOperationException("Selected employee not found.");
        ValidatePriority(workOrder.Priority);
        workOrder.Stage = WorkOrderStages.New;
        workOrder.CreatedByUserId = userId;
        workOrder.CreatedDate = DateTime.UtcNow;
        workOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = WorkOrderStages.New, ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });
        await AssignNumberAsync(workOrder);
        _db.WorkOrders.Add(workOrder);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "WorkOrder", workOrder.Id.ToString(), userId, newValue: workOrder.WorkOrderNumber);
        return workOrder;
    }

    public async Task<WorkOrder> ReportAsync(WorkOrder workOrder, string userId)
    {
        await EnsureAssetNotRetiredAsync(workOrder.AssetId);
        await EnsureAssetInScopeAsync(workOrder.AssetId, userId);
        // Validate the action type/cause up front so a stale dropdown value surfaces as a friendly
        // error rather than a raw FK-constraint DbUpdateException on save.
        if (workOrder.ActionTypeId is { } atId && !await _db.AssetActionTypes.AnyAsync(a => a.Id == atId))
            throw new InvalidOperationException("Selected action type not found.");
        if (workOrder.CauseId is { } cId && !await _db.AssetActionCauses.AnyAsync(a => a.Id == cId))
            throw new InvalidOperationException("Selected cause not found.");

        workOrder.Stage = WorkOrderStages.Draft;
        workOrder.CreatedByUserId = userId;
        workOrder.CreatedDate = DateTime.UtcNow;
        workOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = WorkOrderStages.Draft, ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });
        await AssignNumberAsync(workOrder);
        _db.WorkOrders.Add(workOrder);
        await SetAssetStatusAsync(workOrder.AssetId, AssetStatuses.Defective);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Report", "WorkOrder", workOrder.Id.ToString(), userId, newValue: workOrder.WorkOrderNumber);
        return workOrder;
    }

    /// <summary>Order numbers come from the shared, atomically-claimed OrderNumberSequence
    /// counter (see OrderNumberGenerator) — allocated before the entity is added so the insert is
    /// a plain SaveChanges with no duplicate-number retry loop.</summary>
    private async Task AssignNumberAsync(WorkOrder workOrder) =>
        workOrder.WorkOrderNumber = await OrderNumberGenerator.NextAsync(_db, OrderNumberPrefixes.WorkOrder, workOrder.CreatedDate.Year);

    /// <summary>Keeps Asset.Status in sync with the work order lifecycle so nobody has to flip it by
    /// hand: reporting a defect marks the asset Defective, sending it to a vendor marks it under
    /// Maintenance, and closing the fix returns it to Working (unless another work order on the same
    /// asset is still open, or the asset has been Retired — that status is never overridden).</summary>
    private async Task SetAssetStatusAsync(int assetId, string status)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset != null && asset.Status != AssetStatuses.Retired) asset.Status = status;
    }

    private async Task ValidateVendorAsync(int vendorId)
    {
        var exists = await _db.Vendors.AnyAsync(v => v.Id == vendorId && v.Status == "Active");
        if (!exists) throw new InvalidOperationException("Selected vendor not found or inactive.");
    }

    private void AddStageEvent(WorkOrder wo, string stage, string userId) =>
        _db.WorkOrderStageEvents.Add(new WorkOrderStageEvent { WorkOrderId = wo.Id, Stage = stage, ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });

    public async Task AcceptAsync(int workOrderId, int? vendorId, string priority, string userId)
    {
        ValidatePriority(priority);
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.Draft) throw new InvalidOperationException("Only a Draft report can be accepted.");
        if (vendorId == null && wo.AssignedToUserId == null)
            throw new InvalidOperationException("Assign a vendor or an employee before accepting this report.");
        if (vendorId != null) await ValidateVendorAsync(vendorId.Value);

        // No vendor, only an assigned employee — skip the vendor pipeline entirely and go straight
        // to "New" so the employee's own Report Fix action becomes available.
        var newStage = vendorId != null ? WorkOrderStages.SentToVendor : WorkOrderStages.New;

        // The stage flip and its history row must commit together: the ExecuteUpdateAsync below is
        // its own implicit transaction, so without this an exception between them left the work
        // order advanced with no stage-history entry for how it got there.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, newStage).SetProperty(w => w.VendorId, w => vendorId ?? w.VendorId).SetProperty(w => w.Priority, priority));
        if (rows == 0) throw new InvalidOperationException("Only a Draft report can be accepted.");
        wo.Priority = priority;
        AddStageEvent(wo, newStage, userId);
        await SetAssetStatusAsync(wo.AssetId, AssetStatuses.Maintenance);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("Accept", "WorkOrder", wo.Id.ToString(), userId, oldValue: WorkOrderStages.Draft, newValue: newStage);
    }

    public async Task RejectAsync(int workOrderId, string? reason, string userId)
    {
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.Draft) throw new InvalidOperationException("Only a Draft report can be rejected.");
        var newNotes = string.IsNullOrWhiteSpace(reason) ? wo.Notes : $"{wo.Notes}\n\nRejected: {reason}".Trim();

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, WorkOrderStages.Rejected).SetProperty(w => w.Notes, newNotes));
        if (rows == 0) throw new InvalidOperationException("Only a Draft report can be rejected.");
        AddStageEvent(wo, WorkOrderStages.Rejected, userId);
        // Same rule as ConfirmFix/ForceClose — rejecting one report must not clear a defect another
        // still-open order on the same asset is tracking.
        if (!await AssetWorkState.HasOtherOpenWorkAsync(_db, wo.AssetId, excludeWorkOrderId: wo.Id))
            await SetAssetStatusAsync(wo.AssetId, AssetStatuses.Working);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("Reject", "WorkOrder", wo.Id.ToString(), userId, oldValue: WorkOrderStages.Draft, newValue: WorkOrderStages.Rejected, details: reason);
    }

    public async Task SendToVendorAsync(int workOrderId, int vendorId, string userId)
    {
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.New) throw new InvalidOperationException("Only a New work order can be sent to a vendor.");
        await ValidateVendorAsync(vendorId);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.New)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, WorkOrderStages.SentToVendor).SetProperty(w => w.VendorId, vendorId));
        if (rows == 0) throw new InvalidOperationException("Only a New work order can be sent to a vendor.");
        AddStageEvent(wo, WorkOrderStages.SentToVendor, userId);
        await SetAssetStatusAsync(wo.AssetId, AssetStatuses.Maintenance);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("SendToVendor", "WorkOrder", wo.Id.ToString(), userId, oldValue: WorkOrderStages.New, newValue: WorkOrderStages.SentToVendor);
    }

    // Validates every part is compatible with wo's asset, decrements stock atomically per part
    // (aborting the whole submission if any part's stock is insufficient), and replaces wo.Parts
    // with fresh rows snapshotting Name/UnitCost off the catalog. A tampered/stale picker value
    // must not silently decrement an unrelated part's stock. Caller must have wo.Parts already
    // loaded (Include(w => w.Parts)) and must run this inside the same transaction as the stage
    // transition, since a part failing mid-loop must roll back any parts already decremented.
    // Returns the sum of each applied part's UnitCost * Quantity — FixCost is entirely derived
    // from this now; there's no manually-typed cost field anywhere in the fix-report forms.
    private async Task<decimal> ApplyPartsAsync(WorkOrder wo, List<(int SparePartId, int Quantity)> parts)
    {
        var validParts = parts.Where(p => p.Quantity > 0).ToList();
        // Re-submitting a fix replaces the previous parts list - return the old rows' quantities to
        // stock first, inside this same transaction, or the units they consumed are lost forever.
        foreach (var old in wo.Parts.Where(p => p.SparePartId != null))
            await _spareParts.IncrementStockAsync(old.SparePartId!.Value, old.Quantity);
        _db.WorkOrderParts.RemoveRange(wo.Parts);
        if (validParts.Count == 0) return 0m;

        var compatibleIds = await _db.SparePartAssets.Where(sa => sa.AssetId == wo.AssetId)
            .Select(sa => sa.SparePartId).ToListAsync();
        if (validParts.Select(p => p.SparePartId).Except(compatibleIds).Any())
            throw new InvalidOperationException("One or more selected parts are not compatible with this asset.");

        var partsCatalog = await _db.SpareParts.Where(p => validParts.Select(vp => vp.SparePartId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var totalCost = 0m;
        foreach (var p in validParts)
        {
            var catalogPart = partsCatalog[p.SparePartId];
            if (!await _spareParts.TryDecrementStockAsync(p.SparePartId, p.Quantity))
                throw new InvalidOperationException($"Not enough stock of '{catalogPart.Name}' to complete this fix (requested {p.Quantity}).");
            _db.WorkOrderParts.Add(new WorkOrderPart
            {
                WorkOrderId = wo.Id, SparePartId = p.SparePartId,
                Name = catalogPart.Name, Quantity = p.Quantity, UnitCostAtUsage = catalogPart.UnitCost,
            });
            totalCost += (catalogPart.UnitCost ?? 0m) * p.Quantity;
        }
        return totalCost;
    }

    // Nothing rejected a future-dated Fix Completion Date on any of the three fix paths — a vendor
    // (or employee) could report a fix "completed" months from now, and the work order would show
    // that future date permanently with no warning (found in a cold vendor-portal UX review,
    // confirmed: no such check exists in VendorFixAsync/EmployeeFixAsync/AdvanceWithoutVendorAsync).
    private static void EnsureCompletionDateNotFuture(DateTime? completionDate)
    {
        if (completionDate.HasValue && completionDate.Value.Date > DateTime.UtcNow.Date)
            throw new InvalidOperationException("Completion Date can't be in the future.");
    }

    public async Task VendorFixAsync(int workOrderId, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string vendorUserId)
    {
        EnsureCompletionDateNotFuture(completionDate);
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.SentToVendor) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.SentToVendor)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, WorkOrderStages.FixedPendingConfirmation)
                .SetProperty(w => w.FixCompletionDate, completionDate));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        wo.FixCost = await ApplyPartsAsync(wo, parts);

        AddStageEvent(wo, WorkOrderStages.FixedPendingConfirmation, vendorUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("VendorFix", "WorkOrder", wo.Id.ToString(), vendorUserId, oldValue: WorkOrderStages.SentToVendor, newValue: WorkOrderStages.FixedPendingConfirmation);
    }

    public async Task VendorBlockAsync(int workOrderId, int blockReasonId, string? detail, string vendorUserId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.SentToVendor) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        // Friendly error for a stale/tampered block reason instead of a raw FK-constraint failure.
        if (!await _db.WorkOrderBlockReasons.AnyAsync(r => r.Id == blockReasonId))
            throw new InvalidOperationException("Selected block reason not found.");

        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.SentToVendor)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, WorkOrderStages.Blocked).SetProperty(w => w.BlockReasonId, blockReasonId).SetProperty(w => w.BlockDetail, detail));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        AddStageEvent(wo, WorkOrderStages.Blocked, vendorUserId);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VendorBlock", "WorkOrder", wo.Id.ToString(), vendorUserId, oldValue: WorkOrderStages.SentToVendor, newValue: WorkOrderStages.Blocked, details: detail);
    }

    public async Task ResendToVendorAsync(int workOrderId, string userId)
    {
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.Blocked) throw new InvalidOperationException("Only a Blocked work order can be resent.");
        if (wo.VendorId == null) throw new InvalidOperationException("This work order has no vendor to resend to.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.Blocked)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, WorkOrderStages.SentToVendor).SetProperty(w => w.BlockReasonId, (int?)null).SetProperty(w => w.BlockDetail, (string?)null));
        if (rows == 0) throw new InvalidOperationException("Only a Blocked work order can be resent.");
        AddStageEvent(wo, WorkOrderStages.SentToVendor, userId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("Resend", "WorkOrder", wo.Id.ToString(), userId, oldValue: WorkOrderStages.Blocked, newValue: WorkOrderStages.SentToVendor);
    }

    public async Task ConfirmFixAsync(int workOrderId, string userId)
    {
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.FixedPendingConfirmation) throw new InvalidOperationException("Only a fix pending confirmation can be confirmed.");

        // Same race as ForceCloseAsync: two concurrent confirmations could both pass the in-memory
        // check above before either commits. Claim the transition atomically first.
        var closedDate = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.FixedPendingConfirmation)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, WorkOrderStages.Closed).SetProperty(w => w.ClosedDate, closedDate));
        if (rows == 0) throw new InvalidOperationException("Only a fix pending confirmation can be confirmed.");

        AddStageEvent(wo, WorkOrderStages.Closed, userId);
        if (!await AssetWorkState.HasOtherOpenWorkAsync(_db, wo.AssetId, excludeWorkOrderId: wo.Id))
            await SetAssetStatusAsync(wo.AssetId, AssetStatuses.Working);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("ConfirmFix", "WorkOrder", wo.Id.ToString(), userId, oldValue: WorkOrderStages.FixedPendingConfirmation, newValue: WorkOrderStages.Closed);
    }

    public async Task AssignEmployeeAsync(int workOrderId, string? employeeUserId, string userId)
    {
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        var old = wo.AssignedToUserId;
        // Friendly error for a stale/tampered picker value instead of a raw FK-constraint failure.
        if (!string.IsNullOrWhiteSpace(employeeUserId) && !await _db.Users.AnyAsync(u => u.Id == employeeUserId))
            throw new InvalidOperationException("Selected employee not found.");
        // Same invariant AcceptAsync enforces at creation (vendor or employee, never neither) — this
        // path let a manager clear the employee off a work order that had no vendor either, leaving
        // it assigned to nobody with no way to act on it. Only blocks clearing when there's no
        // vendor to fall back on; a vendor-assigned work order can still have its employee cleared.
        if (string.IsNullOrWhiteSpace(employeeUserId) && wo.VendorId == null)
            throw new InvalidOperationException("This work order has no vendor — assign an employee, or send it to a vendor instead of unassigning.");
        wo.AssignedToUserId = string.IsNullOrWhiteSpace(employeeUserId) ? null : employeeUserId;
        await _db.SaveChangesAsync();

        // Log the employee's name, not the raw user-id GUID — audit values are shown as-is.
        async Task<string?> NameOf(string? uid) => uid == null ? null
            : await _db.Users.Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync();
        await _audit.LogAsync("AssignEmployee", "WorkOrder", wo.Id.ToString(), userId, oldValue: await NameOf(old), newValue: await NameOf(wo.AssignedToUserId));
    }

    public async Task<WorkOrder> EmployeeFixAsync(int workOrderId, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId)
    {
        EnsureCompletionDateNotFuture(completionDate);
        await EnsureWorkOrderActionableAsync(workOrderId, employeeUserId);
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.AssignedToUserId != employeeUserId) throw new InvalidOperationException("This work order isn't assigned to you.");
        if (wo.VendorId != null) throw new InvalidOperationException("A vendor is already handling this work order.");
        if (wo.Stage != WorkOrderStages.New) throw new InvalidOperationException("This work order isn't awaiting a fix.");

        // Same race as ForceCloseAsync/ConfirmFixAsync: claim the transition atomically before
        // touching Parts, so two concurrent submissions can't both pass the in-memory check above.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.New)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, WorkOrderStages.FixedPendingConfirmation)
                .SetProperty(w => w.FixCompletionDate, completionDate));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a fix.");

        wo.FixCost = await ApplyPartsAsync(wo, parts);

        AddStageEvent(wo, WorkOrderStages.FixedPendingConfirmation, employeeUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("EmployeeFix", "WorkOrder", wo.Id.ToString(), employeeUserId, oldValue: WorkOrderStages.New, newValue: WorkOrderStages.FixedPendingConfirmation);
        return wo;
    }

    /// <summary>Lets a manager (WorkOrder.Manage) or the work order's own assigned employee report
    /// the fix directly instead of waiting on the vendor's own portal response — regardless of
    /// RequiresVendorResponse, since an assigned employee is still the one actually accountable for
    /// getting the asset fixed even when a vendor is also on the job (e.g. the vendor has no portal
    /// login, or is simply slow to respond). Ends in the same "Fixed - Pending Confirmation" state as
    /// VendorFixAsync/EmployeeFixAsync so ConfirmFixAsync works unchanged regardless of who actually
    /// reported the fix.</summary>
    public async Task<WorkOrder> AdvanceWithoutVendorAsync(int workOrderId, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string userId)
    {
        EnsureCompletionDateNotFuture(completionDate);
        // Manager-ness is resolved here, not handed in by the caller. A manager acting outside
        // their own scope is still blocked; the assigned employee keeps access to their own
        // already-assigned work regardless of a scope narrowed/added afterward.
        var isManager = await IsManagerAsync(userId);
        if (isManager) await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        else await EnsureWorkOrderActionableAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != WorkOrderStages.SentToVendor) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        if (!isManager && wo.AssignedToUserId != userId) throw new InvalidOperationException("This work order isn't assigned to you.");
        // RequiresVendorResponse was stored and displayed but never enforced - when it is set, only
        // the vendor's own Fix/Block actions may move this work order forward.
        if (wo.RequiresVendorResponse)
            throw new InvalidOperationException("This work order requires the vendor's own response - it can't be advanced on their behalf. Force-close it instead if the vendor can't respond.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == WorkOrderStages.SentToVendor)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, WorkOrderStages.FixedPendingConfirmation)
                .SetProperty(w => w.FixCompletionDate, completionDate));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        wo.FixCost = await ApplyPartsAsync(wo, parts);

        AddStageEvent(wo, WorkOrderStages.FixedPendingConfirmation, userId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("AdvanceWithoutVendor", "WorkOrder", wo.Id.ToString(), userId, oldValue: WorkOrderStages.SentToVendor, newValue: WorkOrderStages.FixedPendingConfirmation);
        return wo;
    }

    public async Task ForceCloseAsync(int workOrderId, string? reason, string userId)
    {
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage == WorkOrderStages.Closed) throw new InvalidOperationException("Already closed.");
        var old = wo.Stage;
        var closedDate = DateTime.UtcNow;
        var newNotes = string.IsNullOrWhiteSpace(reason) ? wo.Notes
            : string.IsNullOrWhiteSpace(wo.Notes) ? $"Force-closed: {reason}" : $"{wo.Notes}\nForce-closed: {reason}";

        // A double-click/double-submit (or two racing requests) could both pass the Stage=="Closed"
        // check above before either commits, each then appending its own stage-history row - the
        // in-memory check alone doesn't close that window. ExecuteUpdateAsync's WHERE clause is
        // evaluated atomically by the database, so only the first request to actually reach it can
        // match Stage != "Closed" and flip the row; a loser sees 0 rows affected and is turned back
        // with the same "Already closed" error the check above already gives a non-racing caller.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.Stage != WorkOrderStages.Closed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, WorkOrderStages.Closed)
                .SetProperty(w => w.ClosedDate, closedDate)
                .SetProperty(w => w.Notes, newNotes));
        if (rows == 0) throw new InvalidOperationException("Already closed.");

        AddStageEvent(wo, WorkOrderStages.Closed, userId);
        if (!await AssetWorkState.HasOtherOpenWorkAsync(_db, wo.AssetId, excludeWorkOrderId: wo.Id))
            await SetAssetStatusAsync(wo.AssetId, AssetStatuses.Working);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("ForceClose", "WorkOrder", wo.Id.ToString(), userId, oldValue: old, newValue: WorkOrderStages.Closed, details: reason);
    }

    public async Task UpdatePriorityAsync(int workOrderId, string priority, string userId)
    {
        if (!WorkOrder.Priorities.Contains(priority)) throw new InvalidOperationException("Invalid priority.");
        await EnsureWorkOrderInScopeAsync(workOrderId, userId);
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage is not (WorkOrderStages.Draft or WorkOrderStages.New)) throw new InvalidOperationException("Priority can only be changed before a work order is sent to a vendor.");
        if (wo.Priority == priority) return;
        var oldPriority = wo.Priority;
        wo.Priority = priority;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("UpdatePriority", "WorkOrder", wo.Id.ToString(), userId, oldValue: oldPriority, newValue: priority);
    }

    public async Task<WorkOrder> CreatePreventiveMaintenanceOccurrenceAsync(int assetId, int vendorId, int sourceContractId, DateTime scheduledDate, string? contractNumber, string systemUserId)
    {
        // Same guard as CreateAsync/ReportAsync — without it, retiring an asset still linked to an
        // active PM contract doesn't stop this background generator from opening new "Sent to
        // Vendor" work orders against it indefinitely (and re-flipping Asset.Status back to
        // "Maintenance" every time it does).
        await EnsureAssetNotRetiredAsync(assetId);
        var contractLabel = string.IsNullOrWhiteSpace(contractNumber) ? sourceContractId.ToString() : contractNumber;
        var wo = new WorkOrder
        {
            AssetId = assetId,
            VendorId = vendorId,
            SourceContractId = sourceContractId,
            ScheduledDate = scheduledDate.Date,
            Stage = WorkOrderStages.SentToVendor,
            Priority = "Medium",
            Description = $"Preventive Maintenance — due {scheduledDate:yyyy-MM-dd} (Contract {contractLabel})",
            CreatedByUserId = systemUserId,
            CreatedDate = DateTime.UtcNow,
            RequiresVendorResponse = true,
        };
        wo.StageEvents.Add(new WorkOrderStageEvent { Stage = WorkOrderStages.SentToVendor, ChangedAt = DateTime.UtcNow, ChangedByUserId = systemUserId });
        await AssignNumberAsync(wo);
        _db.WorkOrders.Add(wo);
        await SetAssetStatusAsync(assetId, AssetStatuses.Maintenance);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AutoGeneratePM", "WorkOrder", wo.Id.ToString(), systemUserId,
            newValue: wo.WorkOrderNumber, details: $"Contract #{sourceContractId}, due {scheduledDate:yyyy-MM-dd}");
        return wo;
    }

    public async Task<WorkOrder> CreateRecurringVendorOccurrenceAsync(int assetId, int vendorId, string? assignedToUserId, int sourceRecurringOrderId, DateTime scheduledDate, string systemUserId)
    {
        // Same guard as CreatePreventiveMaintenanceOccurrenceAsync — without it, retiring an asset
        // still covered by an active schedule doesn't stop this background generator from opening
        // new "Sent to Vendor" work orders against it indefinitely.
        await EnsureAssetNotRetiredAsync(assetId);
        var wo = new WorkOrder
        {
            AssetId = assetId,
            VendorId = vendorId,
            AssignedToUserId = assignedToUserId,
            SourceRecurringOrderId = sourceRecurringOrderId,
            ScheduledDate = scheduledDate.Date,
            Stage = WorkOrderStages.SentToVendor,
            Priority = "Medium",
            Description = $"Recurring vendor order — due {scheduledDate:yyyy-MM-dd} (Schedule #{sourceRecurringOrderId})",
            CreatedByUserId = systemUserId,
            CreatedDate = DateTime.UtcNow,
            RequiresVendorResponse = true,
        };
        wo.StageEvents.Add(new WorkOrderStageEvent { Stage = WorkOrderStages.SentToVendor, ChangedAt = DateTime.UtcNow, ChangedByUserId = systemUserId });
        await AssignNumberAsync(wo);
        _db.WorkOrders.Add(wo);
        await SetAssetStatusAsync(assetId, AssetStatuses.Maintenance);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AutoGenerateRecurring", "WorkOrder", wo.Id.ToString(), systemUserId,
            newValue: wo.WorkOrderNumber, details: $"Schedule #{sourceRecurringOrderId}, due {scheduledDate:yyyy-MM-dd}");
        return wo;
    }

    private async Task<List<WorkOrder>> GetExportRowsAsync(string userId)
    {
        var query = _db.WorkOrders.AsNoTracking().Include(w => w.Asset).Include(w => w.Vendor).AsQueryable();
        // Same scope enforcement as Details (round 12) — an export must not dump orders the
        // exporting user can't even see in the list, matching round 3's fix for Assets export.
        if (await _scope.HasScopeAsync(userId))
        {
            var scopedAssetIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).Select(a => a.Id).ToListAsync();
            query = query.Where(w => scopedAssetIds.Contains(w.AssetId));
        }
        return await query.OrderByDescending(w => w.CreatedDate).ToListAsync();
    }

    public async Task<byte[]> ExportToExcelAsync(string userId)
    {
        var orders = await GetExportRowsAsync(userId);
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Work Orders");
        string[] headers = ["Work Order #", "Asset", "Priority", "Stage", "Vendor", "Created", "Closed"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var w in orders)
        {
            ws.Cells[row, 1].Value = w.WorkOrderNumber;
            ws.Cells[row, 2].Value = w.Asset?.AssetTag;
            ws.Cells[row, 3].Value = w.Priority;
            ws.Cells[row, 4].Value = w.Stage;
            ws.Cells[row, 5].Value = w.Vendor?.Name;
            ws.Cells[row, 6].Value = w.CreatedDate.ToString("yyyy-MM-dd");
            ws.Cells[row, 7].Value = w.ClosedDate?.ToString("yyyy-MM-dd");
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }

    public async Task<byte[]> ExportToPdfAsync(string userId)
    {
        var orders = await GetExportRowsAsync(userId);
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            PdfReportHelper.ApplyPageBackground(pdf);
            var doc = new Document(pdf);
            doc.SetFont(PdfReportHelper.GetFont(_loc.IsRTL));
            PdfReportHelper.AddHeader(doc, _loc.T("Work Orders"), _loc.TDate(DateTime.Today.ToString("dddd, dd MMMM yyyy")));

            var closed = orders.Count(w => w.ClosedDate != null);
            var critical = orders.Count(w => w.Priority == "Critical" && w.ClosedDate == null);
            PdfReportHelper.AddKpiRow(doc,
                (_loc.T("Total"), orders.Count.ToString(), PdfReportHelper.Primary),
                (_loc.T("Open"), (orders.Count - closed).ToString(), PdfReportHelper.Warning),
                (_loc.T("Closed"), closed.ToString(), PdfReportHelper.Success),
                (_loc.T("Critical Open"), critical.ToString(), PdfReportHelper.Danger));

            var byStage = orders.GroupBy(w => _loc.T(w.Stage)).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count()));
            PdfReportHelper.AddBarChart(doc, _loc.T("Work Orders by Stage"), byStage, PdfReportHelper.Primary);
            var byPriority = orders.GroupBy(w => _loc.T(w.Priority)).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count()));
            PdfReportHelper.AddBarChart(doc, _loc.T("Work Orders by Priority"), byPriority, PdfReportHelper.Warning);

            // Equal-width columns (the old `new Table(7, true)`) squeezed "Work Order #" values
            // like "WO-2026-0020" into a column too narrow to fit on one line, wrapping mid-string
            // at the hyphen. Widening that column alone wasn't enough — Document's default 12pt
            // body font left even "AST-0001" (8 chars) wrapping in an 8-char-wide Asset column, so
            // the whole table needed a smaller font, not just different column proportions.
            var table = PdfReportHelper.StyledTable(
                new float[] { 2.2f, 1.4f, 1f, 1.3f, 1.6f, 1.1f, 1.1f },
                new[] { _loc.T("Work Order #"), _loc.T("Asset"), _loc.T("Priority"), _loc.T("Stage"), _loc.T("Vendor"), _loc.T("Created"), _loc.T("Closed") }, 8);
            var i = 0;
            foreach (var w in orders)
            {
                PdfReportHelper.AddRow(table, i++, 8,
                    w.WorkOrderNumber, w.Asset?.AssetTag ?? "", _loc.T(w.Priority), _loc.T(w.Stage), w.Vendor?.Name ?? "",
                    w.CreatedDate.ToString("yyyy-MM-dd"), w.ClosedDate?.ToString("yyyy-MM-dd") ?? "");
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
