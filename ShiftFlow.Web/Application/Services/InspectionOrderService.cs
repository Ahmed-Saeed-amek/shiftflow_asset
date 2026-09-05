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
    private readonly ITeamService _teams;

    public InspectionOrderService(ApplicationDbContext db, IAuditService audit, ITeamService teams)
    {
        _db = db;
        _audit = audit;
        _teams = teams;
    }

    /// <summary>Re-derives the same AssignmentMode rule OrdersController.Create already enforces for
    /// the manual-create path — every other caller (RecurringOrderSchedulerService, and now
    /// ReassignAsync) reaches this service directly, so the check belongs here too or it's silently
    /// bypassable (confirmed live for Reassign: an EmployeeOnly-typed order could be reassigned to a
    /// Team with no error).</summary>
    private static void ValidateAssignmentMode(string? mode, bool hasUser, bool hasTeam)
    {
        if (mode == "EmployeeOnly" && hasTeam)
            throw new InvalidOperationException("This order type can only be assigned to an employee, not a team.");
        if (mode == "TeamOnly" && hasUser)
            throw new InvalidOperationException("This order type can only be assigned to a team, not an employee.");
    }

    public async Task<InspectionOrder> CreateAsync(int orderTypeId, string? description, string? assignedToUserId, int? assignedToTeamId,
        List<int>? assetIds, DateTime? dueDate, string createdByUserId, int? sourceRecurringOrderId = null, DateTime? scheduledDate = null)
    {
        var orderType = await _db.OrderTypes.FirstOrDefaultAsync(t => t.Id == orderTypeId && t.IsActive)
            ?? throw new InvalidOperationException("Invalid order type.");

        var hasUser = !string.IsNullOrEmpty(assignedToUserId);
        var hasTeam = assignedToTeamId.HasValue;
        if (hasUser == hasTeam)
            throw new InvalidOperationException("Select exactly one assignee — a single employee or a Team.");
        ValidateAssignmentMode(orderType.AssignmentMode, hasUser, hasTeam);

        var resolvedAssetIds = assetIds ?? [];
        if (resolvedAssetIds.Count == 0)
            throw new InvalidOperationException("Select at least one asset to inspect.");
        // Same guard as MaintenanceOrderService.CreateAsync — without it, a recurring schedule (or
        // the AI assistant tool) keeps generating new orders against an asset retired after the
        // schedule was created, since OrdersController's own retired-asset check only covers its
        // own manual-create path, not every caller of this method.
        if (await _db.Assets.AnyAsync(a => resolvedAssetIds.Contains(a.Id) && a.Status == "Retired"))
            throw new InvalidOperationException("One or more selected assets are retired and can't have new orders opened against them.");

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
            AssignedToTeamId = hasTeam ? assignedToTeamId : null,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            DueDate = dueDate,
            Status = "Open",
            SourceRecurringOrderId = sourceRecurringOrderId,
            ScheduledDate = scheduledDate,
            InspectionRun = new InspectionRun
            {
                ZoneId = singleZoneId,
                Items = resolvedAssetIds.Select(id => new InspectionRunAsset { AssetId = id }).ToList(),
            },
        };
        _db.InspectionOrders.Add(order);
        await SaveWithUniqueNumberRetryAsync(order, orderType.Prefix);
        await _audit.LogAsync("Create", "InspectionOrder", order.Id.ToString(), createdByUserId, newValue: order.OrderNumber);
        return order;
    }

    /// <summary>OrderNumber "{Prefix}-{year}-{seq:D4}" was assigned from a plain COUNT-then-use
    /// query with no atomic guard — a stale/leftover count (e.g. after older rows were hard-deleted
    /// by the pre-fix Cancel behavior, or two concurrent creates) can compute a seq that collides
    /// with a still-existing row's number, hitting the unique index and raising a raw, unhandled
    /// DbUpdateException all the way to the client (confirmed live). Worse, COUNT(*) itself goes
    /// permanently stale the moment any row for the year is ever hard-deleted — it then recomputes
    /// the exact same already-used number on every retry attempt and can never get past the gap.
    /// The seq counter is shared across every prefix for the year (matching the existing numbering
    /// scheme, not per-prefix), so the "highest existing seq" must be read across all of them —
    /// find it from the numeric suffix of every InspectionOrder's OrderNumber that year rather than
    /// a plain count, and advance it by the attempt number on retry so a genuine concurrent-insert
    /// race still makes progress.</summary>
    private async Task SaveWithUniqueNumberRetryAsync(InspectionOrder order, string prefix)
    {
        var year = order.CreatedAt.Year;
        var suffix = $"-{year}-";
        var existingNumbers = await _db.InspectionOrders
            .Where(o => o.CreatedAt.Year == year)
            .Select(o => o.OrderNumber)
            .ToListAsync();
        var nextSeq = existingNumbers.Count == 0 ? 1
            : existingNumbers.Select(n =>
            {
                var idx = n.IndexOf(suffix, StringComparison.Ordinal);
                return idx >= 0 && int.TryParse(n.AsSpan(idx + suffix.Length), out var s) ? s : 0;
            }).Max() + 1;

        for (var attempt = 0; ; attempt++)
        {
            order.OrderNumber = $"{prefix}-{year}-{nextSeq + attempt:D4}";
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

    public async Task<InspectionOrder?> GetByIdAsync(int id) =>
        await _db.InspectionOrders
            .Include(o => o.OrderType)
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam).ThenInclude(t => t!.Members).ThenInclude(m => m.User)
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
        var myTeamIds = await _db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync();

        var query = _db.InspectionOrders
            .Include(o => o.OrderType)
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .Where(o => o.AssignedToUserId == userId || (o.AssignedToTeamId != null && myTeamIds.Contains(o.AssignedToTeamId.Value)));

        if (!includeDone)
            query = query.Where(o => o.Status != "Done" && o.Status != "Cancelled");
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt <= to.Value);

        return await query.OrderByDescending(o => o.CreatedAt).Take(300).ToListAsync();
    }

    public async Task<List<InspectionOrder>> GetAllAsync(string? status, string? search, bool overdue = false)
    {
        var query = _db.InspectionOrders
            .Include(o => o.OrderType)
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .AsQueryable();

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
            query = query.Where(o => o.Status != "Done" && o.Status != "Cancelled" && o.DueDate != null && o.DueDate < today);
        }

        return await query.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
    }

    public async Task UpdateInspectionItemAsync(int itemId, string outcome, int? workOrderId, List<int>? maintenanceActionTypeIds, string updatedByUserId)
    {
        var item = await _db.InspectionRunAssets.FindAsync(itemId)
            ?? throw new InvalidOperationException("Inspection item not found.");
        if (outcome == "Pending" || !InspectionRunAsset.Outcomes.Contains(outcome))
            throw new InvalidOperationException("Invalid outcome.");

        var orderId = await _db.InspectionRuns.Where(r => r.Id == item.InspectionRunId)
            .Select(r => r.InspectionOrderId).FirstAsync();
        var order = await _db.InspectionOrders.Include(o => o.OrderType).FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Inspection order not found.");
        if (order.Status is "Done" or "PendingApproval")
            throw new InvalidOperationException("This inspection order is already closed.");

        item.Outcome = outcome;
        item.InspectedByUserId = updatedByUserId;
        item.InspectedAt = DateTime.UtcNow;
        item.WorkOrderId = workOrderId;

        _db.InspectionItemMaintenanceActions.RemoveRange(
            await _db.InspectionItemMaintenanceActions.Where(m => m.InspectionRunAssetId == itemId).ToListAsync());
        foreach (var maintenanceActionTypeId in maintenanceActionTypeIds ?? [])
            _db.InspectionItemMaintenanceActions.Add(new InspectionItemMaintenanceAction { InspectionRunAssetId = itemId, MaintenanceActionTypeId = maintenanceActionTypeId });

        if (order.Status == "Open")
            order.Status = "InProgress";

        await _db.SaveChangesAsync();

        var runId = item.InspectionRunId;
        var stillPending = await _db.InspectionRunAssets.AnyAsync(i => i.InspectionRunId == runId && i.Outcome == "Pending");
        if (!stillPending)
        {
            var requiresApproval = order.OrderType?.RequiresApproval ?? false;
            order.Status = requiresApproval ? "PendingApproval" : "Done";
            if (!requiresApproval) order.ClosedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync("UpdateInspectionItem", "InspectionRunAsset", itemId.ToString(), updatedByUserId, newValue: outcome);
    }

    /// <summary>Manager sign-off for an order whose OrderType.RequiresApproval is true — the only
    /// way a PendingApproval order can actually finalize to Done.</summary>
    public async Task ApproveAsync(int orderId, string managerUserId)
    {
        var order = await _db.InspectionOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Inspection order not found.");
        if (order.Status != "PendingApproval") throw new InvalidOperationException("This order isn't awaiting approval.");
        order.Status = "Done";
        order.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Approve", "InspectionOrder", order.Id.ToString(), managerUserId, oldValue: "PendingApproval", newValue: "Done");
    }

    public async Task UpdateMaintenanceActionsAsync(int itemId, List<int>? maintenanceActionTypeIds, string updatedByUserId)
    {
        var item = await _db.InspectionRunAssets.FindAsync(itemId)
            ?? throw new InvalidOperationException("Inspection item not found.");

        var orderId = await _db.InspectionRuns.Where(r => r.Id == item.InspectionRunId)
            .Select(r => r.InspectionOrderId).FirstAsync();
        var order = await _db.InspectionOrders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Inspection order not found.");
        if (order.Status == "Done")
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
        if (order.Status is "Done" or "Cancelled")
            throw new InvalidOperationException("A completed or already-cancelled inspection order cannot be cancelled.");
        var oldStatus = order.Status;

        // Claim the transition atomically — a manager's Cancel and an assignee's item-outcome update
        // (which can independently drive the order to Done/PendingApproval via
        // UpdateInspectionItemAsync) could otherwise both pass the in-memory status check above,
        // and whichever SaveChanges lands last silently overwrites the other's result. Same race
        // class MaintenanceOrderService.CancelAsync/CompleteAsync already guard against.
        var closedAt = DateTime.UtcNow;
        var claimed = await _db.InspectionOrders.Where(o => o.Id == orderId && o.Status != "Done" && o.Status != "Cancelled")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "Cancelled").SetProperty(o => o.ClosedAt, closedAt));
        if (claimed == 0) throw new InvalidOperationException("A completed or already-cancelled inspection order cannot be cancelled.");

        await _audit.LogAsync("Cancel", "InspectionOrder", orderId.ToString(), userId, oldValue: oldStatus, newValue: "Cancelled", details: reason);
    }

    public async Task ReassignAsync(int orderId, string? assignedToUserId, int? assignedToTeamId, string managerUserId)
    {
        var order = await _db.InspectionOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Inspection order not found.");
        if (order.Status is "Done" or "Cancelled") throw new InvalidOperationException("A closed inspection order can't be reassigned.");
        var hasUser = !string.IsNullOrWhiteSpace(assignedToUserId);
        var hasTeam = assignedToTeamId.HasValue;
        if (hasUser == hasTeam) throw new InvalidOperationException("Select exactly one assignee — a single employee or a Team.");
        var assignmentMode = await _db.OrderTypes.Where(t => t.Id == order.OrderTypeId).Select(t => t.AssignmentMode).FirstOrDefaultAsync();
        ValidateAssignmentMode(assignmentMode, hasUser, hasTeam);
        if (hasUser && !await _db.Users.AnyAsync(u => u.Id == assignedToUserId))
            throw new InvalidOperationException("Selected employee not found.");

        var oldLabel = order.AssignedToUserId ?? (order.AssignedToTeamId.HasValue ? $"Team #{order.AssignedToTeamId}" : "—");
        order.AssignedToUserId = hasUser ? assignedToUserId : null;
        order.AssignedToTeamId = hasTeam ? assignedToTeamId : null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reassign", "InspectionOrder", order.Id.ToString(), managerUserId,
            oldValue: oldLabel, newValue: hasUser ? assignedToUserId : $"Team #{assignedToTeamId}");
    }

    public async Task<byte[]> ExportToExcelAsync()
    {
        var orders = await _db.InspectionOrders
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

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
            ws.Cells[row, 3].Value = o.AssignedToUser?.FullName ?? (o.AssignedToTeam != null ? $"Team: {o.AssignedToTeam.Name}" : "");
            ws.Cells[row, 4].Value = items.Count;
            ws.Cells[row, 5].Value = items.Count(i => i.Outcome != "Pending");
            ws.Cells[row, 6].Value = o.DueDate?.ToString("yyyy-MM-dd");
            ws.Cells[row, 7].Value = o.CreatedAt.ToString("yyyy-MM-dd");
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }
}
