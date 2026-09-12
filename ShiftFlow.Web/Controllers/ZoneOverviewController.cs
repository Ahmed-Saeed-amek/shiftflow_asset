using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Controllers;

/// <summary>Latest orders by zone, with each zone's row flagged red/yellow if it currently has a
/// Defective/Maintenance asset — a quick-glance operations view, distinct from the KPI-focused
/// Executive Dashboard.</summary>
[Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
public class ZoneOverviewController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _um;
    private readonly IAssetScopeService _scope;

    public ZoneOverviewController(ApplicationDbContext db, UserManager<ApplicationUser> um, IAssetScopeService scope)
    {
        _db = db; _um = um; _scope = scope;
    }

    /// <summary>The caller's scoped Assets set — the same UserAssetScope enforced everywhere else.
    /// Kept as a composable IQueryable and used as a correlated EXISTS subquery rather than
    /// materialising ids.</summary>
    private Task<IQueryable<Asset>> ScopedAssetsAsync() =>
        _scope.GetScopedAssetsAsync(_um.GetUserId(User)!);

    public async Task<IActionResult> Index()
    {
        var assetQuery = await ScopedAssetsAsync();

        var zones = await _db.Zones.AsNoTracking().Include(z => z.LocationCategory)
            .OrderBy(z => z.LocationCategory!.Name).ThenBy(z => z.Name)
            .ToListAsync();

        var zoneStatus = (await assetQuery
            .GroupBy(a => a.ZoneId)
            .Select(g => new
            {
                ZoneId = g.Key,
                HasDefective = g.Any(a => a.Status == "Defective"),
                HasMaintenance = g.Any(a => a.Status == "Maintenance"),
            })
            .ToListAsync())
            .ToDictionary(x => x.ZoneId, x => (x.HasDefective, x.HasMaintenance));

        var dispatchedByZone = await assetQuery
            .Where(a => a.WorkOrders.Any())
            .GroupBy(a => a.ZoneId)
            .Select(g => new { ZoneId = g.Key, Count = g.SelectMany(a => a.WorkOrders).Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count);

        // Most recent work order dispatched to any asset in the zone — "last visited" for a
        // quick-glance read of which zones haven't seen activity in a while.
        var lastVisitedByZone = await assetQuery
            .Where(a => a.WorkOrders.Any())
            .GroupBy(a => a.ZoneId)
            .Select(g => new { ZoneId = g.Key, LastVisited = g.SelectMany(a => a.WorkOrders).Max(w => w.CreatedDate) })
            .ToDictionaryAsync(x => x.ZoneId, x => x.LastVisited);

        ViewBag.ZoneStatus = zoneStatus;
        ViewBag.DispatchedByZone = dispatchedByZone;
        ViewBag.LastVisitedByZone = lastVisitedByZone;
        return View(zones);
    }

    public async Task<IActionResult> Details(int id)
    {
        var scopedAssets = await ScopedAssetsAsync();

        var zone = await _db.Zones.AsNoTracking().Include(z => z.LocationCategory).FirstOrDefaultAsync(z => z.Id == id);
        if (zone == null) return NotFound();

        var problemAssetsQuery = scopedAssets
            .Include(a => a.Category)
            .Where(a => a.ZoneId == id && (a.Status == "Defective" || a.Status == "Maintenance"));
        ViewBag.ProblemAssets = await problemAssetsQuery
            .OrderBy(a => a.AssetTag)
            .ToListAsync();

        // The zone list's "Dispatched Orders" count comes from this same query (work orders on
        // any asset in the zone) — show the actual orders here so clicking "View" delivers on
        // what that column promises, instead of only ever showing the unrelated problem-assets list.
        var dispatchedOrdersQuery = _db.WorkOrders.AsNoTracking()
            .Include(w => w.Asset)
            .Where(w => w.Asset!.ZoneId == id && scopedAssets.Any(a => a.Id == w.AssetId));
        ViewBag.DispatchedOrders = await dispatchedOrdersQuery
            .OrderByDescending(w => w.CreatedDate)
            .ToListAsync();
        return View(zone);
    }

    /// <summary>One dot per asset for the split-view map — assets share their Zone's coordinates
    /// (assets have no coordinates of their own), so co-located assets stack at the same point.
    /// The client renders them with zIndexOffset by status (Defective on top, then Maintenance,
    /// then Working) so the worst status at a location is always the one visible/clickable.</summary>
    public async Task<IActionResult> AssetMap()
    {
        var query = (await ScopedAssetsAsync())
            .Where(a => a.Zone != null && a.Zone.Latitude != null && a.Zone.Longitude != null);

        var assets = await query
            .Select(a => new
            {
                id = a.Id,
                assetTag = a.AssetTag,
                name = a.Name,
                status = a.Status,
                zoneId = a.ZoneId,
                zoneName = a.Zone!.Name,
                zoneNameAr = a.Zone.NameAr,
                categoryName = a.Zone.LocationCategory != null ? a.Zone.LocationCategory.Name : null,
                lat = a.Zone.Latitude,
                lng = a.Zone.Longitude,
            })
            .ToListAsync();
        return Json(assets);
    }
}
