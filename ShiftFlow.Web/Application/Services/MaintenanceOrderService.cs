using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class MaintenanceOrderService : IMaintenanceOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    public MaintenanceOrderService(ApplicationDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // Stages/statuses (across both entities) that mean "this asset still has open work" —
    // checked before restoring Asset.Status to "Working" so a second, unrelated issue on the
    // same asset doesn't get silently cleared out from under it.
    private static readonly string[] OpenWorkOrderStages = ["Draft", "New", "Sent to Vendor", "Blocked", "Fixed - Pending Confirmation"];

    private async Task SetAssetStatusAsync(int assetId, string status)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset != null && asset.Status != "Retired") asset.Status = status;
    }

    private async Task<bool> HasOtherOpenWorkAsync(int assetId, int excludeMaintenanceOrderId) =>
        await _db.MaintenanceOrders.AnyAsync(m => m.AssetId == assetId && m.Id != excludeMaintenanceOrderId && m.Status == "Open")
        || await _db.WorkOrders.AnyAsync(w => w.AssetId == assetId && OpenWorkOrderStages.Contains(w.Stage));

    public async Task<MaintenanceOrder> CreateAsync(int assetId, string assignedToUserId, string? description, DateTime? dueDate, string createdByUserId, int? orderTypeId = null)
    {
        if (string.IsNullOrWhiteSpace(assignedToUserId))
            throw new InvalidOperationException("Select an employee to assign this maintenance order to.");

        var year = DateTime.UtcNow.Year;
        var seq = await _db.MaintenanceOrders.CountAsync(m => m.CreatedDate.Year == year) + 1;
        var order = new MaintenanceOrder
        {
            OrderNumber = $"MO-{year}-{seq:D4}",
            AssetId = assetId,
            AssignedToUserId = assignedToUserId,
            Description = description,
            DueDate = dueDate,
            CreatedByUserId = createdByUserId,
            CreatedDate = DateTime.UtcNow,
            Status = "Open",
            OrderTypeId = orderTypeId,
        };
        _db.MaintenanceOrders.Add(order);
        await SetAssetStatusAsync(assetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "MaintenanceOrder", order.Id.ToString(), createdByUserId, newValue: order.OrderNumber);
        return order;
    }

    public async Task<MaintenanceOrder> CompleteAsync(int orderId, string fixDescription, decimal? cost, DateTime? completedDate, List<(string Name, int Quantity)> parts, string employeeUserId)
    {
        var order = await _db.MaintenanceOrders.Include(m => m.Parts).FirstOrDefaultAsync(m => m.Id == orderId)
            ?? throw new InvalidOperationException("Maintenance order not found.");
        if (order.AssignedToUserId != employeeUserId) throw new InvalidOperationException("This maintenance order isn't assigned to you.");
        if (order.Status != "Open") throw new InvalidOperationException("This maintenance order isn't awaiting a fix.");

        order.FixDescription = fixDescription;
        order.Cost = cost;
        order.CompletedDate = completedDate;
        _db.MaintenanceOrderParts.RemoveRange(order.Parts);
        foreach (var p in parts.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            _db.MaintenanceOrderParts.Add(new MaintenanceOrderPart { MaintenanceOrderId = order.Id, Name = p.Name, Quantity = p.Quantity });

        order.Status = "Done";
        order.ClosedDate = DateTime.UtcNow;
        if (!await HasOtherOpenWorkAsync(order.AssetId, order.Id)) await SetAssetStatusAsync(order.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Complete", "MaintenanceOrder", order.Id.ToString(), employeeUserId, oldValue: "Open", newValue: "Done");
        return order;
    }

    public async Task CancelAsync(int orderId, string? reason, string userId)
    {
        var order = await _db.MaintenanceOrders.FindAsync(orderId) ?? throw new InvalidOperationException("Maintenance order not found.");
        if (order.Status != "Open") throw new InvalidOperationException("Only an open maintenance order can be cancelled.");

        order.Status = "Cancelled";
        order.ClosedDate = DateTime.UtcNow;
        if (!await HasOtherOpenWorkAsync(order.AssetId, order.Id)) await SetAssetStatusAsync(order.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "MaintenanceOrder", order.Id.ToString(), userId, oldValue: "Open", newValue: "Cancelled", details: reason);
    }

    public async Task<MaintenanceOrder?> GetByIdAsync(int id) =>
        await _db.MaintenanceOrders
            .Include(m => m.Asset).ThenInclude(a => a!.Zone).ThenInclude(z => z!.LocationCategory)
            .Include(m => m.AssignedToUser)
            .Include(m => m.CreatedByUser)
            .Include(m => m.Parts)
            .FirstOrDefaultAsync(m => m.Id == id);

    public async Task<List<MaintenanceOrder>> GetAllAsync(string? status, string? search)
    {
        var query = _db.MaintenanceOrders
            .Include(m => m.Asset)
            .Include(m => m.AssignedToUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.OrderNumber.Contains(term) || m.Asset!.AssetTag.Contains(term));
        }

        return await query.OrderByDescending(m => m.CreatedDate).Take(500).ToListAsync();
    }

    public async Task<byte[]> ExportToExcelAsync()
    {
        var orders = await _db.MaintenanceOrders
            .Include(m => m.Asset)
            .Include(m => m.AssignedToUser)
            .OrderByDescending(m => m.CreatedDate)
            .ToListAsync();

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
            ws.Cells[row, 3].Value = o.AssignedToUser?.FullName;
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
