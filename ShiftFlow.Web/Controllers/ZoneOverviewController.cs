using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public ZoneOverviewController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var zones = await _db.Zones.Include(z => z.LocationCategory)
            .OrderBy(z => z.LocationCategory!.Name).ThenBy(z => z.Name)
            .ToListAsync();

        var zoneStatus = (await _db.Assets.AsNoTracking()
            .GroupBy(a => a.ZoneId)
            .Select(g => new
            {
                ZoneId = g.Key,
                HasDefective = g.Any(a => a.Status == "Defective"),
                HasMaintenance = g.Any(a => a.Status == "Maintenance"),
            })
            .ToListAsync())
            .ToDictionary(x => x.ZoneId, x => (x.HasDefective, x.HasMaintenance));

        var dispatchedByZone = await _db.Assets.AsNoTracking()
            .Where(a => a.WorkOrders.Any())
            .GroupBy(a => a.ZoneId)
            .Select(g => new { ZoneId = g.Key, Count = g.SelectMany(a => a.WorkOrders).Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count);

        // Most recent work order dispatched to any asset in the zone — "last visited" for a
        // quick-glance read of which zones haven't seen activity in a while.
        var lastVisitedByZone = await _db.Assets.AsNoTracking()
            .Where(a => a.WorkOrders.Any())
            .GroupBy(a => a.ZoneId)
            .Select(g => new { ZoneId = g.Key, LastVisited = g.SelectMany(a => a.WorkOrders).Max(w => w.CreatedDate) })
            .ToDictionaryAsync(x => x.ZoneId, x => x.LastVisited);

        ViewBag.ZoneStatus = zoneStatus;
        ViewBag.DispatchedByZone = dispatchedByZone;
        ViewBag.LastVisitedByZone = lastVisitedByZone;
        return View(zones);
    }

    public async Task<IActionResult> Details(int zoneId)
    {
        var zone = await _db.Zones.Include(z => z.LocationCategory).FirstOrDefaultAsync(z => z.Id == zoneId);
        if (zone == null) return NotFound();

        ViewBag.ProblemAssets = await _db.Assets.AsNoTracking()
            .Include(a => a.Category)
            .Where(a => a.ZoneId == zoneId && (a.Status == "Defective" || a.Status == "Maintenance"))
            .OrderBy(a => a.AssetTag)
            .ToListAsync();

        // The zone list's "Dispatched Orders" count comes from this same query (work orders on
        // any asset in the zone) — show the actual orders here so clicking "View" delivers on
        // what that column promises, instead of only ever showing the unrelated problem-assets list.
        ViewBag.DispatchedOrders = await _db.WorkOrders.AsNoTracking()
            .Include(w => w.Asset)
            .Where(w => w.Asset!.ZoneId == zoneId)
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
        var assets = await _db.Assets.AsNoTracking()
            .Include(a => a.Zone).ThenInclude(z => z!.LocationCategory)
            .Where(a => a.Zone != null && a.Zone.Latitude != null && a.Zone.Longitude != null)
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
