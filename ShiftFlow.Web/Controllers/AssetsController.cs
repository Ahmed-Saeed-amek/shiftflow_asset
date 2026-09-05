using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Services;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class AssetsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAssetService _assetService;
    private readonly IContractService _contractService;
    private readonly IAssetScopeService _scopeService;
    private readonly UserManager<ApplicationUser> _userManager;
    public AssetsController(ApplicationDbContext db, IAssetService assetService, IContractService contractService, IAssetScopeService scopeService, UserManager<ApplicationUser> userManager)
    {
        _db = db; _assetService = assetService; _contractService = contractService; _scopeService = scopeService; _userManager = userManager;
    }

    /// <summary>Applies the caller's UserAssetScope (Zone/LocationCategory/Category), if any, to
    /// an Assets queryable. Every action that reads a single asset by ID or lists/searches assets
    /// must go through this — a scoped user (e.g. a vendor or contractor account limited to one
    /// zone) must not be able to view, search, or print a code for an asset outside their scope
    /// just because they hold the blanket Asset.View permission and know/guess an ID.</summary>
    private Task<IQueryable<Asset>> ScopedAssetsAsync(string userId) =>
        _scopeService.ApplyScopeAsync(_db.Assets.AsQueryable(), userId);

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Index(string? status, int? categoryId, int? zoneId, int? locationCategoryId, string? q)
    {
        q = SearchQuery.Cap(q);
        var currentUserId = _userManager.GetUserId(User)!;
        var scope = await _db.UserAssetScopes.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == currentUserId);
        IQueryable<Asset> query = (await ScopedAssetsAsync(currentUserId))
            .Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.LocationCategory);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (categoryId.HasValue) query = query.Where(a => a.CategoryId == categoryId.Value);
        // Most-specific-wins: a Zone pick narrows further than a bare Category pick.
        if (zoneId.HasValue) query = query.Where(a => a.ZoneId == zoneId.Value);
        else if (locationCategoryId.HasValue) query = query.Where(a => a.Zone!.LocationCategoryId == locationCategoryId.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.AssetTag.Contains(q) || a.Name.Contains(q) || (a.SerialNumber != null && a.SerialNumber.Contains(q)));

        ViewBag.Categories = await _db.AssetCategories.Include(c => c.Subcategories).Where(c => c.ParentCategoryId == null)
            .OrderBy(c => c.Name).ToListAsync();
        ViewBag.Zones = await _db.Zones.Include(z => z.LocationCategory)
            .OrderBy(z => z.LocationCategory!.Name).ThenBy(z => z.Name).ToListAsync();
        ViewBag.LocationCategories = await _db.LocationCategories.OrderBy(c => c.Id).ToListAsync();
        ViewBag.Status = status; ViewBag.CategoryId = categoryId; ViewBag.ZoneId = zoneId; ViewBag.Q = q;
        ViewBag.LocationCategoryId = locationCategoryId;
        ViewBag.IsScoped = scope != null;
        return View(await query.OrderBy(a => a.AssetTag).ToListAsync());
    }

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Details(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var asset = await (await ScopedAssetsAsync(userId))
            .Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.LocationCategory)
            .Include(a => a.AssignedToUser)
            .Include(a => a.ContractLinks).ThenInclude(l => l.Contract).ThenInclude(c => c!.Vendor)
            .Include(a => a.WorkOrders).ThenInclude(w => w.Vendor)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        ViewBag.DerivedVendor = await _contractService.GetDerivedVendorAsync(id);
        ViewBag.Inspections = await _db.InspectionRunAssets
            .Include(i => i.InspectedByUser)
            .Include(i => i.MaintenanceActions).ThenInclude(m => m.MaintenanceActionType)
            .Where(i => i.AssetId == id && i.Outcome != "Pending")
            .OrderByDescending(i => i.InspectedAt)
            .ToListAsync();
        return View(asset);
    }

    /// <summary>QR code encoding a direct link to this asset's Details page — scannable with any phone camera.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> QrCode(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        if (!await (await ScopedAssetsAsync(userId)).AnyAsync(a => a.Id == id)) return NotFound();
        var url = Url.Action(nameof(Details), "Assets", new { id }, Request.Scheme)!;
        return File(AssetCodeGenerator.GenerateQrPng(url), "image/png");
    }

    /// <summary>Code128 barcode encoding the asset tag — readable by handheld maintenance scanners.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Barcode(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var asset = await (await ScopedAssetsAsync(userId)).FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        return File(AssetCodeGenerator.GenerateBarcodePng(asset.AssetTag), "image/png");
    }

    /// <summary>Printable label — asset tag, QR, and barcode — for physical tagging.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Label(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var asset = await (await ScopedAssetsAsync(userId)).Include(a => a.Category).Include(a => a.Zone).FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        return View(asset);
    }

    [Authorize(Policy = PermissionCatalog.AssetManage)]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new AssetViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); await PopulateSelectedAsync(vm.CategoryId, vm.ZoneId); return View(vm); }
        if (await _db.Assets.AnyAsync(a => a.AssetTag == vm.AssetTag))
        {
            ModelState.AddModelError(nameof(vm.AssetTag), "This Asset Tag is already in use.");
            await PopulateLookupsAsync(); await PopulateSelectedAsync(vm.CategoryId, vm.ZoneId);
            return View(vm);
        }
        var userId = _userManager.GetUserId(User)!;
        try
        {
            await _assetService.CreateAsync(new Asset
            {
                AssetTag = vm.AssetTag, Name = vm.Name, NameAr = vm.NameAr, CategoryId = vm.CategoryId, ZoneId = vm.ZoneId,
                Model = vm.Model, SerialNumber = vm.SerialNumber, Manufacturer = vm.Manufacturer, Sku = vm.Sku, Status = vm.Status,
                AssignedToUserId = vm.AssignedToUserId, PurchaseDate = vm.PurchaseDate, WarrantyExpiry = vm.WarrantyExpiry, Notes = vm.Notes,
            }, userId);
        }
        catch (DbUpdateException)
        {
            // The AnyAsync check above and this save aren't atomic — two concurrent submissions with
            // the same tag can both pass the check and race the DB's unique index on AssetTag.
            ModelState.AddModelError(nameof(vm.AssetTag), "This Asset Tag is already in use.");
            await PopulateLookupsAsync(); await PopulateSelectedAsync(vm.CategoryId, vm.ZoneId);
            return View(vm);
        }
        TempData["Success"] = "Asset created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.AssetManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var asset = await (await ScopedAssetsAsync(userId))
            .Include(a => a.Zone).ThenInclude(z => z!.LocationCategory).Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        await PopulateLookupsAsync();
        await PopulateSelectedAsync(asset.CategoryId, asset.ZoneId);
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new AssetViewModel
        {
            Id = asset.Id, AssetTag = asset.AssetTag, Name = asset.Name, NameAr = asset.NameAr, CategoryId = asset.CategoryId,
            ZoneId = asset.ZoneId, Model = asset.Model, SerialNumber = asset.SerialNumber, Manufacturer = asset.Manufacturer, Sku = asset.Sku,
            Status = asset.Status, AssignedToUserId = asset.AssignedToUserId, PurchaseDate = asset.PurchaseDate,
            WarrantyExpiry = asset.WarrantyExpiry, Notes = asset.Notes,
        });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AssetViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); await PopulateSelectedAsync(vm.CategoryId, vm.ZoneId); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
        // Unlike GET Edit, the posted vm carries no reliable "which asset is this" signal beyond
        // vm.Id itself, so the scope check here is a direct existence check against the scoped
        // queryable rather than routing the whole update through it.
        if (!await (await ScopedAssetsAsync(userId)).AnyAsync(a => a.Id == vm.Id)) return NotFound();
        await _assetService.UpdateAsync(new Asset
        {
            Id = vm.Id, Name = vm.Name, NameAr = vm.NameAr, CategoryId = vm.CategoryId, ZoneId = vm.ZoneId,
            Model = vm.Model, SerialNumber = vm.SerialNumber, Manufacturer = vm.Manufacturer, Sku = vm.Sku, Status = vm.Status,
            AssignedToUserId = vm.AssignedToUserId, PurchaseDate = vm.PurchaseDate, WarrantyExpiry = vm.WarrantyExpiry, Notes = vm.Notes,
        }, userId);
        TempData["Success"] = "Asset updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Typeahead data source for the Asset multi-picker (e.g. Contracts' Linked Assets) — capped so it never dumps the full asset list to the client regardless of how many assets exist.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Search(string? q)
    {
        q = SearchQuery.Cap(q);
        var userId = _userManager.GetUserId(User)!;
        var query = await ScopedAssetsAsync(userId);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.AssetTag.Contains(q) || a.Name.Contains(q));
        var results = await query.OrderBy(a => a.Name).Take(20)
            .Select(a => new { id = a.Id, assetTag = a.AssetTag, name = a.Name }).ToListAsync();
        return Json(results);
    }

    /// <summary>All assets under a category — if categoryId is a top-level category, this also
    /// includes every asset in its subcategories; if it's a subcategory, just its own assets.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByCategory(int categoryId)
    {
        var userId = _userManager.GetUserId(User)!;
        var categoryIds = await _db.AssetCategories
            .Where(c => c.Id == categoryId || c.ParentCategoryId == categoryId)
            .Select(c => c.Id).ToListAsync();
        var results = await (await ScopedAssetsAsync(userId)).Where(a => categoryIds.Contains(a.CategoryId))
            .OrderBy(a => a.AssetTag)
            .Select(a => new { id = a.Id, assetTag = a.AssetTag, name = a.Name }).ToListAsync();
        return Json(results);
    }

    /// <summary>All assets at or under a location scope — most-specific-wins (Zone > LocationCategory),
    /// same response shape as ByCategory, for bulk "Add All" in the asset multi-picker.
    /// An optional categoryId ANDs in the same category/subcategory expansion as ByCategory, for the
    /// picker's "match both filters" (intersection) mode.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByLocation(int? locationCategoryId, int? zoneId, int? categoryId)
    {
        var userId = _userManager.GetUserId(User)!;
        var query = await ScopedAssetsAsync(userId);
        if (zoneId.HasValue) query = query.Where(a => a.ZoneId == zoneId.Value);
        else if (locationCategoryId.HasValue) query = query.Where(a => a.Zone!.LocationCategoryId == locationCategoryId.Value);
        else if (!categoryId.HasValue) return Json(Array.Empty<object>());

        if (categoryId.HasValue)
        {
            var categoryIds = await _db.AssetCategories
                .Where(c => c.Id == categoryId.Value || c.ParentCategoryId == categoryId.Value)
                .Select(c => c.Id).ToListAsync();
            query = query.Where(a => categoryIds.Contains(a.CategoryId));
        }

        var results = await query.OrderBy(a => a.AssetTag)
            .Select(a => new { id = a.Id, assetTag = a.AssetTag, name = a.Name }).ToListAsync();
        return Json(results);
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderExport)]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _assetService.ExportToExcelAsync(_userManager.GetUserId(User)!);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Assets_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderExport)]
    public async Task<IActionResult> ExportPdf()
    {
        var bytes = await _assetService.ExportToPdfAsync(_userManager.GetUserId(User)!);
        return File(bytes, "application/pdf", $"Assets_{DateTime.Today:yyyyMMdd}.pdf");
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        ViewBag.LocationCategories = await _db.LocationCategories.OrderBy(c => c.Id).ToListAsync();
        ViewBag.Statuses = Asset.Statuses;
    }

    /// <summary>Populates the cascading Category/Subcategory and Location Category/Zone pickers'
    /// pre-selected state from a CategoryId/ZoneId — used both by GET Edit (from the saved asset)
    /// and by a failed Create/Edit POST (from the just-submitted values), so a validation error
    /// doesn't silently reset those pickers back to blank the way it used to.</summary>
    private async Task PopulateSelectedAsync(int categoryId, int zoneId)
    {
        var category = categoryId > 0 ? await _db.AssetCategories.FindAsync(categoryId) : null;
        ViewBag.SelectedParentCategoryId = category?.ParentCategoryId ?? category?.Id;
        ViewBag.SelectedSubcategoryId = category?.ParentCategoryId != null ? category.Id : (int?)null;

        var zone = zoneId > 0 ? await _db.Zones.FindAsync(zoneId) : null;
        ViewBag.SelectedLocationCategoryId = zone?.LocationCategoryId;
        ViewBag.SelectedZone = zone;
    }
}
