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
    private readonly UserManager<ApplicationUser> _userManager;
    public AssetsController(ApplicationDbContext db, IAssetService assetService, IContractService contractService, UserManager<ApplicationUser> userManager)
    {
        _db = db; _assetService = assetService; _contractService = contractService; _userManager = userManager;
    }

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Index(string? status, int? categoryId, int? zoneId, int? governorateId, int? areaId, int? blockId, string? q)
    {
        var query = _db.Assets.Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.Block).ThenInclude(bl => bl!.Area).ThenInclude(a => a!.Governorate).AsQueryable();

        var currentUserId = _userManager.GetUserId(User)!;
        var scope = await _db.UserAssetScopes.FirstOrDefaultAsync(s => s.UserId == currentUserId);
        if (scope != null)
        {
            query = scope.ScopeType switch
            {
                "Zone" => query.Where(a => a.ZoneId == scope.ScopeValueId),
                "Area" => query.Where(a => a.Zone!.Block!.AreaId == scope.ScopeValueId),
                "Category" => query.Where(a => a.CategoryId == scope.ScopeValueId || a.Category!.ParentCategoryId == scope.ScopeValueId),
                _ => query,
            };
        }

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (categoryId.HasValue) query = query.Where(a => a.CategoryId == categoryId.Value);
        // Most-specific-wins: a Zone pick narrows further than Block, which narrows further than Area/Governorate.
        if (zoneId.HasValue) query = query.Where(a => a.ZoneId == zoneId.Value);
        else if (blockId.HasValue) query = query.Where(a => a.Zone!.BlockId == blockId.Value);
        else if (areaId.HasValue) query = query.Where(a => a.Zone!.Block!.AreaId == areaId.Value);
        else if (governorateId.HasValue) query = query.Where(a => a.Zone!.Block!.Area!.GovernorateId == governorateId.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.AssetTag.Contains(q) || a.Name.Contains(q) || (a.SerialNumber != null && a.SerialNumber.Contains(q)));

        ViewBag.Categories = await _db.AssetCategories.Include(c => c.ParentCategory).OrderBy(c => c.ParentCategoryId == null ? c.Name : c.ParentCategory!.Name).ThenBy(c => c.Name).ToListAsync();
        ViewBag.Zones = await _db.Zones.Include(z => z.Block!).ThenInclude(bl => bl.Area!).ThenInclude(a => a.Governorate)
            .OrderBy(z => z.Block!.Area!.Governorate!.Name).ThenBy(z => z.Block!.Area!.Name).ThenBy(z => z.Name).ToListAsync();
        ViewBag.Governorates = await _db.Governorates.OrderBy(g => g.Name).ToListAsync();
        if (governorateId.HasValue)
            ViewBag.Areas = await _db.Areas.Where(a => a.GovernorateId == governorateId.Value).OrderBy(a => a.Name).ToListAsync();
        if (areaId.HasValue)
            ViewBag.Blocks = await _db.Blocks.Where(b => b.AreaId == areaId.Value).OrderBy(b => b.Number).ToListAsync();
        ViewBag.Status = status; ViewBag.CategoryId = categoryId; ViewBag.ZoneId = zoneId; ViewBag.Q = q;
        ViewBag.GovernorateId = governorateId; ViewBag.AreaId = areaId; ViewBag.BlockId = blockId;
        ViewBag.IsScoped = scope != null;
        return View(await query.OrderBy(a => a.AssetTag).ToListAsync());
    }

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Details(int id)
    {
        var asset = await _db.Assets
            .Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.Block).ThenInclude(bl => bl!.Area).ThenInclude(a => a!.Governorate)
            .Include(a => a.AssignedToUser)
            .Include(a => a.ContractLinks).ThenInclude(l => l.Contract).ThenInclude(c => c!.Vendor)
            .Include(a => a.WorkOrders).ThenInclude(w => w.Vendor)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        ViewBag.DerivedVendor = await _contractService.GetDerivedVendorAsync(id);
        ViewBag.Inspections = await _db.InspectionRunAssets
            .Include(i => i.InspectedByUser)
            .Where(i => i.AssetId == id && i.Outcome != "Pending")
            .OrderByDescending(i => i.InspectedAt)
            .ToListAsync();
        return View(asset);
    }

    /// <summary>QR code encoding a direct link to this asset's Details page — scannable with any phone camera.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> QrCode(int id)
    {
        if (!await _db.Assets.AnyAsync(a => a.Id == id)) return NotFound();
        var url = Url.Action(nameof(Details), "Assets", new { id }, Request.Scheme)!;
        return File(AssetCodeGenerator.GenerateQrPng(url), "image/png");
    }

    /// <summary>Code128 barcode encoding the asset tag — readable by handheld maintenance scanners.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Barcode(int id)
    {
        var asset = await _db.Assets.FindAsync(id);
        if (asset == null) return NotFound();
        return File(AssetCodeGenerator.GenerateBarcodePng(asset.AssetTag), "image/png");
    }

    /// <summary>Printable label — asset tag, QR, and barcode — for physical tagging.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Label(int id)
    {
        var asset = await _db.Assets.Include(a => a.Category).Include(a => a.Zone).FirstOrDefaultAsync(a => a.Id == id);
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
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
        await _assetService.CreateAsync(new Asset
        {
            AssetTag = vm.AssetTag, Name = vm.Name, NameAr = vm.NameAr, CategoryId = vm.CategoryId, ZoneId = vm.ZoneId,
            Model = vm.Model, SerialNumber = vm.SerialNumber, Manufacturer = vm.Manufacturer, Sku = vm.Sku, Status = vm.Status,
            AssignedToUserId = vm.AssignedToUserId, PurchaseDate = vm.PurchaseDate, WarrantyExpiry = vm.WarrantyExpiry, Notes = vm.Notes,
        }, userId);
        TempData["Success"] = "Asset created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.AssetManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var asset = await _db.Assets.Include(a => a.Zone).ThenInclude(z => z!.Block).ThenInclude(bl => bl!.Area).Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.SelectedGovernorateId = asset.Zone?.Block?.Area?.GovernorateId;
        ViewBag.SelectedAreaId = asset.Zone?.Block?.AreaId;
        ViewBag.SelectedBlockId = asset.Zone?.BlockId;
        ViewBag.SelectedParentCategoryId = asset.Category?.ParentCategoryId ?? asset.CategoryId;
        ViewBag.SelectedSubcategoryId = asset.Category?.ParentCategoryId != null ? asset.CategoryId : (int?)null;
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
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
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
        var query = _db.Assets.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.AssetTag.Contains(q) || a.Name.Contains(q));
        var results = await query.OrderBy(a => a.Name).Take(20)
            .Select(a => new { id = a.Id, assetTag = a.AssetTag, name = a.Name }).ToListAsync();
        return Json(results);
    }

    /// <summary>All assets under a category — if categoryId is a top-level category, this also
    /// includes every asset in its subcategories; if it's a subcategory, just its own assets.</summary>
    public async Task<IActionResult> ByCategory(int categoryId)
    {
        var categoryIds = await _db.AssetCategories
            .Where(c => c.Id == categoryId || c.ParentCategoryId == categoryId)
            .Select(c => c.Id).ToListAsync();
        var results = await _db.Assets.Where(a => categoryIds.Contains(a.CategoryId))
            .OrderBy(a => a.AssetTag)
            .Select(a => new { id = a.Id, assetTag = a.AssetTag, name = a.Name }).ToListAsync();
        return Json(results);
    }

    /// <summary>All assets at or under a location scope — most-specific-wins (Zone > Block > Area >
    /// Governorate), same response shape as ByCategory, for bulk "Add All" in the asset multi-picker.
    /// An optional categoryId ANDs in the same category/subcategory expansion as ByCategory, for the
    /// picker's "match both filters" (intersection) mode.</summary>
    public async Task<IActionResult> ByLocation(int? governorateId, int? areaId, int? blockId, int? zoneId, int? categoryId)
    {
        var query = _db.Assets.AsQueryable();
        if (zoneId.HasValue) query = query.Where(a => a.ZoneId == zoneId.Value);
        else if (blockId.HasValue) query = query.Where(a => a.Zone!.BlockId == blockId.Value);
        else if (areaId.HasValue) query = query.Where(a => a.Zone!.Block!.AreaId == areaId.Value);
        else if (governorateId.HasValue) query = query.Where(a => a.Zone!.Block!.Area!.GovernorateId == governorateId.Value);
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
        var bytes = await _assetService.ExportToExcelAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Assets_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderExport)]
    public async Task<IActionResult> ExportPdf()
    {
        var bytes = await _assetService.ExportToPdfAsync();
        return File(bytes, "application/pdf", $"Assets_{DateTime.Today:yyyyMMdd}.pdf");
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Governorates = await _db.Governorates.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Statuses = Asset.Statuses;
    }
}
