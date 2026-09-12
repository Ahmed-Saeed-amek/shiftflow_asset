using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
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
    private readonly IAuditService _audit;
    public UserAssetScopesController(ApplicationDbContext db, ILanguageService loc, IAuditService audit)
    {
        _db = db; _loc = loc; _audit = audit;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Both scope lists plus their human-readable labels, in one pass per table. Each row
    /// used to cost up to three FindAsync round trips to name its zone/location/category.</summary>
    public async Task<IActionResult> Index()
    {
        var scopes = await _db.UserAssetScopes.AsNoTracking().Include(s => s.User).OrderBy(s => s.User!.FullName).ToListAsync();
        var groupScopes = await _db.GroupAssetScopes.AsNoTracking().Include(s => s.Group).OrderBy(s => s.Group!.Name).ToListAsync();

        var names = await LoadScopeNamesAsync(
            scopes.Select(s => (s.ZoneId, s.LocationCategoryId, s.CategoryId))
                .Concat(groupScopes.Select(s => (s.ZoneId, s.LocationCategoryId, s.CategoryId))));

        ViewBag.ScopeValueLabels = scopes.ToDictionary(s => s.Id, s => Describe(s.ZoneId, s.LocationCategoryId, s.CategoryId, names));
        ViewBag.GroupScopeValueLabels = groupScopes.ToDictionary(s => s.Id, s => Describe(s.ZoneId, s.LocationCategoryId, s.CategoryId, names));
        ViewBag.GroupScopes = groupScopes;

        return View(scopes);
    }

    /// <summary>Zone, location-category and asset-category names for every id referenced by the
    /// given scope rows — three queries total, regardless of row count.</summary>
    private async Task<ScopeNames> LoadScopeNamesAsync(IEnumerable<(int? ZoneId, int? LocationCategoryId, int? CategoryId)> rows)
    {
        var all = rows.ToList();
        var zoneIds = all.Where(r => r.ZoneId.HasValue).Select(r => r.ZoneId!.Value).Distinct().ToList();
        var locIds = all.Where(r => r.LocationCategoryId.HasValue).Select(r => r.LocationCategoryId!.Value).Distinct().ToList();
        var catIds = all.Where(r => r.CategoryId.HasValue).Select(r => r.CategoryId!.Value).Distinct().ToList();
        return new ScopeNames(
            await _db.Zones.AsNoTracking().Where(z => zoneIds.Contains(z.Id)).ToDictionaryAsync(z => z.Id, z => z.Name),
            await _db.LocationCategories.AsNoTracking().Where(c => locIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name),
            await _db.AssetCategories.AsNoTracking().Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name));
    }

    private sealed record ScopeNames(
        Dictionary<int, string> Zones,
        Dictionary<int, string> LocationCategories,
        Dictionary<int, string> Categories);

    /// <summary>One description used for both user and group scopes — these were two identical
    /// methods.</summary>
    private string Describe(int? zoneId, int? locationCategoryId, int? categoryId, ScopeNames names)
    {
        var parts = new List<string>();
        if (zoneId.HasValue) parts.Add($"{_loc.T("Zone")}: {names.Zones.GetValueOrDefault(zoneId.Value) ?? "—"}");
        if (locationCategoryId.HasValue) parts.Add($"{_loc.T("Location Category")}: {names.LocationCategories.GetValueOrDefault(locationCategoryId.Value) ?? "—"}");
        if (categoryId.HasValue) parts.Add($"{_loc.T("Category")}: {names.Categories.GetValueOrDefault(categoryId.Value) ?? "—"}");
        return parts.Count > 0 ? string.Join(" + ", parts) : "—";
    }

    private static string ScopeAuditValue(int? zoneId, int? locationCategoryId, int? categoryId) =>
        $"Zone: {zoneId?.ToString() ?? "—"}, LocationCategory: {locationCategoryId?.ToString() ?? "—"}, Category: {categoryId?.ToString() ?? "—"}";

    public async Task<IActionResult> CreateGroup()
    {
        await PopulateLookupsAsync();
        ViewBag.Groups = await _db.Groups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.Name).ToListAsync();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new GroupAssetScopeViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGroup(GroupAssetScopeViewModel vm)
    {
        if (await _db.GroupAssetScopes.AnyAsync(s => s.GroupId == vm.GroupId))
            ModelState.AddModelError(nameof(vm.GroupId), _loc.T("This group already has a scope assigned — edit or remove it first."));
        if (vm.GroupId > 0 && !await _db.Groups.AnyAsync(g => g.Id == vm.GroupId))
            ModelState.AddModelError(nameof(vm.GroupId), _loc.T("Selected group not found."));
        await ValidateScopeReferencesAsync(vm.ZoneId, vm.LocationCategoryId, vm.CategoryId);
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync();
            ViewBag.Groups = await _db.Groups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.Name).ToListAsync();
            return View(vm);
        }
        var createdGroupScope = new GroupAssetScope { GroupId = vm.GroupId, ZoneId = vm.ZoneId, LocationCategoryId = vm.LocationCategoryId, CategoryId = vm.CategoryId };
        _db.GroupAssetScopes.Add(createdGroupScope);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "GroupAssetScope", createdGroupScope.Id.ToString(), CurrentUserId,
            newValue: ScopeAuditValue(createdGroupScope.ZoneId, createdGroupScope.LocationCategoryId, createdGroupScope.CategoryId),
            details: $"Group: {createdGroupScope.GroupId}");
        TempData["Success"] = _loc.T("Scope assigned.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> EditGroup(int id)
    {
        var scope = await _db.GroupAssetScopes.AsNoTracking().Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == id);
        if (scope == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.Groups = await _db.Groups.AsNoTracking().Where(g => g.IsActive || g.Id == scope.GroupId).OrderBy(g => g.Name).ToListAsync();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new GroupAssetScopeViewModel { Id = scope.Id, GroupId = scope.GroupId, ZoneId = scope.ZoneId, LocationCategoryId = scope.LocationCategoryId, CategoryId = scope.CategoryId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditGroup(GroupAssetScopeViewModel vm)
    {
        if (await _db.GroupAssetScopes.AnyAsync(s => s.Id != vm.Id && s.GroupId == vm.GroupId))
            ModelState.AddModelError(nameof(vm.GroupId), _loc.T("This group already has a scope assigned — edit or remove it first."));
        if (vm.GroupId > 0 && !await _db.Groups.AnyAsync(g => g.Id == vm.GroupId))
            ModelState.AddModelError(nameof(vm.GroupId), _loc.T("Selected group not found."));
        await ValidateScopeReferencesAsync(vm.ZoneId, vm.LocationCategoryId, vm.CategoryId);
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync();
            ViewBag.Groups = await _db.Groups.AsNoTracking().Where(g => g.IsActive || g.Id == vm.GroupId).OrderBy(g => g.Name).ToListAsync();
            return View(vm);
        }
        var scope = await _db.GroupAssetScopes.FindAsync(vm.Id);
        if (scope == null) return NotFound();
        var oldGroupValue = ScopeAuditValue(scope.ZoneId, scope.LocationCategoryId, scope.CategoryId);
        scope.GroupId = vm.GroupId; scope.ZoneId = vm.ZoneId; scope.LocationCategoryId = vm.LocationCategoryId; scope.CategoryId = vm.CategoryId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "GroupAssetScope", scope.Id.ToString(), CurrentUserId,
            oldValue: oldGroupValue, newValue: ScopeAuditValue(scope.ZoneId, scope.LocationCategoryId, scope.CategoryId),
            details: $"Group: {scope.GroupId}");
        TempData["Success"] = _loc.T("Scope updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var scope = await _db.GroupAssetScopes.FindAsync(id);
        if (scope != null)
        {
            var oldValue = ScopeAuditValue(scope.ZoneId, scope.LocationCategoryId, scope.CategoryId);
            var scopeGroupId = scope.GroupId;
            _db.GroupAssetScopes.Remove(scope);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Delete", "GroupAssetScope", id.ToString(), CurrentUserId, oldValue: oldValue, details: $"Group: {scopeGroupId}");
            TempData["Success"] = _loc.T("Scope removed.");
        }
        return RedirectToAction(nameof(Index));
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
        // A stale or tampered UserId otherwise reached the DB's FK constraint as an unhandled 500.
        await ValidateUserAsync(vm.UserId);
        await ValidateScopeReferencesAsync(vm.ZoneId, vm.LocationCategoryId, vm.CategoryId);
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync();
            ViewBag.SelectedUserName = (await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vm.UserId))?.FullName;
            return View(vm);
        }
        var created = new UserAssetScope { UserId = vm.UserId, ZoneId = vm.ZoneId, LocationCategoryId = vm.LocationCategoryId, CategoryId = vm.CategoryId };
        _db.UserAssetScopes.Add(created);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "UserAssetScope", created.Id.ToString(), CurrentUserId,
            newValue: ScopeAuditValue(created.ZoneId, created.LocationCategoryId, created.CategoryId), details: $"User: {created.UserId}");
        TempData["Success"] = _loc.T("Scope assigned.");
        return RedirectToAction(nameof(Index));
    }

    // A stale or tampered ZoneId/LocationCategoryId/CategoryId otherwise hits the DB's Restrict FK
    // constraints at SaveChangesAsync and surfaces as an unhandled 500.
    private async Task ValidateUserAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            ModelState.AddModelError("UserId", _loc.T("Please select an employee."));
        else if (!await _db.Users.AnyAsync(u => u.Id == userId))
            ModelState.AddModelError("UserId", _loc.T("Selected employee not found."));
    }

    private async Task ValidateScopeReferencesAsync(int? zoneId, int? locationCategoryId, int? categoryId)
    {
        if (zoneId.HasValue && !await _db.Zones.AnyAsync(z => z.Id == zoneId))
            ModelState.AddModelError("ZoneId", _loc.T("Selected zone not found."));
        if (locationCategoryId.HasValue && !await _db.LocationCategories.AnyAsync(c => c.Id == locationCategoryId))
            ModelState.AddModelError("LocationCategoryId", _loc.T("Selected location type not found."));
        if (categoryId.HasValue && !await _db.AssetCategories.AnyAsync(c => c.Id == categoryId))
            ModelState.AddModelError("CategoryId", _loc.T("Selected category not found."));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var scope = await _db.UserAssetScopes.AsNoTracking().Include(s => s.User).FirstOrDefaultAsync(s => s.Id == id);
        if (scope == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.SelectedUserName = scope.User?.FullName;
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new UserAssetScopeViewModel { Id = scope.Id, UserId = scope.UserId, ZoneId = scope.ZoneId, LocationCategoryId = scope.LocationCategoryId, CategoryId = scope.CategoryId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserAssetScopeViewModel vm)
    {
        // The picker on this shared _Form.cshtml is fully editable on Edit too (not just Create),
        // so a manager reassigning the scope to a different employee must actually have that change
        // persisted — same duplicate-scope rule as Create applies here, just excluding this scope's
        // own row.
        if (await _db.UserAssetScopes.AnyAsync(s => s.Id != vm.Id && s.UserId == vm.UserId))
            ModelState.AddModelError(nameof(vm.UserId), _loc.T("This user already has a scope assigned — edit or remove it first."));
        await ValidateUserAsync(vm.UserId);
        await ValidateScopeReferencesAsync(vm.ZoneId, vm.LocationCategoryId, vm.CategoryId);
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync();
            // Redisplay needs the picker's search box populated with a name, not just the hidden
            // UserId — same as Groups' member-chip redisplay, otherwise the box goes blank even
            // though the submitted selection is still there under the hood.
            ViewBag.SelectedUserName = (await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vm.UserId))?.FullName;
            return View(vm);
        }
        var scope = await _db.UserAssetScopes.FindAsync(vm.Id);
        if (scope == null) return NotFound();
        var oldValue = ScopeAuditValue(scope.ZoneId, scope.LocationCategoryId, scope.CategoryId);
        scope.UserId = vm.UserId; scope.ZoneId = vm.ZoneId; scope.LocationCategoryId = vm.LocationCategoryId; scope.CategoryId = vm.CategoryId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "UserAssetScope", scope.Id.ToString(), CurrentUserId,
            oldValue: oldValue, newValue: ScopeAuditValue(scope.ZoneId, scope.LocationCategoryId, scope.CategoryId), details: $"User: {scope.UserId}");
        TempData["Success"] = _loc.T("Scope updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var scope = await _db.UserAssetScopes.FindAsync(id);
        if (scope != null)
        {
            var oldValue = ScopeAuditValue(scope.ZoneId, scope.LocationCategoryId, scope.CategoryId);
            var scopeUserId = scope.UserId;
            _db.UserAssetScopes.Remove(scope);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Delete", "UserAssetScope", id.ToString(), CurrentUserId, oldValue: oldValue, details: $"User: {scopeUserId}");
            TempData["Success"] = _loc.T("Scope removed.");
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Zones = await _db.Zones.AsNoTracking().OrderBy(z => z.Name).ToListAsync();
        ViewBag.LocationCategories = await _db.LocationCategories.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.AsNoTracking().Include(c => c.Subcategories).Where(c => c.ParentCategoryId == null)
            .OrderBy(c => c.Name).ToListAsync();
    }
}
