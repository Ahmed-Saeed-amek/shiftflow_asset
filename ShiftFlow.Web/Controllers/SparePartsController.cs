using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class SparePartsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ISparePartService _service;
    private readonly UserManager<ApplicationUser> _userManager;
    public SparePartsController(ApplicationDbContext db, ISparePartService service, UserManager<ApplicationUser> userManager)
    {
        _db = db; _service = service; _userManager = userManager;
    }

    [Authorize(Policy = PermissionCatalog.SparePartView)]
    public async Task<IActionResult> Index(bool? lowStockOnly)
    {
        var query = _db.SpareParts.AsQueryable();
        if (lowStockOnly == true)
            query = query.Where(p => p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold);
        var parts = await query.OrderBy(p => p.Name).ToListAsync();
        ViewBag.LowStockOnly = lowStockOnly == true;
        return View(parts);
    }

    [Authorize(Policy = PermissionCatalog.SparePartView)]
    public async Task<IActionResult> Details(int id)
    {
        var part = await _db.SpareParts.Include(p => p.AssetLinks).ThenInclude(l => l.Asset)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (part == null) return NotFound();

        // Recent usage across both fix-report entities, most recent first — lets whoever manages
        // the catalog see at a glance what this part has actually been consumed by.
        var woUsage = await _db.WorkOrderParts.Include(p => p.WorkOrder)
            .Where(p => p.SparePartId == id)
            .Select(p => new SparePartUsageRow
            {
                WorkOrderNumber = p.WorkOrder!.WorkOrderNumber, Quantity = p.Quantity,
                UsedDate = p.WorkOrder.ClosedDate ?? p.WorkOrder.CreatedDate,
            }).ToListAsync();
        var moUsage = await _db.MaintenanceOrderParts.Include(p => p.MaintenanceOrder)
            .Where(p => p.SparePartId == id)
            .Select(p => new SparePartUsageRow
            {
                WorkOrderNumber = p.MaintenanceOrder!.OrderNumber, Quantity = p.Quantity,
                UsedDate = p.MaintenanceOrder.ClosedDate ?? p.MaintenanceOrder.CreatedDate,
            }).ToListAsync();
        ViewBag.RecentUsage = woUsage.Concat(moUsage).OrderByDescending(u => u.UsedDate).Take(20).ToList();

        return View(part);
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
        var part = await _db.SpareParts.Include(p => p.AssetLinks).ThenInclude(l => l.Asset).FirstOrDefaultAsync(p => p.Id == id);
        if (part == null) return NotFound();
        ViewBag.SelectedAssetChips = part.AssetLinks
            .Select(l => new AssetChip { Id = l.AssetId, Label = l.Asset!.AssetTag + " — " + l.Asset.Name }).ToList();
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

    private async Task<List<AssetChip>> BuildChipsAsync(List<int>? assetIds)
    {
        if (assetIds == null || assetIds.Count == 0) return [];
        return await _db.Assets.Where(a => assetIds.Contains(a.Id))
            .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name }).ToListAsync();
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
    }
}
