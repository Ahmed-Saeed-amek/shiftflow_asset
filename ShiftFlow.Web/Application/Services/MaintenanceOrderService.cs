using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Services;

namespace ShiftFlow.Application.Services;

public class MaintenanceOrderService : IMaintenanceOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ISparePartService _spareParts;
    private readonly IGroupService _groups;
    private readonly IAssetScopeService _scope;
    public MaintenanceOrderService(ApplicationDbContext db, IAuditService audit, ISparePartService spareParts, IGroupService groups, IAssetScopeService scope) { _db = db; _audit = audit; _spareParts = spareParts; _groups = groups; _scope = scope; }

    // Stages/statuses (across both entities) that mean "this asset still has open work" —
    // checked before restoring Asset.Status to "Working" so a second, unrelated issue on the
    // same asset doesn't get silently cleared out from under it.
    private static readonly string[] OpenWorkOrderStages = ["Draft", "New", "Sent to Vendor", "Blocked", "Fixed - Pending Confirmation"];

    private async Task SetAssetStatusAsync(int assetId, string status)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset != null && asset.Status != "Retired") asset.Status = status;
    }

    // Details (round 12) blocks a scoped user from even viewing an out-of-scope Maintenance Order,
    // but every mutating action below still took only a bare orderId — a scoped user who can't
    // view an order via Details could still Complete/Cancel/Approve/Reassign it via a direct POST
    // with a guessed ID, since none of these re-checked scope.
    private async Task EnsureOrderInScopeAsync(int assetId, string userId)
    {
        var inScope = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).AnyAsync(a => a.Id == assetId);
        if (!inScope) throw new InvalidOperationException("Maintenance order not found.");
    }

    private async Task<bool> HasOtherOpenWorkAsync(int assetId, int excludeMaintenanceOrderId) =>
        await _db.MaintenanceOrders.AnyAsync(m => m.AssetId == assetId && m.Id != excludeMaintenanceOrderId && m.Status == "Open")
        || await _db.WorkOrders.AnyAsync(w => w.AssetId == assetId && OpenWorkOrderStages.Contains(w.Stage));

    /// <summary>Re-derives the same AssignmentMode rule OrdersController.Create already enforces for
    /// the manual-create path — every OTHER caller (RecurringOrderSchedulerService, and now
    /// ReassignAsync) reaches CreateAsync/updates the assignee directly, so the check belongs here
    /// too or it's silently bypassable (confirmed live for Reassign: an EmployeeOnly-typed order
    /// could be reassigned to a Group with no error). orderTypeId null (legacy rows with no catalog
    /// entry) skips the check — there's no AssignmentMode to enforce.</summary>
    private async Task ValidateAssignmentModeAsync(int? orderTypeId, bool hasUser, bool hasGroup)
    {
        if (orderTypeId == null) return;
        var mode = await _db.OrderTypes.Where(t => t.Id == orderTypeId).Select(t => t.AssignmentMode).FirstOrDefaultAsync();
        if (mode == "EmployeeOnly" && hasGroup)
            throw new InvalidOperationException("This order type can only be assigned to an employee, not a group.");
        if (mode == "GroupOnly" && hasUser)
            throw new InvalidOperationException("This order type can only be assigned to a group, not an employee.");
    }

    public async Task<MaintenanceOrder> CreateAsync(int assetId, string? assignedToUserId, int? assignedToGroupId, string? description, DateTime? dueDate, string createdByUserId, int? orderTypeId = null, int? sourceRecurringOrderId = null, DateTime? scheduledDate = null)
    {
        var hasUser = !string.IsNullOrWhiteSpace(assignedToUserId);
        var hasGroup = assignedToGroupId.HasValue;
        if (hasUser == hasGroup)
            throw new InvalidOperationException("Select exactly one assignee — a single employee or a Group.");
        await ValidateAssignmentModeAsync(orderTypeId, hasUser, hasGroup);
        // A nonexistent user/group ID (stale form repost, hallucinated AI tool argument) otherwise
        // reaches an unhandled FK-constraint DbUpdateException at SaveWithUniqueNumberRetryAsync —
        // same bug class as the VendorId existence check already added elsewhere (ContractService).
        if (hasUser && !await _db.Users.AnyAsync(u => u.Id == assignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
        if (hasGroup && !await _db.Groups.AnyAsync(t => t.Id == assignedToGroupId))
            throw new InvalidOperationException("Selected group not found.");
        if (await _db.Assets.AnyAsync(a => a.Id == assetId && a.Status == "Retired"))
            throw new InvalidOperationException("This asset is retired and can't have new orders opened against it.");
        // AssetsController routes every single-asset read through ScopedAssetsAsync so a
        // UserAssetScope-restricted user can't view an out-of-scope asset — but this method (reached
        // directly by OrdersController.Create, the AI assistant, and the recurring scheduler) had no
        // equivalent check, so that same user could still open a maintenance order against an asset
        // they can't view, just by knowing/guessing its ID.
        if (!await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), createdByUserId)).AnyAsync(a => a.Id == assetId))
            throw new InvalidOperationException("Asset not found.");

        var order = new MaintenanceOrder
        {
            AssetId = assetId,
            AssignedToUserId = hasUser ? assignedToUserId : null,
            AssignedToGroupId = hasGroup ? assignedToGroupId : null,
            Description = description,
            DueDate = dueDate,
            CreatedByUserId = createdByUserId,
            CreatedDate = DateTime.UtcNow,
            Status = "Open",
            OrderTypeId = orderTypeId,
            SourceRecurringOrderId = sourceRecurringOrderId,
            ScheduledDate = scheduledDate,
        };
        _db.MaintenanceOrders.Add(order);
        await SetAssetStatusAsync(assetId, "Maintenance");
        await SaveWithUniqueNumberRetryAsync(order);
        await _audit.LogAsync("Create", "MaintenanceOrder", order.Id.ToString(), createdByUserId, newValue: order.OrderNumber);
        return order;
    }

    /// <summary>Same fix as WorkOrderService/InspectionOrderService's identically-named helper.
    /// OrderNumber "MO-{year}-{seq:D4}" used to be COUNT(*)+1, which is wrong the moment a row is
    /// ever hard-deleted (or a batch create's retry loop backs off) and leaves a gap: COUNT stays
    /// permanently one short of the real next sequence, so it recomputes the exact same colliding
    /// number on every retry attempt and can never actually get past the gap (confirmed live — a
    /// deleted test row left every subsequent create's COUNT+1 permanently landing on an
    /// already-used number, 500ing on a unique-index violation). Base the sequence on the highest
    /// existing number for the year instead of a count, and advance it by the attempt number on
    /// retry so a genuine concurrent-insert race still makes progress each time.</summary>
    private async Task SaveWithUniqueNumberRetryAsync(MaintenanceOrder order)
    {
        var year = order.CreatedDate.Year;
        var prefix = $"MO-{year}-";
        var existingNumbers = await _db.MaintenanceOrders
            .Where(m => m.OrderNumber.StartsWith(prefix))
            .Select(m => m.OrderNumber)
            .ToListAsync();
        var nextSeq = existingNumbers.Count == 0 ? 1
            : existingNumbers.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var s) ? s : 0).Max() + 1;

        for (var attempt = 0; ; attempt++)
        {
            order.OrderNumber = $"{prefix}{nextSeq + attempt:D4}";
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                // Concurrent insert claimed this number first — advance to the next one and retry.
            }
        }
    }

    public async Task<MaintenanceOrder> CompleteAsync(int orderId, DateTime? completedDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId)
    {
        var order = await _db.MaintenanceOrders.Include(m => m.Parts).Include(m => m.OrderType).FirstOrDefaultAsync(m => m.Id == orderId)
            ?? throw new InvalidOperationException("Maintenance order not found.");
        // Same "assignee or group member" rule as InspectionOrder — a Group-assigned order can be
        // completed by any member, not just whoever happens to be recorded as AssignedToUserId
        // (which is null for a Group-assigned order in the first place).
        var isAssignee = order.AssignedToUserId == employeeUserId;
        var isGroupMember = order.AssignedToGroupId.HasValue && await _groups.IsMemberAsync(order.AssignedToGroupId.Value, employeeUserId);
        if (!isAssignee && !isGroupMember) throw new InvalidOperationException("This maintenance order isn't assigned to you.");
        // No scope check here: the assignee/group-member check above already confirms this is the
        // order's own legitimate assignee — a scope narrowed/added after assignment must not lock
        // them out of finishing their own already-assigned work (scope restricts new discovery of
        // work, not access already legitimately granted; see EnsureOrderInScopeAsync's callers below,
        // which are all manager-only actions where the strict scope check is intentional).
        if (order.Status != "Open") throw new InvalidOperationException("This maintenance order isn't awaiting a fix.");

        var requiresApproval = order.OrderType?.RequiresApproval ?? false;
        var newStatus = requiresApproval ? "PendingApproval" : "Done";
        var closedDate = requiresApproval ? (DateTime?)null : DateTime.UtcNow;

        // Same pattern as WorkOrderService.ApplyPartsAsync: validate compatibility, decrement stock
        // atomically per part, snapshot Name/UnitCost from the catalog, all inside one transaction
        // so a mid-loop stock-insufficiency failure rolls back any parts already decremented. Cost
        // is entirely derived from the parts used — there's no manually-typed cost field anymore.
        var validParts = parts.Where(p => p.Quantity > 0).ToList();
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Claim the Open -> Done/PendingApproval transition atomically before touching parts/stock —
        // same race WorkOrderService's fix flows already guard against (EmployeeFixAsync/VendorFixAsync/
        // AdvanceWithoutVendorAsync all claim their stage transition before calling ApplyPartsAsync).
        // Without this, two concurrent Completes on the same order both pass the in-memory
        // Status=="Open" check above and each decrement stock for their own parts list — confirmed
        // live: two concurrent submissions both "succeeded", stock was decremented for both parts
        // lists (double the intended amount), and Cost ended up reflecting only whichever writer's
        // SaveChanges landed last, silently understating the true parts cost.
        var claimed = await _db.MaintenanceOrders.Where(m => m.Id == orderId && m.Status == "Open")
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, newStatus)
                .SetProperty(m => m.ClosedDate, closedDate)
                .SetProperty(m => m.CompletedDate, completedDate));
        if (claimed == 0) throw new InvalidOperationException("This maintenance order isn't awaiting a fix.");
        order.Status = newStatus; order.ClosedDate = closedDate; order.CompletedDate = completedDate;

        _db.MaintenanceOrderParts.RemoveRange(order.Parts);
        var totalCost = 0m;
        if (validParts.Count > 0)
        {
            var compatibleIds = await _db.SparePartAssets.Where(sa => sa.AssetId == order.AssetId)
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
                _db.MaintenanceOrderParts.Add(new MaintenanceOrderPart
                {
                    MaintenanceOrderId = order.Id, SparePartId = p.SparePartId,
                    Name = catalogPart.Name, Quantity = p.Quantity, UnitCostAtUsage = catalogPart.UnitCost,
                });
                totalCost += (catalogPart.UnitCost ?? 0m) * p.Quantity;
            }
        }
        order.Cost = totalCost;

        if (!await HasOtherOpenWorkAsync(order.AssetId, order.Id)) await SetAssetStatusAsync(order.AssetId, "Working");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("Complete", "MaintenanceOrder", order.Id.ToString(), employeeUserId, oldValue: "Open", newValue: order.Status);
        return order;
    }

    /// <summary>Manager sign-off for an order whose OrderType.RequiresApproval is true — the only
    /// way a PendingApproval order can actually finalize to Done.</summary>
    public async Task ApproveAsync(int orderId, string managerUserId)
    {
        var order = await _db.MaintenanceOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Maintenance order not found.");
        await EnsureOrderInScopeAsync(order.AssetId, managerUserId);
        if (order.Status != "PendingApproval") throw new InvalidOperationException("This order isn't awaiting approval.");
        order.Status = "Done";
        order.ClosedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Approve", "MaintenanceOrder", order.Id.ToString(), managerUserId, oldValue: "PendingApproval", newValue: "Done");
    }

    public async Task CancelAsync(int orderId, string? reason, string userId)
    {
        var order = await _db.MaintenanceOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Maintenance order not found.");
        await EnsureOrderInScopeAsync(order.AssetId, userId);
        if (order.Status != "Open") throw new InvalidOperationException("Only an open maintenance order can be cancelled.");

        // Claim the transition atomically — same race CompleteAsync already guards against: a
        // manager's Cancel and the assignee's Complete could otherwise both pass the in-memory
        // Status=="Open" check above, and whichever SaveChanges lands last silently overwrites the
        // other's result (confirmed live: a concurrent Complete+Cancel pair left the order
        // "Cancelled" in the DB while the audit log also showed a completed transition moments
        // earlier — contradictory state, and any parts/stock the Complete side decremented stayed
        // decremented even though the order ended up Cancelled instead of Done).
        var closedDate = DateTime.UtcNow;
        var claimed = await _db.MaintenanceOrders.Where(m => m.Id == orderId && m.Status == "Open")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "Cancelled").SetProperty(m => m.ClosedDate, closedDate));
        if (claimed == 0) throw new InvalidOperationException("Only an open maintenance order can be cancelled.");

        if (!await HasOtherOpenWorkAsync(order.AssetId, order.Id)) await SetAssetStatusAsync(order.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "MaintenanceOrder", order.Id.ToString(), userId, oldValue: "Open", newValue: "Cancelled", details: reason);
    }

    public async Task ReassignAsync(int orderId, string? assignedToUserId, int? assignedToGroupId, string managerUserId)
    {
        var order = await _db.MaintenanceOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Maintenance order not found.");
        await EnsureOrderInScopeAsync(order.AssetId, managerUserId);
        if (order.Status is "Done" or "Cancelled") throw new InvalidOperationException("A closed maintenance order can't be reassigned.");
        var hasUser = !string.IsNullOrWhiteSpace(assignedToUserId);
        var hasGroup = assignedToGroupId.HasValue;
        if (hasUser == hasGroup) throw new InvalidOperationException("Select exactly one assignee — a single employee or a Group.");
        await ValidateAssignmentModeAsync(order.OrderTypeId, hasUser, hasGroup);
        if (hasUser && !await _db.Users.AnyAsync(u => u.Id == assignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
        if (hasGroup && !await _db.Groups.AnyAsync(t => t.Id == assignedToGroupId))
            throw new InvalidOperationException("Selected group not found.");

        var oldLabel = order.AssignedToUserId ?? (order.AssignedToGroupId.HasValue ? $"Group #{order.AssignedToGroupId}" : "—");
        // Claim atomically against the DB's current status, not the copy loaded above — a concurrent
        // Complete/Cancel could otherwise close the order between that load and this write, and this
        // Reassign would still apply, permanently misattributing a completed order to someone who
        // never touched it (confirmed live: a concurrent Complete+Reassign pair left the order Done
        // but assigned to the Reassign's target, erasing who actually did the work).
        var newAssignedToUserId = hasUser ? assignedToUserId : null;
        var newAssignedToGroupId = hasGroup ? assignedToGroupId : null;
        var claimed = await _db.MaintenanceOrders.Where(m => m.Id == orderId && m.Status != "Done" && m.Status != "Cancelled")
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AssignedToUserId, newAssignedToUserId)
                .SetProperty(m => m.AssignedToGroupId, newAssignedToGroupId));
        if (claimed == 0) throw new InvalidOperationException("A closed maintenance order can't be reassigned.");
        await _audit.LogAsync("Reassign", "MaintenanceOrder", order.Id.ToString(), managerUserId,
            oldValue: oldLabel, newValue: hasUser ? assignedToUserId : $"Group #{assignedToGroupId}");
    }

    public async Task<MaintenanceOrder?> GetByIdAsync(int id) =>
        await _db.MaintenanceOrders
            .Include(m => m.Asset).ThenInclude(a => a!.Zone).ThenInclude(z => z!.LocationCategory)
            .Include(m => m.Asset).ThenInclude(a => a!.Category)
            .Include(m => m.AssignedToUser)
            .Include(m => m.AssignedToGroup)
            .Include(m => m.CreatedByUser)
            .Include(m => m.Parts)
            .FirstOrDefaultAsync(m => m.Id == id);

    public async Task<List<MaintenanceOrder>> GetAllAsync(string? status, string? search, string userId)
    {
        var query = _db.MaintenanceOrders
            .Include(m => m.Asset)
            .Include(m => m.AssignedToUser)
            .Include(m => m.AssignedToGroup)
            .Include(m => m.OrderType)
            .AsQueryable();

        // Details (round 12) 404s a scoped user out of an out-of-scope order — this list must hide
        // the same rows, or a scoped user sees every order exist in the list and only gets blocked
        // one click later on Details.
        if (await _scope.HasScopeAsync(userId))
        {
            var scopedAssetIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).Select(a => a.Id).ToListAsync();
            query = query.Where(m => scopedAssetIds.Contains(m.AssetId));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SearchQuery.Cap(search.Trim())!;
            query = query.Where(m => m.OrderNumber.Contains(term) || m.Asset!.AssetTag.Contains(term));
        }

        return await query.OrderByDescending(m => m.CreatedDate).Take(500).ToListAsync();
    }

    public async Task<byte[]> ExportToExcelAsync(string userId)
    {
        var query = _db.MaintenanceOrders
            .Include(m => m.Asset)
            .Include(m => m.AssignedToUser)
            .Include(m => m.AssignedToGroup)
            .AsQueryable();
        // Same scope enforcement as GetAllAsync — an export must not dump orders the exporting
        // user can't even see in the list, matching round 3's fix for Assets export.
        if (await _scope.HasScopeAsync(userId))
        {
            var scopedAssetIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId)).Select(a => a.Id).ToListAsync();
            query = query.Where(m => scopedAssetIds.Contains(m.AssetId));
        }
        var orders = await query.OrderByDescending(m => m.CreatedDate).ToListAsync();

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Maintenance Orders");
        string[] headers = ["Order #", "Asset Tag", "Assigned To", "Status", "Cost", "Completed Date", "Created"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var o in orders)
        {
            ws.Cells[row, 1].Value = o.OrderNumber;
            ws.Cells[row, 2].Value = o.Asset?.AssetTag;
            ws.Cells[row, 3].Value = o.AssignedToUser?.FullName ?? (o.AssignedToGroup != null ? $"Group: {o.AssignedToGroup.Name}" : null);
            ws.Cells[row, 4].Value = o.Status;
            ws.Cells[row, 5].Value = o.Cost;
            ws.Cells[row, 6].Value = o.CompletedDate?.ToString("yyyy-MM-dd");
            ws.Cells[row, 7].Value = o.CreatedDate.ToString("yyyy-MM-dd");
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }
}
