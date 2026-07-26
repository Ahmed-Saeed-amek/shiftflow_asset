using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

/// <summary>Lets a manager (Asset.ScopeManage) restrict which zone/area/category of assets a specific employee can see on the Assets list. At most one scope per user — see UserAssetScope.</summary>
[Authorize(Policy = PermissionCatalog.AssetScopeManage)]
public class UserAssetScopesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILanguageService _loc;
    public UserAssetScopesController(ApplicationDbContext db, ILanguageService loc) { _db = db; _loc = loc; }

    public async Task<IActionResult> Index()
    {
        var scopes = await _db.UserAssetScopes.Include(s => s.User).OrderBy(s => s.User!.FullName).ToListAsync();
        var labels = new Dictionary<int, string>();
        foreach (var s in scopes)
            labels[s.Id] = await DescribeScopeValueAsync(s.ScopeType, s.ScopeValueId);
        ViewBag.ScopeValueLabels = labels;
        return View(scopes);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new UserAssetScopeViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserAssetScopeViewModel vm)
    {
        if (await _db.UserAssetScopes.AnyAsync(s => s.UserId == vm.UserId))
            ModelState.AddModelError(nameof(vm.UserId), _loc.T("This user already has a scope assigned — edit or remove it first."));
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View(vm); }
        _db.UserAssetScopes.Add(new UserAssetScope { UserId = vm.UserId, ScopeType = vm.ScopeType, ScopeValueId = vm.ScopeValueId });
        await _db.SaveChangesAsync();
        TempData["Success"] = _loc.T("Scope assigned.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var scope = await _db.UserAssetScopes.Include(s => s.User).FirstOrDefaultAsync(s => s.Id == id);
        if (scope == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.SelectedUserName = scope.User?.FullName;
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new UserAssetScopeViewModel { Id = scope.Id, UserId = scope.UserId, ScopeType = scope.ScopeType, ScopeValueId = scope.ScopeValueId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserAssetScopeViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View(vm); }
        var scope = await _db.UserAssetScopes.FindAsync(vm.Id);
        if (scope == null) return NotFound();
        scope.ScopeType = vm.ScopeType; scope.ScopeValueId = vm.ScopeValueId;
        await _db.SaveChangesAsync();
        TempData["Success"] = _loc.T("Scope updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var scope = await _db.UserAssetScopes.FindAsync(id);
        if (scope != null) { _db.UserAssetScopes.Remove(scope); await _db.SaveChangesAsync(); TempData["Success"] = _loc.T("Scope removed."); }
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> DescribeScopeValueAsync(string scopeType, int valueId) => scopeType switch
    {
        "Zone" => (await _db.Zones.FindAsync(valueId))?.Name ?? "—",
        "Area" => (await _db.Areas.FindAsync(valueId))?.Name ?? "—",
        "Category" => (await _db.AssetCategories.FindAsync(valueId))?.Name ?? "—",
        _ => "—",
    };

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Zones = await _db.Zones.OrderBy(z => z.Name).ToListAsync();
        ViewBag.Areas = await _db.Areas.OrderBy(a => a.Name).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.OrderBy(c => c.Name).ToListAsync();
    }
}
