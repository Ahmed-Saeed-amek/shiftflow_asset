using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class MaintenanceActionTypesController : Controller
{
    private readonly ApplicationDbContext _db;
    public MaintenanceActionTypesController(ApplicationDbContext db) => _db = db;

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public async Task<IActionResult> Index()
    {
        var types = await _db.MaintenanceActionTypes.OrderBy(m => m.Name).ToListAsync();
        return View(types);
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public IActionResult Create()
    {
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new MaintenanceActionTypeViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MaintenanceActionTypeViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.MaintenanceActionTypes.Add(new MaintenanceActionType { Name = vm.Name, NameAr = vm.NameAr, IsActive = vm.IsActive });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Maintenance action created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var type = await _db.MaintenanceActionTypes.FindAsync(id);
        if (type == null) return NotFound();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new MaintenanceActionTypeViewModel { Id = type.Id, Name = type.Name, NameAr = type.NameAr, IsActive = type.IsActive });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MaintenanceActionTypeViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var type = await _db.MaintenanceActionTypes.FindAsync(vm.Id);
        if (type == null) return NotFound();
        type.Name = vm.Name; type.NameAr = vm.NameAr; type.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Maintenance action updated.";
        return RedirectToAction(nameof(Index));
    }
}
