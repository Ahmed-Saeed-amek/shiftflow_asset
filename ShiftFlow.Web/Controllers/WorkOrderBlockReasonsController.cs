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

    [Authorize(Policy = PermissionCatalog.AssetCategoryManage)]
    public async Task<IActionResult> Index()
    {
        var reasons = await _db.WorkOrderBlockReasons.OrderBy(r => r.Name).ToListAsync();
        return View(reasons);
    }

    // Create/Edit are modals on Index now (this list is small and single-purpose enough that a
    // separate full-page form was pure overhead) — both just redirect back to Index either way,
    // so an invalid submit reports the error there instead of returning a page that no longer exists.
    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkOrderBlockReasonViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.WorkOrderBlockReasons.AnyAsync(r => r.Name == vm.Name))
        {
            TempData["Error"] = $"A block reason named '{vm.Name}' already exists.";
            return RedirectToAction(nameof(Index));
        }
        _db.WorkOrderBlockReasons.Add(new WorkOrderBlockReason { Name = vm.Name, NameAr = vm.NameAr, IsActive = vm.IsActive });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Block reason created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(WorkOrderBlockReasonViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        var reason = await _db.WorkOrderBlockReasons.FindAsync(vm.Id);
        if (reason == null) return NotFound();
        if (await _db.WorkOrderBlockReasons.AnyAsync(r => r.Id != vm.Id && r.Name == vm.Name))
        {
            TempData["Error"] = $"A block reason named '{vm.Name}' already exists.";
            return RedirectToAction(nameof(Index));
        }
        reason.Name = vm.Name; reason.NameAr = vm.NameAr; reason.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Block reason updated.";
        return RedirectToAction(nameof(Index));
    }
}
