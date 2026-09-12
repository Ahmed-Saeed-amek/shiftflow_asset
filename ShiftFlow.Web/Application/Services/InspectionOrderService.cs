using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Services;

namespace ShiftFlow.Application.Services;

public class InspectionOrderService : IInspectionOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IGroupService _groups;
    private readonly IAssetScopeService _scope;
    private readonly IWorkOrderService _workOrders;

    public InspectionOrderService(ApplicationDbContext db, IAuditService audit, IGroupService groups, IAssetScopeService scope, IWorkOrderService workOrders)
    {
        _db = db;
        _audit = audit;
        _groups = groups;
        _scope = scope;
        _workOrders = workOrders;
    }

    /// <summary>Re-derives the same AssignmentMode rule OrdersController.Create already enforces for
    /// the manual-create path — every other caller (RecurringOrderSchedulerService, and now
    /// ReassignAsync) reaches this service directly, so the check belongs here too or it's silently
    /// bypassable (confirmed live for Reassign: an EmployeeOnly-typed order could be reassigned to a
    /// Group with no error).</summary>
    private static void ValidateAssignmentMode(string? mode, bool hasUser, bool hasGroup)
    {
        if (mode == "EmployeeOnly" && hasGroup)
            throw new InvalidOperationException("This order type can only be assigned to an employee, not a group.");
        if (mode == "GroupOnly" && hasUser)
            throw new InvalidOperationException("This order type can only be assigned to a group, not an employee.");
    }

    // A stale/tampered maintenance-action-type checkbox value otherwise hits the DB's Restrict FK
    // constraint on InspectionItemMaintenanceActions.MaintenanceActionTypeId and raises an unhandled
    // DbUpdateException — same bug class already guarded against for ActionType/Cause/VendorId/
    // employeeUserId elsewhere (e.g. WorkOrderService.VendorBlockAsync's block-reason check). Confirmed
    // live: posting a non-existent id to UpdateMaintenanceActions leaked a raw SQL FK-violation error.
    private async Task EnsureMaintenanceActionTypesExistAsync(List<int>? maintenanceActionTypeIds)
    {
        if (maintenanceActionTypeIds is null || maintenanceActionTypeIds.Count == 0) return;
        var distinctIds = maintenanceActionTypeIds.Distinct().ToList();
        var existingCount = await _db.MaintenanceActionTypes.CountAsync(t => distinctIds.Contains(t.Id));
        if (existingCount != distinctIds.Count)
            throw new InvalidOperationException("One or more selected maintenance actions were not found.");
    }

    public async Task<InspectionOrder> CreateAsync(int orderTypeId, string? description, string? assignedToUserId, int? assignedToGroupId,
        List<int>? assetIds, DateTime? dueDate, string createdByUserId, int? sourceRecurringOrderId = null, DateTime? scheduledDate = null)
    {
        var orderType = await _db.OrderTypes.FirstOrDefaultAsync(t => t.Id == orderTypeId && t.IsActive)
            ?? throw new InvalidOperationException("Invalid order type.");

        var hasUser = !string.IsNullOrEmpty(assignedToUserId);
        var hasGroup = assignedToGroupId.HasValue;
        if (hasUser == hasGroup)
            throw new InvalidOperationException("Select exactly one assignee — a single employee or a Group.");
        ValidateAssignmentMode(orderType.AssignmentMode, hasUser, hasGroup);
        // Friendly error for a nonexistent user/group id instead of a raw FK-constraint failure.
        if (hasUser && !await _db.Users.AnyAsync(u => u.Id == assignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
        if (hasGroup && !await _db.Groups.AnyAsync(t => t.Id == assignedToGroupId))
            throw new InvalidOperationException("Selected group not found.");

        // De-duplicated: the same asset picked twice used to produce two InspectionRunAsset rows
        // for one order, each needing its own outcome.
        var resolvedAssetIds = (assetIds ?? []).Distinct().ToList();
        if (resolvedAssetIds.Count == 0)
            throw new InvalidOperationException("Select at least one asset to inspect.");
        // Same guard as MaintenanceOrderService.CreateAsync — without it, a recurring schedule (or
        // the AI assistant tool) keeps generating new orders against an asset retired after the
        // schedule was created, since OrdersController's own retired-asset check only covers its
        // own manual-create path, not every caller of this method.
        var retiredTags = await _db.Assets.Where(a => resolvedAssetIds.Contains(a.Id) && a.Status == "Retired")
            .Select(a => a.AssetTag).ToListAsync();
        if (retiredTags.Count > 0)
            // Names the offending asset(s) — with a multi-asset picker, "one or more" left the
            // caller no way to tell which of several selected assets to remove without trial and
            // error (confirmed live: submitting AST-0006 + AST-0007 together gave no indication
            // AST-0006 was the retired one).
            throw new InvalidOperationException("These assets are retired and can't have new orders opened against them: " + string.Join(", ", retiredTags));
        // AssetsController routes every single-asset read through ScopedAssetsAsync so a
        // UserAssetScope-restricted user can't view an out-of-scope asset — but this method (reached
        // directly by OrdersController.Create, the AI assistant, and the recurring scheduler) had no
        // equivalent check, so that same user could still open an inspection order against an asset
        // they can't view, just by knowing/guessing its ID.
        var inScopeCount = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), createdByUserId))
            .CountAsync(a => resolvedAssetIds.Contains(a.Id));
        if (inScopeCount != resolvedAssetIds.Count)
            throw new InvalidOperationException("One or more selected assets were not found.");

        // Provenance only (not used for resolution): if every picked asset happens to share one
        // Zone, record it so Zone-scoped reporting/display can rely on it; else left null.
        var distinctZoneIds = await _db.Assets.Where(a => resolvedAssetIds.Contains(a.Id))
            .Select(a => a.ZoneId).Distinct().ToListAsync();
        int? singleZoneId = distinctZoneIds.Count == 1 ? distinctZoneIds[0] : null;

        var order = new InspectionOrder
        {
            Description = description,
            OrderTypeId = orderType.Id,
            AssignedToUserId = hasUser ? assignedToUserId : null,
            AssignedToGroupId = hasGroup ? assignedToGroupId : null,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            DueDate = dueDate,
            Status = OrderStatuses.Open,
            SourceRecurringOrderId = sourceRecurringOrderId,
            ScheduledDate = scheduledDate,
            InspectionRun = new InspectionRun
            {
                ZoneId = singleZoneId,
                Items = resolvedAssetIds.Select(id => new InspectionRunAsset { AssetId = id }).ToList(),
            },
        };
        await AssignNumberAsync(order, orderType.Prefix);
        _db.InspectionOrders.Add(order);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "InspectionOrder", order.Id.ToString(), createdByUserId, newValue: order.OrderNumber);
        return order;
    }

    /// <summary>Order numbers come from the shared, atomically-claimed OrderNumberSequence keyed by
    /// the order type's own Prefix (see OrderNumberGenerator) - allocated before the entity is
    /// added, so the insert is a plain SaveChanges with no duplicate-number retry loop.</summary>
    private async Task AssignNumberAsync(InspectionOrder order, string prefix) =>
        order.OrderNumber = await OrderNumberGenerator.NextAsync(_db, prefix, order.CreatedAt.Year);

    public async Task<InspectionOrder?> GetByIdAsync(int id) =>
        await _db.InspectionOrders.AsNoTracking()
            .Include(o => o.OrderType)
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToGroup).ThenInclude(t => t!.Members).ThenInclude(m => m.User)
            .Include(o => o.CreatedByUser)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Zone)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items).ThenInclude(i => i.Asset).ThenInclude(a => a.Zone)
                .ThenInclude(z => z!.LocationCategory)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items).ThenInclude(i => i.WorkOrder)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items).ThenInclude(i => i.MaintenanceActions).ThenInclude(m => m.MaintenanceActionType)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<List<InspectionOrder>> GetMyOrdersAsync(string userId, bool includeDone = false, DateTime? from = null, DateTime? to = null)
    {
        var myGroupIds = await _db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();

        var query = _db.InspectionOrders.AsNoTracking()
            .Include(o => o.OrderType)
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToGroup)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .Where(o => o.AssignedToUserId == userId || (o.AssignedToGroupId != null && myGroupIds.Contains(o.AssignedToGroupId.Value)));

        if (!includeDone)
            query = query.Where(o => o.Status != OrderStatuses.Done && o.Status != OrderStatuses.Cancelled);
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt <= to.Value);

        return await query.OrderByDescending(o => o.CreatedAt).Take(300).ToListAsync();
    }

    public async Task<List<InspectionOrder>> GetAllAsync(string? status, string? search, bool overdue, string userId)
    {
        var query = _db.InspectionOrders.AsNoTracking()
            .Include(o => o.OrderType)
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToGroup)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .AsQueryable();

        // Details (round 12) 404s a scoped user out of an order with any out-of-scope asset — this
        // list must hide the same rows, or a scoped user sees every order exist (asset tag, status,
        // assignee) in the list and only gets blocked one click later on Details.
        if (await _scope.HasScopeAsync(userId))
        {
            var scopedAssetIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).Select(a => a.Id).ToListAsync();
            query = query.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SearchQuery.Cap(search.Trim())!;
            query = query.Where(o => o.OrderNumber.Contains(term));
        }

        // Same definition as the Dashboard's Overdue Orders KPI/card, so the "View all" link
        // there actually lands on the same set instead of the unfiltered full list.
        if (overdue)
        {
            var today = DateTime.UtcNow.Date;
            query = query.Where(o => o.Status != OrderStatuses.Done && o.Status != OrderStatuses.Cancelled && o.DueDate != null && o.DueDate < today);
        }

        return await query.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
    }

    /// <summary>The one place an inspection item's outcome is recorded. A "Defective" outcome
    /// spawns the tracking Work Order here, inside this method's own transaction - the two used to
    /// be separate commits in each caller (controller + AI tool), so a failure in the second left a
    /// committed Work Order (and an asset already flipped to Defective) with no item pointing at
    /// it. Returns the new Work Order's id, or null when none was created.</summary>
    public async Task<int?> UpdateInspectionItemAsync(int itemId, string outcome, int? actionTypeId, int? causeId, string? notes, string updatedByUserId)
    {
        var item = await _db.InspectionRunAssets.FindAsync(itemId)
            ?? throw new InvalidOperationException("Inspection item not found.");
        if (outcome == InspectionOutcomes.Pending || !InspectionRunAsset.Outcomes.Contains(outcome))
            throw new InvalidOperationException("Invalid outcome.");

        var order = await LoadOrderForItemAsync(item, includeOrderType: true);
        await EnsureItemActionableAsync(order, item, updatedByUserId);
        if (order.Status is OrderStatuses.Done or OrderStatuses.PendingApproval or OrderStatuses.Cancelled)
            throw new InvalidOperationException("This inspection order is already closed.");
        // Types that TracksDefectOutcome require Action Type + Cause; other types still spawn a
        // Work Order for tracking, with both left null.
        if (outcome == InspectionOutcomes.Defective && (order.OrderType?.TracksDefectOutcome ?? false)
            && (actionTypeId == null || causeId == null))
            throw new InvalidOperationException("Action Type and Cause are required to report a defect.");

        // Everything below runs inside one transaction so a concurrent Cancel racing this method
        // rolls the whole thing back - the item's outcome, the order's status flip, and the Work
        // Order the defect spawned.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var claimed = await _db.InspectionOrders.Where(o => o.Id == order.Id && o.Status != OrderStatuses.Done && o.Status != OrderStatuses.PendingApproval && o.Status != OrderStatuses.Cancelled)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, o => o.Status == OrderStatuses.Open ? OrderStatuses.InProgress : o.Status));
        if (claimed == 0) throw new InvalidOperationException("This inspection order is already closed.");

        int? workOrderId = null;
        if (outcome == InspectionOutcomes.Defective)
        {
            var tracks = order.OrderType?.TracksDefectOutcome ?? false;
            var wo = await _workOrders.ReportAsync(new WorkOrder
            {
                AssetId = item.AssetId,
                ActionTypeId = tracks ? actionTypeId : null,
                CauseId = tracks ? causeId : null,
                Notes = notes,
                RequiresVendorResponse = order.OrderType?.RequiresVendor ?? false,
            }, updatedByUserId);
            workOrderId = wo.Id;
        }

        item.Outcome = outcome;
        item.InspectedByUserId = updatedByUserId;
        item.InspectedAt = DateTime.UtcNow;
        item.WorkOrderId = workOrderId;

        // Maintenance actions belong to UpdateMaintenanceActionsAsync alone - confirming an outcome
        // must not wipe actions already logged for this item.
        await _db.SaveChangesAsync();

        var runId = item.InspectionRunId;
        var stillPending = await _db.InspectionRunAssets.AnyAsync(i => i.InspectionRunId == runId && i.Outcome == InspectionOutcomes.Pending);
        if (!stillPending)
        {
            var requiresApproval = order.OrderType?.RequiresApproval ?? false;
            var newStatus = requiresApproval ? OrderStatuses.PendingApproval : OrderStatuses.Done;
            var closedAt = requiresApproval ? (DateTime?)null : DateTime.UtcNow;
            var claimedFinal = await _db.InspectionOrders.Where(o => o.Id == order.Id && o.Status != OrderStatuses.Done && o.Status != OrderStatuses.PendingApproval && o.Status != OrderStatuses.Cancelled)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, newStatus).SetProperty(o => o.ClosedAt, closedAt));
            if (claimedFinal == 0) throw new InvalidOperationException("This inspection order is already closed.");
        }

        await tx.CommitAsync();
        await _audit.LogAsync("UpdateInspectionItem", "InspectionRunAsset", itemId.ToString(), updatedByUserId, newValue: outcome);
        return workOrderId;
    }

    private async Task<InspectionOrder> LoadOrderForItemAsync(InspectionRunAsset item, bool includeOrderType)
    {
        var orderId = await _db.InspectionRuns.Where(r => r.Id == item.InspectionRunId)
            .Select(r => r.InspectionOrderId).FirstAsync();
        var query = _db.InspectionOrders.AsQueryable();
        if (includeOrderType) query = query.Include(o => o.OrderType);
        return await query.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Inspection order not found.");
    }

    /// <summary>The shared assignee/scope gate for both per-item write paths (outcome and
    /// maintenance actions). A scope narrowed/added after the order was assigned must not lock the
    /// legitimate assignee/group member out of their own already-assigned work; anyone else -
    /// a manager acting on someone else's order, the AI assistant - gets the strict scope check.</summary>
    private async Task EnsureItemActionableAsync(InspectionOrder order, InspectionRunAsset item, string userId)
    {
        var isAssigneeOrGroupMember = order.AssignedToUserId == userId
            || (order.AssignedToGroupId.HasValue && await _groups.IsMemberAsync(order.AssignedToGroupId.Value, userId));
        if (isAssigneeOrGroupMember) return;
        if (!await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).AnyAsync(a => a.Id == item.AssetId))
            throw new InvalidOperationException("Inspection item not found.");
        await EnsureOrderInScopeAsync(order.Id, userId);
    }

    // Details (round 12) blocks a scoped user from even viewing an out-of-scope Inspection Order,
    // but every mutating action below still took only a bare orderId — a scoped user who can't
    // view an order via Details could still Approve/Cancel/Reassign it via a direct POST with a
    // guessed ID, since none of these re-checked scope. All of an order's assets must be in scope.
    private async Task EnsureOrderInScopeAsync(int orderId, string userId)
    {
        var assetIds = await _db.InspectionRunAssets.Where(i => i.InspectionRun!.InspectionOrderId == orderId)
            .Select(i => i.AssetId).Distinct().ToListAsync();
        if (assetIds.Count == 0) return;
        var inScopeCount = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).CountAsync(a => assetIds.Contains(a.Id));
        if (inScopeCount != assetIds.Count) throw new InvalidOperationException("Inspection order not found.");
    }

    /// <summary>Manager sign-off for an order whose OrderType.RequiresApproval is true — the only
    /// way a PendingApproval order can actually finalize to Done.</summary>
    public async Task ApproveAsync(int orderId, string managerUserId)
    {
        var order = await _db.InspectionOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Inspection order not found.");
        await EnsureOrderInScopeAsync(orderId, managerUserId);
        if (order.Status != OrderStatuses.PendingApproval) throw new InvalidOperationException("This order isn't awaiting approval.");
        // Claim atomically - a load/check/save would let a concurrent Cancel be overwritten to Done.
        var closedAt = DateTime.UtcNow;
        var claimed = await _db.InspectionOrders.Where(o => o.Id == orderId && o.Status == OrderStatuses.PendingApproval)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatuses.Done).SetProperty(o => o.ClosedAt, closedAt));
        if (claimed == 0) throw new InvalidOperationException("This order isn't awaiting approval.");
        await _audit.LogAsync("Approve", "InspectionOrder", order.Id.ToString(), managerUserId, oldValue: OrderStatuses.PendingApproval, newValue: OrderStatuses.Done);
    }

    public async Task UpdateMaintenanceActionsAsync(int itemId, List<int>? maintenanceActionTypeIds, string updatedByUserId)
    {
        var item = await _db.InspectionRunAssets.FindAsync(itemId)
            ?? throw new InvalidOperationException("Inspection item not found.");
        await EnsureMaintenanceActionTypesExistAsync(maintenanceActionTypeIds);

        var order = await LoadOrderForItemAsync(item, includeOrderType: false);
        // Same assignee/scope gate the outcome path uses - this had none at all, so any signed-in
        // user could log maintenance actions on any item by id.
        await EnsureItemActionableAsync(order, item, updatedByUserId);
        // Aligned with the outcome path: once every item is reported the order is frozen, whether
        // it went to PendingApproval or straight to Done.
        if (order.Status is OrderStatuses.Done or OrderStatuses.PendingApproval or OrderStatuses.Cancelled)
            throw new InvalidOperationException("This inspection order is already closed.");

        // Deliberately does not touch Outcome/InspectedByUserId/InspectedAt/WorkOrderId, or the
        // order's Open->InProgress/Done status transitions — those all belong to the OK/Defective
        // decision (UpdateInspectionItemAsync above). This lets maintenance actually performed be
        // logged independent of that decision, and safely re-editable afterward, since there's no
        // Work Order (re-)creation here to risk duplicating.
        _db.InspectionItemMaintenanceActions.RemoveRange(
            await _db.InspectionItemMaintenanceActions.Where(m => m.InspectionRunAssetId == itemId).ToListAsync());
        foreach (var maintenanceActionTypeId in maintenanceActionTypeIds ?? [])
            _db.InspectionItemMaintenanceActions.Add(new InspectionItemMaintenanceAction { InspectionRunAssetId = itemId, MaintenanceActionTypeId = maintenanceActionTypeId });

        await _db.SaveChangesAsync();
        var actionNames = await _db.MaintenanceActionTypes
            .Where(t => (maintenanceActionTypeIds ?? new List<int>()).Contains(t.Id))
            .Select(t => t.Name).ToListAsync();
        await _audit.LogAsync("UpdateMaintenanceActions", "InspectionRunAsset", itemId.ToString(), updatedByUserId,
            newValue: actionNames.Count > 0 ? string.Join(", ", actionNames) : "(none)");
    }

    public async Task CancelAsync(int orderId, string? reason, string userId)
    {
        var order = await _db.InspectionOrders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Inspection order not found.");
        await EnsureOrderInScopeAsync(orderId, userId);
        if (order.Status is OrderStatuses.Done or OrderStatuses.Cancelled)
            throw new InvalidOperationException("A completed or already-cancelled inspection order cannot be cancelled.");
        var oldStatus = order.Status;

        // Claim the transition atomically — a manager's Cancel and an assignee's item-outcome update
        // (which can independently drive the order to Done/PendingApproval via
        // UpdateInspectionItemAsync) could otherwise both pass the in-memory status check above,
        // and whichever SaveChanges lands last silently overwrites the other's result. Same race
        // class MaintenanceOrderService.CancelAsync/CompleteAsync already guard against.
        var closedAt = DateTime.UtcNow;
        var claimed = await _db.InspectionOrders.Where(o => o.Id == orderId && o.Status != OrderStatuses.Done && o.Status != OrderStatuses.Cancelled)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatuses.Cancelled).SetProperty(o => o.ClosedAt, closedAt));
        if (claimed == 0) throw new InvalidOperationException("A completed or already-cancelled inspection order cannot be cancelled.");

        await _audit.LogAsync("Cancel", "InspectionOrder", orderId.ToString(), userId, oldValue: oldStatus, newValue: OrderStatuses.Cancelled, details: reason);
    }

    public async Task ReassignAsync(int orderId, string? assignedToUserId, int? assignedToGroupId, string managerUserId)
    {
        var order = await _db.InspectionOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Inspection order not found.");
        await EnsureOrderInScopeAsync(orderId, managerUserId);
        if (order.Status is OrderStatuses.Done or OrderStatuses.Cancelled) throw new InvalidOperationException("A closed inspection order can't be reassigned.");
        var hasUser = !string.IsNullOrWhiteSpace(assignedToUserId);
        var hasGroup = assignedToGroupId.HasValue;
        if (hasUser == hasGroup) throw new InvalidOperationException("Select exactly one assignee — a single employee or a Group.");
        var assignmentMode = await _db.OrderTypes.Where(t => t.Id == order.OrderTypeId).Select(t => t.AssignmentMode).FirstOrDefaultAsync();
        ValidateAssignmentMode(assignmentMode, hasUser, hasGroup);
        if (hasUser && !await _db.Users.AnyAsync(u => u.Id == assignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
        if (hasGroup && !await _db.Groups.AnyAsync(t => t.Id == assignedToGroupId))
            throw new InvalidOperationException("Selected group not found.");

        var oldLabel = order.AssignedToUserId ?? (order.AssignedToGroupId.HasValue ? $"Group #{order.AssignedToGroupId}" : "—");
        // Claim atomically against the DB's current status, not the copy loaded above — a concurrent
        // Cancel or item-outcome update (which can independently drive the order to Done via
        // UpdateInspectionItemAsync) could otherwise close the order between that load and this
        // write, and this Reassign would still apply, permanently misattributing a closed order to
        // someone who never touched it. Same race class Cancel/Complete already guard against.
        var newAssignedToUserId = hasUser ? assignedToUserId : null;
        var newAssignedToGroupId = hasGroup ? assignedToGroupId : null;
        var claimed = await _db.InspectionOrders.Where(o => o.Id == orderId && o.Status != OrderStatuses.Done && o.Status != OrderStatuses.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.AssignedToUserId, newAssignedToUserId)
                .SetProperty(o => o.AssignedToGroupId, newAssignedToGroupId));
        if (claimed == 0) throw new InvalidOperationException("A closed inspection order can't be reassigned.");
        await _audit.LogAsync("Reassign", "InspectionOrder", order.Id.ToString(), managerUserId,
            oldValue: oldLabel, newValue: hasUser ? assignedToUserId : $"Group #{assignedToGroupId}");
    }

    public async Task<byte[]> ExportToExcelAsync(string userId)
    {
        var query = _db.InspectionOrders.AsNoTracking()
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToGroup)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .AsQueryable();
        // Same scope enforcement as GetAllAsync — an export must not dump orders the exporting
        // user can't even see in the list, matching round 3's fix for Assets export.
        if (await _scope.HasScopeAsync(userId))
        {
            var scopedAssetIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).Select(a => a.Id).ToListAsync();
            query = query.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        }
        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Inspection Orders");
        string[] headers = ["Order #", "Status", "Assigned To", "Assets", "Checked", "Due Date", "Created"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var o in orders)
        {
            var items = o.InspectionRun?.Items ?? [];
            ws.Cells[row, 1].Value = o.OrderNumber;
            ws.Cells[row, 2].Value = o.Status;
            ws.Cells[row, 3].Value = o.AssignedToUser?.FullName ?? (o.AssignedToGroup != null ? $"Group: {o.AssignedToGroup.Name}" : "");
            ws.Cells[row, 4].Value = items.Count;
            ws.Cells[row, 5].Value = items.Count(i => i.Outcome != InspectionOutcomes.Pending);
            ws.Cells[row, 6].Value = o.DueDate?.ToString("yyyy-MM-dd");
            ws.Cells[row, 7].Value = o.CreatedAt.ToString("yyyy-MM-dd");
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }
}
