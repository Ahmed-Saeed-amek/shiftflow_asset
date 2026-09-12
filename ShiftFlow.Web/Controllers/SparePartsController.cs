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
public class SparePartsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ISparePartService _service;
    private readonly IAssetScopeService _scopeService;
    private readonly ILookupCache _lookups;
    private readonly UserManager<ApplicationUser> _userManager;
    public SparePartsController(ApplicationDbContext db, ISparePartService service, IAssetScopeService scopeService, ILookupCache lookups, UserManager<ApplicationUser> userManager)
    {
        _db = db; _service = service; _scopeService = scopeService; _lookups = lookups; _userManager = userManager;
    }

    private const int PageSize = 25;

    /// <summary>Scope rule for the parts catalog, in full: a user with no asset scope sees every
    /// part (the previous filter also hid parts with no asset links at all, contradicting its own
    /// "no scope sees everything" comment); a scoped user sees a part linked to at least one asset
    /// inside their scope, or one with no asset links — an unlinked generic consumable belongs to
    /// nobody's zone, so hiding it just makes the catalog incomplete with nothing gained.</summary>
    private async Task<IQueryable<SparePart>> ScopedPartsAsync(IQueryable<SparePart> query, string userId)
    {
        if (!await _scopeService.HasScopeAsync(userId)) return query;
        var scopedAssets = await _scopeService.GetScopedAssetsAsync(userId);
        return query.Where(p => !p.AssetLinks.Any() || p.AssetLinks.Any(l => scopedAssets.Any(a => a.Id == l.AssetId)));
    }

    [Authorize(Policy = PermissionCatalog.SparePartView)]
    public async Task<IActionResult> Index(bool? lowStockOnly, string? q, int page = 1)
    {
        if (page < 1) page = 1;
        q = SearchQuery.Cap(q);
        var userId = _userManager.GetUserId(User)!;
        var query = await ScopedPartsAsync(_db.SpareParts.AsNoTracking(), userId);
        if (lowStockOnly == true)
            query = query.Where(p => p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => p.Name.Contains(q) || (p.Sku != null && p.Sku.Contains(q)));

        var ordered = query.OrderBy(p => p.Name);
        var totalCount = await ordered.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages) page = totalPages;

        var parts = await ordered.Skip((page - 1) * PageSize).Take(PageSize)
            .Select(p => new SparePartRow
            {
                Id = p.Id, Name = p.Name, NameAr = p.NameAr, Sku = p.Sku, UnitCost = p.UnitCost,
                StockQuantity = p.StockQuantity, ReorderThreshold = p.ReorderThreshold, IsActive = p.IsActive,
                LinkedAssetCount = p.AssetLinks.Count,
            })
            .ToListAsync();

        return View(new SparePartIndexViewModel
        {
            Parts = parts, LowStockOnly = lowStockOnly == true, Q = q, TotalCount = totalCount,
            Pagination = new PaginationModel { Page = page, TotalPages = totalPages },
        });
    }

    [Authorize(Policy = PermissionCatalog.SparePartView)]
    public async Task<IActionResult> Details(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var part = await (await ScopedPartsAsync(_db.SpareParts.AsNoTracking(), userId))
            .FirstOrDefaultAsync(p => p.Id == id);
        if (part == null) return NotFound();
        // Only the links the caller may see — the raw AssetLinks collection names every asset the
        // part fits, in or out of scope.
        var linkedAssets = await InScopeAssetChipsAsync(id, userId);

        // Recent usage across both fix-report entities, most recent first — lets whoever manages
        // the catalog see at a glance what this part has actually been consumed by.
        var woUsage = await _db.WorkOrderParts.AsNoTracking()
            .Where(p => p.SparePartId == id)
            .Select(p => new SparePartUsageRow
            {
                WorkOrderNumber = p.WorkOrder!.WorkOrderNumber, Quantity = p.Quantity,
                UsedDate = p.WorkOrder.ClosedDate ?? p.WorkOrder.CreatedDate,
            }).ToListAsync();
        var moUsage = await _db.MaintenanceOrderParts.AsNoTracking()
            .Where(p => p.SparePartId == id)
            .Select(p => new SparePartUsageRow
            {
                WorkOrderNumber = p.MaintenanceOrder!.OrderNumber, Quantity = p.Quantity,
                UsedDate = p.MaintenanceOrder.ClosedDate ?? p.MaintenanceOrder.CreatedDate,
            }).ToListAsync();
        return View(new SparePartDetailsViewModel
        {
            Part = part,
            LinkedAssets = linkedAssets,
            RecentUsage = woUsage.Concat(moUsage).OrderByDescending(u => u.UsedDate).Take(20).ToList(),
        });
    }

    [Authorize(Policy = PermissionCatalog.SparePartManage)]
    public async Task<IActionResult> Create()
    {
        ViewBag.SelectedAssetChips = new List<AssetChip>();
        await PopulateLookupsAsync();
        return View(new SparePartViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.SparePartManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SparePartViewModel vm)
    {
        if (!ModelState.IsValid) { ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds); await PopulateLookupsAsync(); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
        try
        {
            var part = await _service.CreateAsync(new SparePart
            {
                Name = vm.Name, NameAr = vm.NameAr, Sku = vm.Sku, UnitCost = vm.UnitCost,
                StockQuantity = vm.StockQuantity, ReorderThreshold = vm.ReorderThreshold, IsActive = vm.IsActive,
            }, vm.AssetIds ?? [], userId);
            TempData["Success"] = "Spare part created.";
            return RedirectToAction(nameof(Details), new { id = part.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds);
            await PopulateLookupsAsync();
            return View(vm);
        }
    }

    [Authorize(Policy = PermissionCatalog.SparePartManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        // Edit was reachable for an out-of-scope part even though Details 404s on it, and its chip
        // list named every linked asset regardless of scope.
        var part = await (await ScopedPartsAsync(_db.SpareParts.AsNoTracking(), userId))
            .Include(p => p.AssetLinks)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (part == null) return NotFound();
        ViewBag.SelectedAssetChips = await InScopeAssetChipsAsync(id, userId);
        await PopulateLookupsAsync();
        return View(new SparePartViewModel
        {
            Id = part.Id, Name = part.Name, NameAr = part.NameAr, Sku = part.Sku, UnitCost = part.UnitCost,
            StockQuantity = part.StockQuantity, ReorderThreshold = part.ReorderThreshold, IsActive = part.IsActive,
            AssetIds = part.AssetLinks.Select(l => l.AssetId).ToList(),
        });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.SparePartManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SparePartViewModel vm)
    {
        if (!ModelState.IsValid) { ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds); await PopulateLookupsAsync(); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
        if (!await (await ScopedPartsAsync(_db.SpareParts.AsNoTracking(), userId)).AnyAsync(p => p.Id == vm.Id)) return NotFound();
        try
        {
            await _service.UpdateAsync(new SparePart
            {
                Id = vm.Id, Name = vm.Name, NameAr = vm.NameAr, Sku = vm.Sku, UnitCost = vm.UnitCost,
                ReorderThreshold = vm.ReorderThreshold, IsActive = vm.IsActive,
            }, vm.AssetIds ?? [], userId);
            TempData["Success"] = "Spare part updated.";
            return RedirectToAction(nameof(Details), new { id = vm.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds);
            await PopulateLookupsAsync();
            return View(vm);
        }
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.SparePartManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustStock(int id, int newQuantity, string? reason)
    {
        var userId = _userManager.GetUserId(User)!;
        if (!await (await ScopedPartsAsync(_db.SpareParts.AsNoTracking(), userId)).AnyAsync(p => p.Id == id)) return NotFound();
        try { await _service.AdjustStockAsync(id, newQuantity, reason, userId); TempData["Success"] = "Stock updated."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Backs every fix-report parts picker (Work Orders, Vendor Portal, Maintenance Orders).
    /// Deliberately just [Authorize], not gated by SparePart.View — vendors need this to submit a
    /// fix but don't get the full permission or see the catalog nav item.</summary>
    [HttpGet, Authorize]
    public async Task<IActionResult> CompatibleForAsset(int assetId) =>
        Json((await _service.GetCompatiblePartsAsync(assetId))
            .Select(p => new { id = p.Id, name = p.Name }));

    /// <summary>The assets a part is linked to, narrowed to the caller's own asset scope.</summary>
    private async Task<List<AssetChip>> InScopeAssetChipsAsync(int sparePartId, string userId) =>
        await (await _scopeService.GetScopedAssetsAsync(userId))
            .Where(a => a.SparePartLinks.Any(l => l.SparePartId == sparePartId))
            .OrderBy(a => a.AssetTag)
            .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name })
            .ToListAsync();

    private async Task<List<AssetChip>> BuildChipsAsync(List<int>? assetIds)
    {
        if (assetIds == null || assetIds.Count == 0) return [];
        var userId = _userManager.GetUserId(User)!;
        return await (await _scopeService.GetScopedAssetsAsync(userId)).Where(a => assetIds.Contains(a.Id))
            .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name }).ToListAsync();
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Categories = await _lookups.TopLevelCategoriesAsync();
    }
}
