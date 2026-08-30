using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class AssetCategoriesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILanguageService _loc;
    public AssetCategoriesController(ApplicationDbContext db, ILanguageService loc) { _db = db; _loc = loc; }

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Index()
    {
        var categories = await _db.AssetCategories.Include(c => c.Subcategories)
            .OrderBy(c => c.Name).ToListAsync();
        return View(categories.Where(c => c.ParentCategoryId == null).ToList());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetCategoryViewModel vm)
    {
        if (vm.ParentCategoryId.HasValue)
        {
            var parent = await _db.AssetCategories.FindAsync(vm.ParentCategoryId.Value);
            if (parent?.ParentCategoryId != null)
                ModelState.AddModelError(nameof(vm.ParentCategoryId), _loc.T("Only 2 levels are supported — a subcategory can't itself have a parent that's already a subcategory."));
        }
        if (!string.IsNullOrWhiteSpace(vm.Name) &&
            await _db.AssetCategories.AnyAsync(c => c.ParentCategoryId == vm.ParentCategoryId && c.Name == vm.Name))
            ModelState.AddModelError(nameof(vm.Name), _loc.T("A category with this name already exists at this level."));

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        _db.AssetCategories.Add(new AssetCategory { Name = vm.Name, NameAr = vm.NameAr, ParentCategoryId = vm.ParentCategoryId, CreatedDate = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        TempData["Success"] = _loc.T("Asset category created.");
        return RedirectToAction(nameof(Index));
    }

    // Edit is a modal on Index now, matching Create — a separate full page was pure overhead
    // for 3 fields. Invalid submits report the error on Index instead of returning a page
    // that no longer exists.
    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AssetCategoryViewModel vm)
    {
        if (vm.ParentCategoryId.HasValue)
        {
            if (vm.ParentCategoryId == vm.Id)
                ModelState.AddModelError(nameof(vm.ParentCategoryId), _loc.T("A category can't be its own parent."));
            else
            {
                var parent = await _db.AssetCategories.FindAsync(vm.ParentCategoryId.Value);
                if (parent?.ParentCategoryId != null)
                    ModelState.AddModelError(nameof(vm.ParentCategoryId), _loc.T("Only 2 levels are supported — a subcategory can't itself have a parent that's already a subcategory."));
            }
            var hasChildren = await _db.AssetCategories.AnyAsync(c => c.ParentCategoryId == vm.Id);
            if (hasChildren)
                ModelState.AddModelError(nameof(vm.ParentCategoryId), _loc.T("This category already has subcategories, so it can't become a subcategory itself."));
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        var category = await _db.AssetCategories.FindAsync(vm.Id);
        if (category == null) return NotFound();
        category.Name = vm.Name; category.NameAr = vm.NameAr; category.ParentCategoryId = vm.ParentCategoryId;
        await _db.SaveChangesAsync();
        TempData["Success"] = _loc.T("Asset category updated.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Cascading dropdown data source for the Asset form's Category → Subcategory picker.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByParent(int parentId)
    {
        var subcategories = await _db.AssetCategories.Where(c => c.ParentCategoryId == parentId)
            .OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.NameAr }).ToListAsync();
        return Json(subcategories);
    }
}
