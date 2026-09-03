using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class WorkOrderService : IWorkOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ISparePartService _spareParts;
    public WorkOrderService(ApplicationDbContext db, IAuditService audit, ISparePartService spareParts) { _db = db; _audit = audit; _spareParts = spareParts; }

    // The Assets/Details "New Work Order"/"Report Action" buttons are now hidden for a Retired
    // asset, but that's UI-only — enforce it here too (both CreateAsync and ReportAsync route
    // through this) so a direct POST, or another caller like OrdersController's vendor-required
    // Maintenance branch, can't open new work against equipment that's already decommissioned.
    private async Task EnsureAssetNotRetiredAsync(int assetId)
    {
        if (await _db.Assets.AnyAsync(a => a.Id == assetId && a.Status == "Retired"))
            throw new InvalidOperationException("This asset is retired and can't have new work orders opened against it.");
    }

    public async Task<WorkOrder> CreateAsync(WorkOrder workOrder, string userId)
    {
        await EnsureAssetNotRetiredAsync(workOrder.AssetId);
        workOrder.Stage = "New";
        workOrder.CreatedByUserId = userId;
        workOrder.CreatedDate = DateTime.UtcNow;
        workOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "New", ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });
        _db.WorkOrders.Add(workOrder);
        await SaveWithUniqueNumberRetryAsync(workOrder);
        await _audit.LogAsync("Create", "WorkOrder", workOrder.Id.ToString(), userId, newValue: workOrder.WorkOrderNumber);
        return workOrder;
    }

    // Both decimal(12,2) columns FixCost lands in (Report/EmployeeFix/VendorFix/AdvanceWithoutVendor
    // all set it) — an out-of-range value used to reach an unhandled ArgumentException at the SQL
    // parameter layer instead of a clean message, and a negative value was silently accepted and
    // persisted with no business validation at all. Confirmed live on EmployeeFix.
    private const decimal MaxCost = 9_999_999_999.99m;

    private static void ValidateCost(decimal? cost)
    {
        if (cost is { } c && (c < 0 || c > MaxCost))
            throw new InvalidOperationException($"Cost must be between 0 and {MaxCost:N2}.");
    }

    public async Task<WorkOrder> ReportAsync(WorkOrder workOrder, string userId)
    {
        await EnsureAssetNotRetiredAsync(workOrder.AssetId);
        // A non-existent ActionTypeId/CauseId (e.g. a stale dropdown value) used to reach an
        // unhandled FK-constraint DbUpdateException at SaveWithUniqueNumberRetryAsync - which,
        // worse, isn't even a duplicate-key error, so the retry loop there just failed the same
        // way five times before still leaking the raw SQL error. Validate up front instead.
        if (workOrder.ActionTypeId is { } atId && !await _db.AssetActionTypes.AnyAsync(a => a.Id == atId))
            throw new InvalidOperationException("Selected action type not found.");
        if (workOrder.CauseId is { } cId && !await _db.AssetActionCauses.AnyAsync(a => a.Id == cId))
            throw new InvalidOperationException("Selected cause not found.");

        workOrder.Stage = "Draft";
        workOrder.CreatedByUserId = userId;
        workOrder.CreatedDate = DateTime.UtcNow;
        workOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "Draft", ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });
        _db.WorkOrders.Add(workOrder);
        await SetAssetStatusAsync(workOrder.AssetId, "Defective");
        await SaveWithUniqueNumberRetryAsync(workOrder);
        await _audit.LogAsync("Report", "WorkOrder", workOrder.Id.ToString(), userId, newValue: workOrder.WorkOrderNumber);
        return workOrder;
    }

    /// <summary>The WorkOrderNumber "WO-{year}-{seq:D4}" was assigned from a plain COUNT-then-use
    /// query with no atomic guard — two requests confirming a defect (or creating a work order)
    /// concurrently could compute the same seq and both try to insert the same number, hitting
    /// the unique index and raising a raw, unhandled DbUpdateException all the way to the client
    /// (confirmed live: a concurrent InspectionOrders/UpdateItem pair produced exactly this SQL
    /// exception leak). Retry with a freshly recomputed number on that specific failure instead of
    /// trying to make the count itself atomic, since a handful of retries is far simpler than a
    /// real sequence object and the collision is rare enough that a retry loop is plenty.</summary>
    private async Task SaveWithUniqueNumberRetryAsync(WorkOrder workOrder)
    {
        for (var attempt = 0; ; attempt++)
        {
            var year = workOrder.CreatedDate.Year;
            var seq = await _db.WorkOrders.CountAsync(w => w.CreatedDate.Year == year) + 1;
            workOrder.WorkOrderNumber = $"WO-{year}-{seq:D4}";
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                // Duplicate WorkOrderNumber from a concurrent insert — recompute and retry.
                // The entity stays tracked as Added after a failed SaveChanges, so the next
                // attempt just retries the same insert with a new number.
            }
        }
    }

    /// <summary>Keeps Asset.Status in sync with the work order lifecycle so nobody has to flip it by
    /// hand: reporting a defect marks the asset Defective, sending it to a vendor marks it under
    /// Maintenance, and closing the fix returns it to Working (unless another work order on the same
    /// asset is still open, or the asset has been Retired — that status is never overridden).</summary>
    private static readonly string[] OpenStages = ["Draft", "New", "Sent to Vendor", "Blocked", "Fixed - Pending Confirmation"];

    private async Task SetAssetStatusAsync(int assetId, string status)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset != null && asset.Status != "Retired") asset.Status = status;
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
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Draft") throw new InvalidOperationException("Only a Draft report can be accepted.");
        if (vendorId == null && wo.AssignedToUserId == null)
            throw new InvalidOperationException("Assign a vendor or an employee before accepting this report.");

        wo.Priority = priority;
        string newStage;
        if (vendorId != null)
        {
            await ValidateVendorAsync(vendorId.Value);
            newStage = "Sent to Vendor";
            // Atomically claim the transition — closes the same race as Force Close/EmployeeFix/
            // ConfirmFix: two concurrent Accept calls could otherwise both pass the Draft check
            // above and each append its own stage-history row.
            var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Draft")
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, newStage).SetProperty(w => w.VendorId, vendorId).SetProperty(w => w.Priority, priority));
            if (rows == 0) throw new InvalidOperationException("Only a Draft report can be accepted.");
            AddStageEvent(wo, "Sent to Vendor", userId);
        }
        else
        {
            // No vendor, only an assigned employee — skip the vendor pipeline entirely and go
            // straight to "New" so the employee's own Report Fix action becomes available.
            newStage = "New";
            var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Draft")
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, newStage).SetProperty(w => w.Priority, priority));
            if (rows == 0) throw new InvalidOperationException("Only a Draft report can be accepted.");
            AddStageEvent(wo, "New", userId);
        }
        await SetAssetStatusAsync(wo.AssetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Accept", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Draft", newValue: newStage);
    }

    public async Task RejectAsync(int workOrderId, string? reason, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Draft") throw new InvalidOperationException("Only a Draft report can be rejected.");
        var newNotes = string.IsNullOrWhiteSpace(reason) ? wo.Notes : $"{wo.Notes}\n\nRejected: {reason}".Trim();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Draft")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, "Rejected").SetProperty(w => w.Notes, newNotes));
        if (rows == 0) throw new InvalidOperationException("Only a Draft report can be rejected.");
        AddStageEvent(wo, "Rejected", userId);
        await SetAssetStatusAsync(wo.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reject", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Draft", newValue: "Rejected", details: reason);
    }

    public async Task SendToVendorAsync(int workOrderId, int vendorId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "New") throw new InvalidOperationException("Only a New work order can be sent to a vendor.");
        await ValidateVendorAsync(vendorId);

        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "New")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, "Sent to Vendor").SetProperty(w => w.VendorId, vendorId));
        if (rows == 0) throw new InvalidOperationException("Only a New work order can be sent to a vendor.");
        AddStageEvent(wo, "Sent to Vendor", userId);
        await SetAssetStatusAsync(wo.AssetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SendToVendor", "WorkOrder", wo.Id.ToString(), userId, oldValue: "New", newValue: "Sent to Vendor");
    }

    // Validates every part is compatible with wo's asset, decrements stock atomically per part
    // (aborting the whole submission if any part's stock is insufficient), and replaces wo.Parts
    // with fresh rows snapshotting Name/UnitCost off the catalog. A tampered/stale picker value
    // must not silently decrement an unrelated part's stock. Caller must have wo.Parts already
    // loaded (Include(w => w.Parts)) and must run this inside the same transaction as the stage
    // transition, since a part failing mid-loop must roll back any parts already decremented.
    private async Task ApplyPartsAsync(WorkOrder wo, List<(int SparePartId, int Quantity)> parts)
    {
        var validParts = parts.Where(p => p.Quantity > 0).ToList();
        _db.WorkOrderParts.RemoveRange(wo.Parts);
        if (validParts.Count == 0) return;

        var compatibleIds = await _db.SparePartAssets.Where(sa => sa.AssetId == wo.AssetId)
            .Select(sa => sa.SparePartId).ToListAsync();
        if (validParts.Select(p => p.SparePartId).Except(compatibleIds).Any())
            throw new InvalidOperationException("One or more selected parts are not compatible with this asset.");

        var partsCatalog = await _db.SpareParts.Where(p => validParts.Select(vp => vp.SparePartId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

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
        }
    }

    public async Task VendorFixAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string vendorUserId)
    {
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Sent to Vendor") throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        ValidateCost(cost);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Sent to Vendor")
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, "Fixed - Pending Confirmation")
                .SetProperty(w => w.FixDescription, description)
                .SetProperty(w => w.FixCost, cost)
                .SetProperty(w => w.FixCompletionDate, completionDate));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        await ApplyPartsAsync(wo, parts);

        AddStageEvent(wo, "Fixed - Pending Confirmation", vendorUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("VendorFix", "WorkOrder", wo.Id.ToString(), vendorUserId, oldValue: "Sent to Vendor", newValue: "Fixed - Pending Confirmation");
    }

    public async Task VendorBlockAsync(int workOrderId, int blockReasonId, string? detail, string vendorUserId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Sent to Vendor") throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        // A non-existent blockReasonId (e.g. a stale dropdown value) used to reach an unhandled
        // FK-constraint DbUpdateException at ExecuteUpdateAsync below - same bug class as the
        // ActionType/Cause/VendorId/employeeUserId FK checks already added elsewhere.
        if (!await _db.WorkOrderBlockReasons.AnyAsync(r => r.Id == blockReasonId))
            throw new InvalidOperationException("Selected block reason not found.");

        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Sent to Vendor")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, "Blocked").SetProperty(w => w.BlockReasonId, blockReasonId).SetProperty(w => w.BlockDetail, detail));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        AddStageEvent(wo, "Blocked", vendorUserId);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VendorBlock", "WorkOrder", wo.Id.ToString(), vendorUserId, oldValue: "Sent to Vendor", newValue: "Blocked", details: detail);
    }

    public async Task ResendToVendorAsync(int workOrderId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Blocked") throw new InvalidOperationException("Only a Blocked work order can be resent.");
        if (wo.VendorId == null) throw new InvalidOperationException("This work order has no vendor to resend to.");

        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Blocked")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, "Sent to Vendor").SetProperty(w => w.BlockReasonId, (int?)null).SetProperty(w => w.BlockDetail, (string?)null));
        if (rows == 0) throw new InvalidOperationException("Only a Blocked work order can be resent.");
        AddStageEvent(wo, "Sent to Vendor", userId);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Resend", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Blocked", newValue: "Sent to Vendor");
    }

    public async Task ConfirmFixAsync(int workOrderId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Fixed - Pending Confirmation") throw new InvalidOperationException("Only a fix pending confirmation can be confirmed.");

        // Same race as ForceCloseAsync: two concurrent confirmations could both pass the in-memory
        // check above before either commits. Claim the transition atomically first.
        var closedDate = DateTime.UtcNow;
        var rows = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.Stage == "Fixed - Pending Confirmation")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, "Closed").SetProperty(w => w.ClosedDate, closedDate));
        if (rows == 0) throw new InvalidOperationException("Only a fix pending confirmation can be confirmed.");

        AddStageEvent(wo, "Closed", userId);
        // Only restore Working if no other work order or standalone maintenance order on this
        // asset is still open — a second, unrelated defect shouldn't get silently cleared just
        // because a different one closed.
        var hasOtherOpenWork = await _db.WorkOrders.AnyAsync(w => w.AssetId == wo.AssetId && w.Id != wo.Id && OpenStages.Contains(w.Stage))
            || await _db.MaintenanceOrders.AnyAsync(m => m.AssetId == wo.AssetId && m.Status == "Open");
        if (!hasOtherOpenWork) await SetAssetStatusAsync(wo.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ConfirmFix", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Fixed - Pending Confirmation", newValue: "Closed");
    }

    public async Task AssignEmployeeAsync(int workOrderId, string? employeeUserId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        var old = wo.AssignedToUserId;
        // A non-existent employeeUserId (e.g. a stale/tampered picker value) used to reach an
        // unhandled FK-constraint DbUpdateException at SaveChangesAsync, leaking the raw SQL
        // error and database/table names to the client. Validate up front like vendor
        // assignment already does via ValidateVendorAsync.
        if (!string.IsNullOrWhiteSpace(employeeUserId) && !await _db.Users.AnyAsync(u => u.Id == employeeUserId))
            throw new InvalidOperationException("Selected employee not found.");
        wo.AssignedToUserId = string.IsNullOrWhiteSpace(employeeUserId) ? null : employeeUserId;
        await _db.SaveChangesAsync();

        // Audit values are shown to admins as-is (e.g. on the per-user profile Audit Log tab) —
        // log the employee's name, not the raw user-id GUID, so the entry is actually readable.
        async Task<string?> NameOf(string? uid) => uid == null ? null
            : await _db.Users.Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync();
        await _audit.LogAsync("AssignEmployee", "WorkOrder", wo.Id.ToString(), userId, oldValue: await NameOf(old), newValue: await NameOf(wo.AssignedToUserId));
    }

    public async Task<WorkOrder> EmployeeFixAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId)
    {
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.AssignedToUserId != employeeUserId) throw new InvalidOperationException("This work order isn't assigned to you.");
        if (wo.VendorId != null) throw new InvalidOperationException("A vendor is already handling this work order.");
        if (wo.Stage != "New") throw new InvalidOperationException("This work order isn't awaiting a fix.");
        ValidateCost(cost);

        // Same race as ForceCloseAsync/ConfirmFixAsync: claim the transition atomically before
        // touching Parts, so two concurrent submissions can't both pass the in-memory check above.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.Stage == "New")
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, "Fixed - Pending Confirmation")
                .SetProperty(w => w.FixDescription, description)
                .SetProperty(w => w.FixCost, cost)
                .SetProperty(w => w.FixCompletionDate, completionDate));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a fix.");

        await ApplyPartsAsync(wo, parts);

        AddStageEvent(wo, "Fixed - Pending Confirmation", employeeUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("EmployeeFix", "WorkOrder", wo.Id.ToString(), employeeUserId, oldValue: "New", newValue: "Fixed - Pending Confirmation");
        return wo;
    }

    /// <summary>Bypasses waiting on the vendor's own response for a work order sitting at "Sent to
    /// Vendor" whose RequiresVendorResponse flag is off — usable by a manager (WorkOrder.Manage) or
    /// the assigned employee. Ends in the same "Fixed - Pending Confirmation" state as VendorFixAsync/
    /// EmployeeFixAsync so ConfirmFixAsync works unchanged regardless of who actually reported the fix.</summary>
    public async Task<WorkOrder> AdvanceWithoutVendorAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string userId, bool isManager = false)
    {
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.RequiresVendorResponse) throw new InvalidOperationException("This work order requires a vendor response.");
        if (wo.Stage != "Sent to Vendor") throw new InvalidOperationException("This work order isn't awaiting a vendor response.");
        if (!isManager && wo.AssignedToUserId != userId) throw new InvalidOperationException("This work order isn't assigned to you.");
        ValidateCost(cost);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rows = await _db.WorkOrders.Where(w => w.Id == workOrderId && w.Stage == "Sent to Vendor")
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, "Fixed - Pending Confirmation")
                .SetProperty(w => w.FixDescription, description)
                .SetProperty(w => w.FixCost, cost)
                .SetProperty(w => w.FixCompletionDate, completionDate));
        if (rows == 0) throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        await ApplyPartsAsync(wo, parts);

        AddStageEvent(wo, "Fixed - Pending Confirmation", userId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("AdvanceWithoutVendor", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Sent to Vendor", newValue: "Fixed - Pending Confirmation");
        return wo;
    }

    public async Task ForceCloseAsync(int workOrderId, string? reason, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage == "Closed") throw new InvalidOperationException("Already closed.");
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
        var rows = await _db.WorkOrders
            .Where(w => w.Id == workOrderId && w.Stage != "Closed")
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Stage, "Closed")
                .SetProperty(w => w.ClosedDate, closedDate)
                .SetProperty(w => w.Notes, newNotes));
        if (rows == 0) throw new InvalidOperationException("Already closed.");

        AddStageEvent(wo, "Closed", userId);
        var hasOtherOpenWork = await _db.WorkOrders.AnyAsync(w => w.AssetId == wo.AssetId && w.Id != wo.Id && OpenStages.Contains(w.Stage))
            || await _db.MaintenanceOrders.AnyAsync(m => m.AssetId == wo.AssetId && m.Status == "Open");
        if (!hasOtherOpenWork) await SetAssetStatusAsync(wo.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ForceClose", "WorkOrder", wo.Id.ToString(), userId, oldValue: old, newValue: "Closed", details: reason);
    }

    public async Task UpdatePriorityAsync(int workOrderId, string priority, string userId)
    {
        if (!WorkOrder.Priorities.Contains(priority)) throw new InvalidOperationException("Invalid priority.");
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage is not ("Draft" or "New")) throw new InvalidOperationException("Priority can only be changed before a work order is sent to a vendor.");
        if (wo.Priority == priority) return;
        var oldPriority = wo.Priority;
        wo.Priority = priority;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("UpdatePriority", "WorkOrder", wo.Id.ToString(), userId, oldValue: oldPriority, newValue: priority);
    }

    public async Task<WorkOrder> CreatePreventiveMaintenanceOccurrenceAsync(int assetId, int vendorId, int sourceContractId, DateTime scheduledDate, string? contractNumber, string systemUserId)
    {
        var year = DateTime.UtcNow.Year;
        var seq = await _db.WorkOrders.CountAsync(w => w.CreatedDate.Year == year) + 1;
        var contractLabel = string.IsNullOrWhiteSpace(contractNumber) ? sourceContractId.ToString() : contractNumber;
        var wo = new WorkOrder
        {
            WorkOrderNumber = $"WO-{year}-{seq:D4}",
            AssetId = assetId,
            VendorId = vendorId,
            SourceContractId = sourceContractId,
            ScheduledDate = scheduledDate.Date,
            Stage = "Sent to Vendor",
            Priority = "Medium",
            Description = $"Preventive Maintenance — due {scheduledDate:yyyy-MM-dd} (Contract {contractLabel})",
            CreatedByUserId = systemUserId,
            CreatedDate = DateTime.UtcNow,
            RequiresVendorResponse = true,
        };
        wo.StageEvents.Add(new WorkOrderStageEvent { Stage = "Sent to Vendor", ChangedAt = DateTime.UtcNow, ChangedByUserId = systemUserId });
        _db.WorkOrders.Add(wo);
        await SetAssetStatusAsync(assetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AutoGeneratePM", "WorkOrder", wo.Id.ToString(), systemUserId,
            newValue: wo.WorkOrderNumber, details: $"Contract #{sourceContractId}, due {scheduledDate:yyyy-MM-dd}");
        return wo;
    }

    private async Task<List<WorkOrder>> GetExportRowsAsync() =>
        await _db.WorkOrders.Include(w => w.Asset).Include(w => w.Vendor).OrderByDescending(w => w.CreatedDate).ToListAsync();

    public async Task<byte[]> ExportToExcelAsync()
    {
        var orders = await GetExportRowsAsync();
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

    public async Task<byte[]> ExportToPdfAsync()
    {
        var orders = await GetExportRowsAsync();
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            var doc = new Document(pdf);
            doc.Add(new Paragraph("Work Orders").SetBold().SetFontSize(16));
            // Equal-width columns (the old `new Table(7, true)`) squeezed "Work Order #" values
            // like "WO-2026-0020" into a column too narrow to fit on one line, wrapping mid-string
            // at the hyphen. Widening that column alone wasn't enough — Document's default 12pt
            // body font left even "AST-0001" (8 chars) wrapping in an 8-char-wide Asset column, so
            // the whole table needed a smaller font, not just different column proportions.
            var table = new Table(new float[] { 2.2f, 1.4f, 1f, 1.3f, 1.6f, 1.1f, 1.1f }).UseAllAvailableWidth().SetFontSize(8);
            foreach (var h in new[] { "Work Order #", "Asset", "Priority", "Stage", "Vendor", "Created", "Closed" })
                table.AddHeaderCell(h);
            foreach (var w in orders)
            {
                table.AddCell(w.WorkOrderNumber);
                table.AddCell(w.Asset?.AssetTag ?? "");
                table.AddCell(w.Priority);
                table.AddCell(w.Stage);
                table.AddCell(w.Vendor?.Name ?? "");
                table.AddCell(w.CreatedDate.ToString("yyyy-MM-dd"));
                table.AddCell(w.ClosedDate?.ToString("yyyy-MM-dd") ?? "");
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
