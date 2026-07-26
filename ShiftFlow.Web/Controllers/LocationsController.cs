using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class LocationsController : Controller
{
    private readonly ApplicationDbContext _db;
    public LocationsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var locations = await _db.Locations.OrderBy(l => l.Name).ToListAsync();
        return View(locations);
    }

    [Authorize(Policy = PermissionCatalog.LocationManage)]
    public IActionResult Create()
    {
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new LocationViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.LocationManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LocationViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.Locations.Add(new Location
        {
            Name = vm.Name, NameAr = vm.NameAr, Code = vm.Code, City = vm.City, Address = vm.Address,
            Status = vm.Status, Notes = vm.Notes, Latitude = vm.Latitude, Longitude = vm.Longitude,
            CreatedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Location created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.LocationManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var loc = await _db.Locations.FindAsync(id);
        if (loc == null) return NotFound();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new LocationViewModel
        {
            Id = loc.Id, Name = loc.Name, NameAr = loc.NameAr, Code = loc.Code, City = loc.City,
            Address = loc.Address, Status = loc.Status, Notes = loc.Notes, Latitude = loc.Latitude, Longitude = loc.Longitude,
        });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.LocationManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(LocationViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var loc = await _db.Locations.FindAsync(vm.Id);
        if (loc == null) return NotFound();
        loc.Name = vm.Name; loc.NameAr = vm.NameAr; loc.Code = vm.Code; loc.City = vm.City; loc.Address = vm.Address;
        loc.Status = vm.Status; loc.Notes = vm.Notes; loc.Latitude = vm.Latitude; loc.Longitude = vm.Longitude;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Location updated.";
        return RedirectToAction(nameof(Index));
    }
}
