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
    public MaintenanceOrderService(ApplicationDbContext db, IAuditService audit, ISparePartService spareParts) { _db = db; _audit = audit; _spareParts = spareParts; }

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
        if (await _db.Assets.AnyAsync(a => a.Id == assetId && a.Status == "Retired"))
            throw new InvalidOperationException("This asset is retired and can't have new orders opened against it.");

        var order = new MaintenanceOrder
        {
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
        await SaveWithUniqueNumberRetryAsync(order);
        await _audit.LogAsync("Create", "MaintenanceOrder", order.Id.ToString(), createdByUserId, newValue: order.OrderNumber);
        return order;
    }

    /// <summary>Same fix as WorkOrderService/InspectionOrderService's identically-named helper —
    /// OrderNumber "MO-{year}-{seq:D4}" was a plain COUNT-then-use query with no atomic guard,
    /// which can collide with a still-existing row's number and raise a raw unhandled
    /// DbUpdateException. Retry with a freshly recomputed number on that specific failure.</summary>
    private async Task SaveWithUniqueNumberRetryAsync(MaintenanceOrder order)
    {
        for (var attempt = 0; ; attempt++)
        {
            var year = order.CreatedDate.Year;
            var seq = await _db.MaintenanceOrders.CountAsync(m => m.CreatedDate.Year == year) + 1;
            order.OrderNumber = $"MO-{year}-{seq:D4}";
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

    public async Task<MaintenanceOrder> CompleteAsync(int orderId, string fixDescription, decimal? cost, DateTime? completedDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId)
    {
        var order = await _db.MaintenanceOrders.Include(m => m.Parts).FirstOrDefaultAsync(m => m.Id == orderId)
            ?? throw new InvalidOperationException("Maintenance order not found.");
        if (order.AssignedToUserId != employeeUserId) throw new InvalidOperationException("This maintenance order isn't assigned to you.");
        if (order.Status != "Open") throw new InvalidOperationException("This maintenance order isn't awaiting a fix.");

        order.FixDescription = fixDescription;
        order.Cost = cost;
        order.CompletedDate = completedDate;

        // Same pattern as WorkOrderService.ApplyPartsAsync: validate compatibility, decrement stock
        // atomically per part, snapshot Name/UnitCost from the catalog, all inside one transaction
        // so a mid-loop stock-insufficiency failure rolls back any parts already decremented.
        var validParts = parts.Where(p => p.Quantity > 0).ToList();
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.MaintenanceOrderParts.RemoveRange(order.Parts);
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
            }
        }

        order.Status = "Done";
        order.ClosedDate = DateTime.UtcNow;
        if (!await HasOtherOpenWorkAsync(order.AssetId, order.Id)) await SetAssetStatusAsync(order.AssetId, "Working");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
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
            var term = SearchQuery.Cap(search.Trim())!;
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
