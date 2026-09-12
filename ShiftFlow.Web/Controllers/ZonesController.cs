using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class ZonesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILanguageService _loc;
    private readonly IAssetScopeService _scope;
    private readonly ILookupCache _lookups;
    private readonly UserManager<ApplicationUser> _um;
    public ZonesController(ApplicationDbContext db, ILanguageService loc, IAssetScopeService scope, ILookupCache lookups, UserManager<ApplicationUser> um)
    {
        _db = db; _loc = loc; _scope = scope; _lookups = lookups; _um = um;
    }

    private const int PageSize = 25;

    /// <summary>The caller's scoped Assets set, composable as a correlated subquery — the same
    /// UserAssetScope ZoneOverviewController and AssetsController enforce. Every asset count and
    /// asset list on this controller goes through it, so a scoped user's zone rows never advertise
    /// assets they can't open.</summary>
    private Task<IQueryable<Asset>> ScopedAssetsAsync() =>
        _scope.GetScopedAssetsAsync(_um.GetUserId(User)!);

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Index(int page = 1)
    {
        if (page < 1) page = 1;
        var scopedAssets = await ScopedAssetsAsync();
        var query = _db.Zones.AsNoTracking()
            .OrderBy(z => z.LocationCategory!.Name).ThenBy(z => z.Name);

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages) page = totalPages;

        // Counts are projected server-side instead of Include(z => z.Assets) pulling every asset row
        // into memory just to call .Count on it.
        var zones = await query.Skip((page - 1) * PageSize).Take(PageSize)
            .Select(z => new ZoneRow
            {
                Id = z.Id, Name = z.Name, NameAr = z.NameAr,
                LocationCategoryName = z.LocationCategory!.Name,
                LocationCategoryNameAr = z.LocationCategory.NameAr,
                Latitude = z.Latitude, Longitude = z.Longitude,
                AssetCount = scopedAssets.Count(a => a.ZoneId == z.Id),
            })
            .ToListAsync();

        return View(new ZoneIndexViewModel
        {
            Zones = zones,
            TotalCount = totalCount,
            Pagination = new PaginationModel { Page = page, TotalPages = totalPages },
        });
    }

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Details(int id)
    {
        var zone = await _db.Zones.AsNoTracking()
            .Include(z => z.LocationCategory)
            .FirstOrDefaultAsync(z => z.Id == id);
        if (zone == null) return NotFound();
        var assets = await (await ScopedAssetsAsync())
            .Include(a => a.Category)
            .Where(a => a.ZoneId == id)
            .OrderBy(a => a.AssetTag)
            .ToListAsync();
        return View(new ZoneDetailsViewModel { Zone = zone, Assets = assets });
    }

    [Authorize(Policy = PermissionCatalog.AssetManage)]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new ZoneViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ZoneViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View(vm); }
        // A stale dropdown value or a raw/tampered POST with a non-existent LocationCategoryId
        // otherwise hits the DB's Restrict FK constraint and raises an unhandled DbUpdateException.
        if (!await _db.LocationCategories.AsNoTracking().AnyAsync(c => c.Id == vm.LocationCategoryId))
        {
            ModelState.AddModelError(nameof(vm.LocationCategoryId), _loc.T("Selected location type not found."));
            await PopulateLookupsAsync();
            return View(vm);
        }
        if (await _db.Zones.AsNoTracking().AnyAsync(z => z.LocationCategoryId == vm.LocationCategoryId && z.Name == vm.Name))
        {
            ModelState.AddModelError(nameof(vm.Name), _loc.T("A zone with this name already exists in this location category."));
            await PopulateLookupsAsync();
            return View(vm);
        }
        _db.Zones.Add(new Zone
        {
            Name = vm.Name, NameAr = vm.NameAr, LocationCategoryId = vm.LocationCategoryId, Address = vm.Address,
            Latitude = vm.Latitude, Longitude = vm.Longitude, CreatedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        _lookups.InvalidateZones();
        TempData["Success"] = _loc.T("Asset location created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.AssetManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var zone = await _db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id);
        if (zone == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new ZoneViewModel
        {
            Id = zone.Id, Name = zone.Name, NameAr = zone.NameAr,
            LocationCategoryId = zone.LocationCategoryId,
            Address = zone.Address, Latitude = zone.Latitude, Longitude = zone.Longitude,
        });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ZoneViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View(vm); }
        var zone = await _db.Zones.FindAsync(vm.Id);
        if (zone == null) return NotFound();
        if (!await _db.LocationCategories.AsNoTracking().AnyAsync(c => c.Id == vm.LocationCategoryId))
        {
            ModelState.AddModelError(nameof(vm.LocationCategoryId), _loc.T("Selected location type not found."));
            await PopulateLookupsAsync();
            return View(vm);
        }
        if (await _db.Zones.AsNoTracking().AnyAsync(z => z.Id != vm.Id && z.LocationCategoryId == vm.LocationCategoryId && z.Name == vm.Name))
        {
            ModelState.AddModelError(nameof(vm.Name), _loc.T("A zone with this name already exists in this location category."));
            await PopulateLookupsAsync();
            return View(vm);
        }
        zone.Name = vm.Name; zone.NameAr = vm.NameAr; zone.LocationCategoryId = vm.LocationCategoryId; zone.Address = vm.Address;
        zone.Latitude = vm.Latitude; zone.Longitude = vm.Longitude;
        await _db.SaveChangesAsync();
        _lookups.InvalidateZones();
        TempData["Success"] = _loc.T("Asset location updated.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Zone list for a given category — the picker's second (and final) step.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByCategory(int locationCategoryId)
    {
        var zones = await _db.Zones.AsNoTracking().Where(z => z.LocationCategoryId == locationCategoryId)
            .OrderBy(z => z.Name).Select(z => new { z.Id, z.Name, z.NameAr }).ToListAsync();
        return Json(zones);
    }

    /// <summary>JSON feed for the map view / overview pins (only zones with coordinates).</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> MapData()
    {
        var scopedAssets = await ScopedAssetsAsync();
        var zones = await _db.Zones.AsNoTracking()
            .Where(z => z.Latitude != null && z.Longitude != null)
            .Select(z => new
            {
                z.Id, z.Name, z.Latitude, z.Longitude,
                CategoryName = z.LocationCategory!.Name,
                AssetCount = scopedAssets.Count(a => a.ZoneId == z.Id),
            })
            .ToListAsync();
        return Json(zones);
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.LocationCategories = await _lookups.LocationCategoriesAsync();
    }
}
