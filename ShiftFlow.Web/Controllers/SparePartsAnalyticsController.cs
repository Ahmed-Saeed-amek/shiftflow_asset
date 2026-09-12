using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.SparePartView)]
public class SparePartsAnalyticsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILanguageService _loc;
    private readonly UserManager<ApplicationUser> _um;
    private readonly IAssetScopeService _scope;
    private readonly ILookupCache _lookups;
    public SparePartsAnalyticsController(ApplicationDbContext db, ILanguageService loc, UserManager<ApplicationUser> um, IAssetScopeService scope, ILookupCache lookups)
    {
        _db = db; _loc = loc; _um = um; _scope = scope; _lookups = lookups;
    }

    /// <summary>Every aggregate on this page is computed by the database. The previous version
    /// loaded every WorkOrderPart, MaintenanceOrderPart, SparePart and Asset row into memory and
    /// grouped there. EF can't UNION two different entity types in one LINQ query, so each metric
    /// is grouped separately per source table and the two (already tiny) result sets merged here.</summary>
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? assetId, int? categoryId)
    {
        // Only rows with a SparePartId are usable — pre-catalog free-text history has no
        // stock/cost data to attribute.
        var woQuery = _db.WorkOrderParts.AsNoTracking().Where(p => p.SparePartId != null);
        var moQuery = _db.MaintenanceOrderParts.AsNoTracking().Where(p => p.SparePartId != null);

        if (from.HasValue)
        {
            woQuery = woQuery.Where(p => (p.WorkOrder!.ClosedDate ?? p.WorkOrder.CreatedDate) >= from);
            moQuery = moQuery.Where(p => (p.MaintenanceOrder!.ClosedDate ?? p.MaintenanceOrder.CreatedDate) >= from);
        }
        if (to.HasValue)
        {
            woQuery = woQuery.Where(p => (p.WorkOrder!.ClosedDate ?? p.WorkOrder.CreatedDate) <= to);
            moQuery = moQuery.Where(p => (p.MaintenanceOrder!.ClosedDate ?? p.MaintenanceOrder.CreatedDate) <= to);
        }

        // Same UserAssetScope enforced on the Assets list — without it a scoped user reads cost and
        // usage figures (and asset tags/names) for assets outside their zone or category.
        var userId = _um.GetUserId(User);
        if (userId != null && await _scope.HasScopeAsync(userId))
        {
            var scopedAssets = await _scope.GetScopedAssetsAsync(userId);
            woQuery = woQuery.Where(p => scopedAssets.Any(a => a.Id == p.WorkOrder!.AssetId));
            moQuery = moQuery.Where(p => scopedAssets.Any(a => a.Id == p.MaintenanceOrder!.AssetId));
        }
        if (assetId.HasValue)
        {
            woQuery = woQuery.Where(p => p.WorkOrder!.AssetId == assetId);
            moQuery = moQuery.Where(p => p.MaintenanceOrder!.AssetId == assetId);
        }
        if (categoryId.HasValue)
        {
            woQuery = woQuery.Where(p => p.WorkOrder!.Asset!.CategoryId == categoryId);
            moQuery = moQuery.Where(p => p.MaintenanceOrder!.Asset!.CategoryId == categoryId);
        }

        // Both sides flattened to one common shape, so every grouping below reads the same projection.
        var woRows = woQuery.Select(p => new UsageProjection
        {
            SparePartId = p.SparePartId!.Value,
            AssetId = p.WorkOrder!.AssetId,
            CategoryId = p.WorkOrder.Asset!.CategoryId,
            Quantity = p.Quantity,
            Cost = (p.UnitCostAtUsage ?? 0) * p.Quantity,
            Uncosted = p.UnitCostAtUsage == null,
            UsedDate = p.WorkOrder.ClosedDate ?? p.WorkOrder.CreatedDate,
        });
        var moRows = moQuery.Select(p => new UsageProjection
        {
            SparePartId = p.SparePartId!.Value,
            AssetId = p.MaintenanceOrder!.AssetId,
            CategoryId = p.MaintenanceOrder.Asset!.CategoryId,
            Quantity = p.Quantity,
            Cost = (p.UnitCostAtUsage ?? 0) * p.Quantity,
            Uncosted = p.UnitCostAtUsage == null,
            UsedDate = p.MaintenanceOrder.ClosedDate ?? p.MaintenanceOrder.CreatedDate,
        });

        var byPart = Merge(await GroupByAsync(woRows, Dimension.Part), await GroupByAsync(moRows, Dimension.Part));
        var byAssetAgg = Merge(await GroupByAsync(woRows, Dimension.Asset), await GroupByAsync(moRows, Dimension.Asset));
        var byCategoryAgg = Merge(await GroupByAsync(woRows, Dimension.Category), await GroupByAsync(moRows, Dimension.Category));

        // Distinct part count per asset has to span both tables, so the distinct (asset, part)
        // pairs come back and are counted here — one row per real pairing, not per usage.
        var woPairs = await woRows.Select(r => new { r.AssetId, r.SparePartId }).Distinct().ToListAsync();
        var moPairs = await moRows.Select(r => new { r.AssetId, r.SparePartId }).Distinct().ToListAsync();
        var distinctParts = woPairs.Concat(moPairs).Distinct()
            .GroupBy(x => x.AssetId)
            .ToDictionary(g => g.Key, g => g.Count());

        var topPartIds = byPart.Values.OrderByDescending(x => x.Quantity).Take(20).Select(x => x.Key).ToList();
        var partNames = await _db.SpareParts.AsNoTracking().Where(p => topPartIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);
        var mostUsed = byPart.Values.OrderByDescending(x => x.Quantity).Take(20)
            .Select(x => new SparePartUsageSummary
            {
                SparePartId = x.Key,
                Name = partNames.GetValueOrDefault(x.Key) ?? "—",
                TotalQuantity = x.Quantity,
                UsageCount = x.Count,
                TotalCost = x.Cost,
            }).ToList();

        var assetIds = byAssetAgg.Keys.ToList();
        var assetLabels = await _db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.AssetTag + " — " + a.Name);
        var byAsset = byAssetAgg.Values.OrderByDescending(x => x.Cost)
            .Select(x => new AssetUsageSummary
            {
                AssetId = x.Key,
                AssetLabel = assetLabels.GetValueOrDefault(x.Key) ?? "—",
                TotalQuantity = x.Quantity,
                TotalCost = x.Cost,
                DistinctParts = distinctParts.GetValueOrDefault(x.Key),
            }).ToList();

        var categoryIds = byCategoryAgg.Keys.ToList();
        var categoryRows = await _db.AssetCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.NameAr }).ToListAsync();
        var categoryLabels = categoryRows.ToDictionary(c => c.Id, c => _loc.LocalizedName(c.Name, c.NameAr));
        var byCategory = byCategoryAgg.Values.OrderByDescending(x => x.Cost)
            .Select(x => new CategoryUsageSummary
            {
                CategoryId = x.Key,
                CategoryLabel = categoryLabels.GetValueOrDefault(x.Key) ?? "—",
                TotalQuantity = x.Quantity,
                TotalCost = x.Cost,
            }).ToList();

        var costByMonth = (await GroupByMonthAsync(woRows)).Concat(await GroupByMonthAsync(moRows))
            .GroupBy(x => new { x.Year, x.Month })
            .Select(g => new MonthlyCostRow { Year = g.Key.Year, Month = g.Key.Month, Cost = g.Sum(x => x.Cost) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();

        ViewBag.MostUsed = mostUsed;
        ViewBag.ByAsset = byAsset;
        ViewBag.ByCategory = byCategory;
        ViewBag.TotalCost = byPart.Values.Sum(x => x.Cost);
        ViewBag.CostByMonth = costByMonth;
        ViewBag.HasUncostedRows = await woRows.AnyAsync(r => r.Uncosted) || await moRows.AnyAsync(r => r.Uncosted);
        ViewBag.Categories = await _lookups.TopLevelCategoriesAsync();
        ViewBag.From = from; ViewBag.To = to; ViewBag.AssetId = assetId; ViewBag.CategoryId = categoryId;
        return View();
    }

    private enum Dimension { Part, Asset, Category }

    private static Task<List<Agg>> GroupByAsync(IQueryable<UsageProjection> rows, Dimension by)
    {
        var keyed = by switch
        {
            Dimension.Part => rows.GroupBy(r => r.SparePartId),
            Dimension.Asset => rows.GroupBy(r => r.AssetId),
            _ => rows.GroupBy(r => r.CategoryId),
        };
        return keyed
            .Select(g => new Agg { Key = g.Key, Quantity = g.Sum(r => r.Quantity), Count = g.Count(), Cost = g.Sum(r => r.Cost) })
            .ToListAsync();
    }

    private static Task<List<MonthAgg>> GroupByMonthAsync(IQueryable<UsageProjection> rows) =>
        rows.GroupBy(r => new { r.UsedDate.Year, r.UsedDate.Month })
            .Select(g => new MonthAgg { Year = g.Key.Year, Month = g.Key.Month, Cost = g.Sum(r => r.Cost) })
            .ToListAsync();

    private static Dictionary<int, Agg> Merge(List<Agg> left, List<Agg> right)
    {
        var result = left.ToDictionary(x => x.Key);
        foreach (var r in right)
            result[r.Key] = result.TryGetValue(r.Key, out var e)
                ? new Agg { Key = r.Key, Quantity = e.Quantity + r.Quantity, Count = e.Count + r.Count, Cost = e.Cost + r.Cost }
                : r;
        return result;
    }

    private sealed class UsageProjection
    {
        public int SparePartId { get; set; }
        public int AssetId { get; set; }
        public int CategoryId { get; set; }
        public int Quantity { get; set; }
        public decimal Cost { get; set; }
        public bool Uncosted { get; set; }
        public DateTime UsedDate { get; set; }
    }

    private sealed class Agg
    {
        public int Key { get; set; }
        public int Quantity { get; set; }
        public int Count { get; set; }
        public decimal Cost { get; set; }
    }

    private sealed class MonthAgg
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Cost { get; set; }
    }
}
