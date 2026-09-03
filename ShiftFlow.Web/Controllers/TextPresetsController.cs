using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

/// <summary>Admin management of canned text ("presets") that let an employee pick from a dropdown
/// instead of typing a field freehand — one shared catalog across every field wired up this way
/// (see TextPreset.Fields for the list), each optionally scoped narrower within its own field.</summary>
[Authorize]
public class TextPresetsController : Controller
{
    private readonly ApplicationDbContext _db;
    public TextPresetsController(ApplicationDbContext db) => _db = db;

    [Authorize(Policy = PermissionCatalog.AssetCategoryManage)]
    public async Task<IActionResult> Index(string? field)
    {
        field ??= TextPreset.Fields.OrderDescription;
        var presets = await _db.TextPresets.Include(t => t.OrderType).Include(t => t.Category)
            .Where(t => t.FieldKey == field).OrderBy(t => t.SortOrder).ThenBy(t => t.Text).ToListAsync();
        ViewBag.Field = field;
        ViewBag.OrderTypes = await _db.OrderTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Include(c => c.Subcategories).Where(c => c.ParentCategoryId == null)
            .OrderBy(c => c.Name).ToListAsync();
        return View(presets);
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TextPresetViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Text is required.";
            return RedirectToAction(nameof(Index), new { field = vm.FieldKey });
        }
        _db.TextPresets.Add(new TextPreset
        {
            FieldKey = vm.FieldKey, OrderTypeId = vm.OrderTypeId, CategoryId = vm.CategoryId,
            Text = vm.Text, TextAr = vm.TextAr, IsActive = vm.IsActive, SortOrder = vm.SortOrder,
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Preset created.";
        return RedirectToAction(nameof(Index), new { field = vm.FieldKey });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetCategoryManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TextPresetViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Text is required.";
            return RedirectToAction(nameof(Index), new { field = vm.FieldKey });
        }
        var preset = await _db.TextPresets.FindAsync(vm.Id);
        if (preset == null) return NotFound();
        preset.OrderTypeId = vm.OrderTypeId; preset.CategoryId = vm.CategoryId;
        preset.Text = vm.Text; preset.TextAr = vm.TextAr; preset.IsActive = vm.IsActive; preset.SortOrder = vm.SortOrder;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Preset updated.";
        return RedirectToAction(nameof(Index), new { field = vm.FieldKey });
    }

    /// <summary>Cascading data source for a field's preset dropdown — an OrderType-scoped field
    /// (orderTypeId given) or a Category-scoped field (categoryId given, inheriting its parent
    /// category's presets too, same rule as MaintenanceActionTypesController.ByCategory), plus every
    /// scope-agnostic preset for that field.</summary>
    [Authorize]
    public async Task<IActionResult> ByField(string fieldKey, int? orderTypeId, int? categoryId)
    {
        var query = _db.TextPresets.Where(t => t.IsActive && t.FieldKey == fieldKey);

        if (orderTypeId.HasValue)
        {
            query = query.Where(t => t.OrderTypeId == null || t.OrderTypeId == orderTypeId);
        }
        if (categoryId.HasValue)
        {
            var category = await _db.AssetCategories.FindAsync(categoryId.Value);
            var categoryIds = new List<int> { categoryId.Value };
            if (category?.ParentCategoryId != null) categoryIds.Add(category.ParentCategoryId.Value);
            query = query.Where(t => t.CategoryId == null || categoryIds.Contains(t.CategoryId.Value));
        }

        var presets = await query.OrderBy(t => t.SortOrder).ThenBy(t => t.Text)
            .Select(t => new { t.Id, t.Text, t.TextAr }).ToListAsync();
        return Json(presets);
    }
}
