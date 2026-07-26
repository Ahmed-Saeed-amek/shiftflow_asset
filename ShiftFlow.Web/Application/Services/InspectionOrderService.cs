using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

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

    public async Task<InspectionOrder> CreateAsync(string title, string? description, string? assignedToUserId, int? assignedToTeamId,
        int? zoneId, List<int>? assetIds, DateTime? dueDate, string createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Title is required.");

        var hasUser = !string.IsNullOrEmpty(assignedToUserId);
        var hasTeam = assignedToTeamId.HasValue;
        if (hasUser == hasTeam)
            throw new InvalidOperationException("Select exactly one assignee — a single employee or a Team.");

        var resolvedAssetIds = zoneId.HasValue
            ? await _db.Assets.Where(a => a.ZoneId == zoneId.Value).Select(a => a.Id).ToListAsync()
            : assetIds ?? [];

        if (resolvedAssetIds.Count == 0)
            throw new InvalidOperationException("Select a zone or at least one asset to inspect.");

        var year = DateTime.UtcNow.Year;
        var seq = await _db.InspectionOrders.CountAsync(o => o.CreatedAt.Year == year) + 1;

        var order = new InspectionOrder
        {
            OrderNumber = $"INS-{year}-{seq:D4}",
            Title = title,
            Description = description,
            AssignedToUserId = hasUser ? assignedToUserId : null,
            AssignedToTeamId = hasTeam ? assignedToTeamId : null,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            DueDate = dueDate,
            Status = "Open",
            InspectionRun = new InspectionRun
            {
                ZoneId = zoneId,
                Items = resolvedAssetIds.Select(id => new InspectionRunAsset { AssetId = id }).ToList(),
            },
        };
        _db.InspectionOrders.Add(order);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "InspectionOrder", order.Id.ToString(), createdByUserId, newValue: order.OrderNumber);
        return order;
    }

    public async Task<InspectionOrder?> GetByIdAsync(int id) =>
        await _db.InspectionOrders
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam).ThenInclude(t => t!.Members).ThenInclude(m => m.User)
            .Include(o => o.CreatedByUser)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Zone)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items).ThenInclude(i => i.Asset).ThenInclude(a => a.Zone)
                .ThenInclude(z => z!.Block!).ThenInclude(bl => bl.Area!).ThenInclude(a => a.Governorate)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items).ThenInclude(i => i.WorkOrder)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<List<InspectionOrder>> GetMyOrdersAsync(string userId, bool includeDone = false)
    {
        var myTeamIds = await _db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync();

        var query = _db.InspectionOrders
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .Where(o => o.AssignedToUserId == userId || (o.AssignedToTeamId != null && myTeamIds.Contains(o.AssignedToTeamId.Value)));

        if (!includeDone)
            query = query.Where(o => o.Status != "Done");

        return await query.OrderByDescending(o => o.CreatedAt).Take(300).ToListAsync();
    }

    public async Task<List<InspectionOrder>> GetAllAsync(string? status, string? search)
    {
        var query = _db.InspectionOrders
            .Include(o => o.AssignedToUser)
            .Include(o => o.AssignedToTeam)
            .Include(o => o.InspectionRun!).ThenInclude(r => r.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(term) || o.Title.Contains(term));
        }

        return await query.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
    }

    public async Task UpdateInspectionItemAsync(int itemId, string outcome, string? notes, int? workOrderId, string updatedByUserId)
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
        item.Notes = notes;
        item.InspectedByUserId = updatedByUserId;
        item.InspectedAt = DateTime.UtcNow;
        item.WorkOrderId = workOrderId;

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

    public async Task CancelAsync(int orderId, string userId)
    {
        var order = await _db.InspectionOrders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Inspection order not found.");
        if (order.Status == "Done")
            throw new InvalidOperationException("A completed inspection order cannot be cancelled.");

        _db.InspectionOrders.Remove(order);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "InspectionOrder", orderId.ToString(), userId, oldValue: order.OrderNumber);
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
        string[] headers = ["Order #", "Title", "Status", "Assigned To", "Assets", "Checked", "Due Date", "Created"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var o in orders)
        {
            var items = o.InspectionRun?.Items ?? [];
            ws.Cells[row, 1].Value = o.OrderNumber;
            ws.Cells[row, 2].Value = o.Title;
            ws.Cells[row, 3].Value = o.Status;
            ws.Cells[row, 4].Value = o.AssignedToUser?.FullName ?? (o.AssignedToTeam != null ? $"Team: {o.AssignedToTeam.Name}" : "");
            ws.Cells[row, 5].Value = items.Count;
            ws.Cells[row, 6].Value = items.Count(i => i.Outcome != "Pending");
            ws.Cells[row, 7].Value = o.DueDate?.ToString("yyyy-MM-dd");
            ws.Cells[row, 8].Value = o.CreatedAt.ToString("yyyy-MM-dd");
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }
}
