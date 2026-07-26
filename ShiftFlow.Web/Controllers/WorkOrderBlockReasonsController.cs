using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class WorkOrderBlockReasonsController : Controller
{
    private readonly ApplicationDbContext _db;
    public WorkOrderBlockReasonsController(ApplicationDbContext db) => _db = db;

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public async Task<IActionResult> Index()
    {
        var reasons = await _db.WorkOrderBlockReasons.OrderBy(r => r.Name).ToListAsync();
        return View(reasons);
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public IActionResult Create()
    {
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new WorkOrderBlockReasonViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkOrderBlockReasonViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.WorkOrderBlockReasons.Add(new WorkOrderBlockReason { Name = vm.Name, NameAr = vm.NameAr, IsActive = vm.IsActive });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Block reason created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var reason = await _db.WorkOrderBlockReasons.FindAsync(id);
        if (reason == null) return NotFound();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new WorkOrderBlockReasonViewModel { Id = reason.Id, Name = reason.Name, NameAr = reason.NameAr, IsActive = reason.IsActive });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(WorkOrderBlockReasonViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var reason = await _db.WorkOrderBlockReasons.FindAsync(vm.Id);
        if (reason == null) return NotFound();
        reason.Name = vm.Name; reason.NameAr = vm.NameAr; reason.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Block reason updated.";
        return RedirectToAction(nameof(Index));
    }
}
