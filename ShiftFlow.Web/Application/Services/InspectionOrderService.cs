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

    public async Task<InspectionOrder> CreateAsync(int orderTypeId, string? description, string? assignedToUserId, int? assignedToTeamId,
        List<int>? assetIds, DateTime? dueDate, string createdByUserId)
    {
        var orderType = await _db.OrderTypes.FirstOrDefaultAsync(t => t.Id == orderTypeId && t.IsActive)
            ?? throw new InvalidOperationException("Invalid order type.");

        var hasUser = !string.IsNullOrEmpty(assignedToUserId);
        var hasTeam = assignedToTeamId.HasValue;
        if (hasUser == hasTeam)
            throw new InvalidOperationException("Select exactly one assignee — a single employee or a Team.");

        var resolvedAssetIds = assetIds ?? [];
        if (resolvedAssetIds.Count == 0)
            throw new InvalidOperationException("Select at least one asset to inspect.");

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
    /// DbUpdateException all the way to the client (confirmed live). Same fix as
    /// WorkOrderService.SaveWithUniqueNumberRetryAsync: retry with a freshly recomputed number on
    /// that specific failure instead of making the count itself atomic.</summary>
    private async Task SaveWithUniqueNumberRetryAsync(InspectionOrder order, string prefix)
    {
        for (var attempt = 0; ; attempt++)
        {
            var year = order.CreatedAt.Year;
            var seq = await _db.InspectionOrders.CountAsync(o => o.CreatedAt.Year == year) + 1;
            order.OrderNumber = $"{prefix}-{year}-{seq:D4}";
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                // Duplicate OrderNumber from a stale count or a concurrent insert — recompute and retry.
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
        var order = await _db.InspectionOrders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Inspection order not found.");
        if (order.Status == "Done")
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
            order.Status = "Done";
            order.ClosedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync("UpdateInspectionItem", "InspectionRunAsset", itemId.ToString(), updatedByUserId, newValue: outcome);
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
        order.Status = "Cancelled";
        order.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "InspectionOrder", orderId.ToString(), userId, oldValue: oldStatus, newValue: "Cancelled", details: reason);
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
