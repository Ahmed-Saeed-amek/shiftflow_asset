using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class ZonesController : Controller
{
    private readonly ApplicationDbContext _db;
    public ZonesController(ApplicationDbContext db) => _db = db;

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Index()
    {
        var zones = await _db.Zones
            .Include(z => z.LocationCategory)
            .Include(z => z.Assets)
            .OrderBy(z => z.LocationCategory!.Name).ThenBy(z => z.Name)
            .ToListAsync();
        return View(zones);
    }

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Details(int id)
    {
        var zone = await _db.Zones
            .Include(z => z.LocationCategory)
            .Include(z => z.Assets).ThenInclude(a => a.Category)
            .FirstOrDefaultAsync(z => z.Id == id);
        if (zone == null) return NotFound();
        return View(zone);
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
        if (await _db.Zones.AnyAsync(z => z.LocationCategoryId == vm.LocationCategoryId && z.Name == vm.Name))
        {
            ModelState.AddModelError(nameof(vm.Name), "A zone with this name already exists in this location category.");
            await PopulateLookupsAsync();
            return View(vm);
        }
        _db.Zones.Add(new Zone
        {
            Name = vm.Name, NameAr = vm.NameAr, LocationCategoryId = vm.LocationCategoryId, Address = vm.Address,
            Latitude = vm.Latitude, Longitude = vm.Longitude, CreatedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Asset location created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.AssetManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var zone = await _db.Zones.FirstOrDefaultAsync(z => z.Id == id);
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
        if (await _db.Zones.AnyAsync(z => z.Id != vm.Id && z.LocationCategoryId == vm.LocationCategoryId && z.Name == vm.Name))
        {
            ModelState.AddModelError(nameof(vm.Name), "A zone with this name already exists in this location category.");
            await PopulateLookupsAsync();
            return View(vm);
        }
        zone.Name = vm.Name; zone.NameAr = vm.NameAr; zone.LocationCategoryId = vm.LocationCategoryId; zone.Address = vm.Address;
        zone.Latitude = vm.Latitude; zone.Longitude = vm.Longitude;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Asset location updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Zone list for a given category — the picker's second (and final) step.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByCategory(int locationCategoryId)
    {
        var zones = await _db.Zones.Where(z => z.LocationCategoryId == locationCategoryId)
            .OrderBy(z => z.Name).Select(z => new { z.Id, z.Name, z.NameAr }).ToListAsync();
        return Json(zones);
    }

    /// <summary>JSON feed for the map view / overview pins (only zones with coordinates).</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> MapData()
    {
        var zones = await _db.Zones
            .Include(z => z.LocationCategory)
            .Where(z => z.Latitude != null && z.Longitude != null)
            .Select(z => new
            {
                z.Id, z.Name, z.Latitude, z.Longitude,
                CategoryName = z.LocationCategory!.Name,
                AssetCount = z.Assets.Count,
            })
            .ToListAsync();
        return Json(zones);
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.LocationCategories = await _db.LocationCategories.OrderBy(c => c.Id).ToListAsync();
    }
}
