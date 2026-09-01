using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.SparePartView)]
public class SparePartsAnalyticsController : Controller
{
    private readonly ApplicationDbContext _db;
    public SparePartsAnalyticsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? assetId, int? categoryId)
    {
        // EF can't UNION two different entity types in one LINQ query, so both sides are projected
        // to one common shape first (same approach the Dashboard already uses to combine
        // Inspection/Maintenance/Work orders into one recent-orders list), then concatenated
        // in-memory. Only rows with a SparePartId are usable here - pre-catalog free-text history
        // (SparePartId == null) has no stock/cost data to attribute and is excluded.
        var woUsageQuery = _db.WorkOrderParts.AsNoTracking()
            .Where(p => p.SparePartId != null);
        var moUsageQuery = _db.MaintenanceOrderParts.AsNoTracking()
            .Where(p => p.SparePartId != null);
        if (from.HasValue)
        {
            woUsageQuery = woUsageQuery.Where(p => (p.WorkOrder!.ClosedDate ?? p.WorkOrder.CreatedDate) >= from);
            moUsageQuery = moUsageQuery.Where(p => (p.MaintenanceOrder!.ClosedDate ?? p.MaintenanceOrder.CreatedDate) >= from);
        }
        if (to.HasValue)
        {
            woUsageQuery = woUsageQuery.Where(p => (p.WorkOrder!.ClosedDate ?? p.WorkOrder.CreatedDate) <= to);
            moUsageQuery = moUsageQuery.Where(p => (p.MaintenanceOrder!.ClosedDate ?? p.MaintenanceOrder.CreatedDate) <= to);
        }

        var woUsage = await woUsageQuery.Select(p => new PartUsageRow
        {
            SparePartId = p.SparePartId!.Value, AssetId = p.WorkOrder!.AssetId, Quantity = p.Quantity,
            UnitCostAtUsage = p.UnitCostAtUsage, UsedDate = p.WorkOrder.ClosedDate ?? p.WorkOrder.CreatedDate,
        }).ToListAsync();
        var moUsage = await moUsageQuery.Select(p => new PartUsageRow
        {
            SparePartId = p.SparePartId!.Value, AssetId = p.MaintenanceOrder!.AssetId, Quantity = p.Quantity,
            UnitCostAtUsage = p.UnitCostAtUsage, UsedDate = p.MaintenanceOrder.ClosedDate ?? p.MaintenanceOrder.CreatedDate,
        }).ToListAsync();

        var allUsage = woUsage.Concat(moUsage).ToList();
        if (assetId.HasValue) allUsage = allUsage.Where(u => u.AssetId == assetId).ToList();

        var partsById = await _db.SpareParts.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var assetsById = await _db.Assets.AsNoTracking().Include(a => a.Category)
            .ToDictionaryAsync(a => a.Id);

        if (categoryId.HasValue)
            allUsage = allUsage.Where(u => assetsById.TryGetValue(u.AssetId, out var a) && a.CategoryId == categoryId).ToList();

        var mostUsed = allUsage.GroupBy(u => u.SparePartId)
            .Select(g => new SparePartUsageSummary
            {
                SparePartId = g.Key,
                Name = partsById.TryGetValue(g.Key, out var p) ? p.Name : "—",
                TotalQuantity = g.Sum(u => u.Quantity),
                UsageCount = g.Count(),
                TotalCost = g.Sum(u => (u.UnitCostAtUsage ?? 0) * u.Quantity),
            })
            .OrderByDescending(x => x.TotalQuantity).Take(20).ToList();

        var byAsset = allUsage.GroupBy(u => u.AssetId)
            .Select(g => new AssetUsageSummary
            {
                AssetId = g.Key,
                AssetLabel = assetsById.TryGetValue(g.Key, out var a) ? $"{a.AssetTag} — {a.Name}" : "—",
                TotalQuantity = g.Sum(u => u.Quantity),
                TotalCost = g.Sum(u => (u.UnitCostAtUsage ?? 0) * u.Quantity),
                DistinctParts = g.Select(u => u.SparePartId).Distinct().Count(),
            })
            .OrderByDescending(x => x.TotalCost).ToList();

        var byCategory = allUsage
            .Where(u => assetsById.ContainsKey(u.AssetId))
            .GroupBy(u => assetsById[u.AssetId].CategoryId)
            .Select(g => new CategoryUsageSummary
            {
                CategoryId = g.Key,
                CategoryLabel = assetsById.Values.FirstOrDefault(a => a.CategoryId == g.Key)?.Category?.Name ?? "—",
                TotalQuantity = g.Sum(u => u.Quantity),
                TotalCost = g.Sum(u => (u.UnitCostAtUsage ?? 0) * u.Quantity),
            })
            .OrderByDescending(x => x.TotalCost).ToList();

        var totalCost = allUsage.Sum(u => (u.UnitCostAtUsage ?? 0) * u.Quantity);
        var costByMonth = allUsage.GroupBy(u => new { u.UsedDate.Year, u.UsedDate.Month })
            .Select(g => new MonthlyCostRow { Year = g.Key.Year, Month = g.Key.Month, Cost = g.Sum(u => (u.UnitCostAtUsage ?? 0) * u.Quantity) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();

        ViewBag.MostUsed = mostUsed;
        ViewBag.ByAsset = byAsset;
        ViewBag.ByCategory = byCategory;
        ViewBag.TotalCost = totalCost;
        ViewBag.CostByMonth = costByMonth;
        ViewBag.HasUncostedRows = allUsage.Any(u => u.UnitCostAtUsage == null);
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        ViewBag.From = from; ViewBag.To = to; ViewBag.AssetId = assetId; ViewBag.CategoryId = categoryId;
        return View();
    }
}
