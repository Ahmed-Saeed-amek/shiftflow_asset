using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

/// <summary>Admin management of per-category Action Types and their Causes — the dropdown option sets employees pick from when reporting an action (see WorkOrdersController.Report).</summary>
[Authorize]
public class AssetActionTypesController : Controller
{
    private readonly ApplicationDbContext _db;
    public AssetActionTypesController(ApplicationDbContext db) => _db = db;

    [Authorize(Policy = PermissionCatalog.AssetCategoryManage)]
    public async Task<IActionResult> Index(int categoryId)
    {
        var category = await _db.AssetCategories.FindAsync(categoryId);
        if (category == null) return NotFound();
        var actionTypes = await _db.AssetActionTypes.Include(a => a.Causes)
            .Where(a => a.CategoryId == categoryId).OrderBy(a => a.Name).ToListAsync();
        ViewBag.Category = category;
        return View(actionTypes);
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetActionTypeViewModel vm)
    {
        if (ModelState.IsValid)
        {
            _db.AssetActionTypes.Add(new AssetActionType { CategoryId = vm.CategoryId, Name = vm.Name, NameAr = vm.NameAr, IsActive = true });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Action type added.";
        }
        return RedirectToAction(nameof(Index), new { categoryId = vm.CategoryId });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AssetActionTypeViewModel vm)
    {
        var actionType = await _db.AssetActionTypes.FindAsync(vm.Id);
        if (actionType != null)
        {
            actionType.Name = vm.Name; actionType.NameAr = vm.NameAr; actionType.IsActive = vm.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Action type updated.";
        }
        return RedirectToAction(nameof(Index), new { categoryId = vm.CategoryId });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCause(AssetActionCauseViewModel vm)
    {
        var actionType = await _db.AssetActionTypes.FindAsync(vm.ActionTypeId);
        if (actionType != null && ModelState.IsValid)
        {
            _db.AssetActionCauses.Add(new AssetActionCause { ActionTypeId = vm.ActionTypeId, Name = vm.Name, NameAr = vm.NameAr, IsActive = true });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Cause added.";
        }
        return RedirectToAction(nameof(Index), new { categoryId = actionType?.CategoryId });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCause(AssetActionCauseViewModel vm)
    {
        var cause = await _db.AssetActionCauses.Include(c => c.ActionType).FirstOrDefaultAsync(c => c.Id == vm.Id);
        if (cause != null)
        {
            cause.Name = vm.Name; cause.NameAr = vm.NameAr; cause.IsActive = vm.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Cause updated.";
        }
        return RedirectToAction(nameof(Index), new { categoryId = cause?.ActionType?.CategoryId });
    }

    /// <summary>Cascading dropdown data source for the Report Action form. A subcategory (e.g. HVAC → Split Units) inherits its parent category's action types on top of any it defines itself, so admins don't have to redefine "Report Failure" etc. per subcategory.</summary>
    [Authorize(Policy = PermissionCatalog.AssetReportAction)]
    public async Task<IActionResult> ByCategory(int categoryId)
    {
        var category = await _db.AssetCategories.FindAsync(categoryId);
        var categoryIds = new List<int> { categoryId };
        if (category?.ParentCategoryId != null) categoryIds.Add(category.ParentCategoryId.Value);

        var actionTypes = await _db.AssetActionTypes.Where(a => categoryIds.Contains(a.CategoryId) && a.IsActive)
            .OrderBy(a => a.Name).Select(a => new { a.Id, a.Name, a.NameAr }).ToListAsync();
        return Json(actionTypes);
    }

    /// <summary>Cascading dropdown data source for the Report Action form.</summary>
    [Authorize(Policy = PermissionCatalog.AssetReportAction)]
    public async Task<IActionResult> CausesByActionType(int actionTypeId)
    {
        var causes = await _db.AssetActionCauses.Where(c => c.ActionTypeId == actionTypeId && c.IsActive)
            .OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.NameAr }).ToListAsync();
        return Json(causes);
    }
}
