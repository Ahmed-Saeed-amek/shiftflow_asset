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

    [Authorize(Policy = PermissionCatalog.AssetCategoryManage)]
    public async Task<IActionResult> Index()
    {
        var types = await _db.MaintenanceActionTypes.Include(m => m.Category)
            .OrderBy(m => m.Category == null ? "" : m.Category.Name).ThenBy(m => m.Name).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Include(c => c.Subcategories).Where(c => c.ParentCategoryId == null)
            .OrderBy(c => c.Name).ToListAsync();
        return View(types);
    }

    // Create/Edit are modals on Index now (this list is small and single-purpose enough that a
    // separate full-page form was pure overhead) — both just redirect back to Index either way,
    // so an invalid submit reports the error there instead of returning a page that no longer exists.
    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MaintenanceActionTypeViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        _db.MaintenanceActionTypes.Add(new MaintenanceActionType { CategoryId = vm.CategoryId, Name = vm.Name, NameAr = vm.NameAr, IsActive = vm.IsActive });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Maintenance action created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MaintenanceActionTypeViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        var type = await _db.MaintenanceActionTypes.FindAsync(vm.Id);
        if (type == null) return NotFound();
        type.CategoryId = vm.CategoryId; type.Name = vm.Name; type.NameAr = vm.NameAr; type.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Maintenance action updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Cascading data source for the inspection checklist's maintenance-action picker — a
    /// subcategory-scoped asset also gets its parent category's types, same inheritance rule as
    /// AssetActionTypesController.ByCategory, plus every category-agnostic (CategoryId == null) type.</summary>
    [Authorize]
    public async Task<IActionResult> ByCategory(int categoryId)
    {
        var category = await _db.AssetCategories.FindAsync(categoryId);
        var categoryIds = new List<int> { categoryId };
        if (category?.ParentCategoryId != null) categoryIds.Add(category.ParentCategoryId.Value);

        var types = await _db.MaintenanceActionTypes
            .Where(m => m.IsActive && (m.CategoryId == null || categoryIds.Contains(m.CategoryId.Value)))
            .OrderBy(m => m.Name).Select(m => new { m.Id, m.Name, m.NameAr }).ToListAsync();
        return Json(types);
    }
}
