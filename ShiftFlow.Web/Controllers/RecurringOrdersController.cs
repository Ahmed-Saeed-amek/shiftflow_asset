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

/// <summary>Admin-managed schedules that auto-generate an Inspection order, Maintenance order (per
/// OrderType.IsDirectFix), or — for a RequiresVendor type — a vendor-routed Work Order, per linked
/// asset, on a repeating cadence. See RecurringOrderSchedulerService, which is the only thing that
/// ever reads these rows outside this CRUD. Any active Order Type can be scheduled; asset cardinality
/// mirrors OrdersController.Create exactly — an AllowsMultipleAssets type (Inspection, Quick Check) can
/// cover several assets via AssetLinks, while a single-asset type (Maintenance, tied to spare-part
/// usage per asset) is still limited to exactly one, resolved server-side from the OrderType's own
/// flag rather than trusting whichever picker the client posted.</summary>
[Authorize(Policy = PermissionCatalog.OrderTypeManage)]
public class RecurringOrdersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IRecurringOrderService _recurringOrders;
    private readonly UserManager<ApplicationUser> _userManager;
    public RecurringOrdersController(ApplicationDbContext db, IRecurringOrderService recurringOrders, UserManager<ApplicationUser> userManager)
    {
        _db = db; _recurringOrders = recurringOrders; _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> Index()
    {
        var schedules = await _db.RecurringOrders
            .Include(r => r.OrderType)
            .Include(r => r.AssetLinks)
            .Include(r => r.AssignedToUser)
            .Include(r => r.AssignedToGroup)
            .Include(r => r.Vendor)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
        return View(schedules);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        ViewBag.SelectedAssetChips = new List<AssetChip>();
        return View(new RecurringOrderViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecurringOrderViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(vm); ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds); return View(vm); }
        try
        {
            var assetIds = await ResolveAssetIdsAsync(vm);
            await _recurringOrders.CreateAsync(new RecurringOrder
            {
                OrderTypeId = vm.OrderTypeId,
                AssignedToUserId = string.IsNullOrEmpty(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
                AssignedToGroupId = vm.AssignedToGroupId,
                VendorId = vm.VendorId,
                Cadence = vm.Cadence,
                StartDate = vm.StartDate.Date,
                EndDate = vm.EndDate?.Date,
                IsActive = vm.IsActive,
            }, assetIds, CurrentUserId);
            TempData["Success"] = "Recurring order schedule created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync(vm);
            ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds);
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var schedule = await _db.RecurringOrders.Include(r => r.AssetLinks).ThenInclude(l => l.Asset).FirstOrDefaultAsync(r => r.Id == id);
        if (schedule == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.SelectedEmployeeLabel = !string.IsNullOrEmpty(schedule.AssignedToUserId)
            ? await _db.Users.Where(u => u.Id == schedule.AssignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync()
            : null;
        var linkedAssetIds = schedule.AssetLinks.Select(l => l.AssetId).ToList();
        ViewBag.SelectedAssetChips = schedule.AssetLinks
            .Select(l => new AssetChip { Id = l.AssetId, Label = $"{l.Asset!.AssetTag} — {l.Asset.Name}" }).ToList();
        // The single-asset picker needs its own prefill too — used whichever way the OrderType
        // currently allows multiple assets or not (a type can be flipped after a schedule exists for
        // an AllowsMultipleAssets-false type only, per OrderTypesController's in-use guard, so this
        // is always exactly one asset when it applies).
        var firstAsset = schedule.AssetLinks.Select(l => l.Asset).FirstOrDefault();
        ViewBag.SelectedAssetLabel = firstAsset != null ? $"{firstAsset.AssetTag} — {firstAsset.Name}" : null;
        return View(new RecurringOrderViewModel
        {
            Id = schedule.Id,
            OrderTypeId = schedule.OrderTypeId,
            AssetId = linkedAssetIds.FirstOrDefault(),
            AssetIds = linkedAssetIds,
            OriginalAssetIds = linkedAssetIds,
            AssignedToUserId = schedule.AssignedToUserId,
            AssignedToGroupId = schedule.AssignedToGroupId,
            VendorId = schedule.VendorId,
            Cadence = schedule.Cadence,
            StartDate = schedule.StartDate,
            EndDate = schedule.EndDate,
            IsActive = schedule.IsActive,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RecurringOrderViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(vm); ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds); return View(vm); }
        try
        {
            var assetIds = await ResolveAssetIdsAsync(vm);
            await _recurringOrders.UpdateAsync(new RecurringOrder
            {
                Id = vm.Id,
                OrderTypeId = vm.OrderTypeId,
                AssignedToUserId = string.IsNullOrEmpty(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
                AssignedToGroupId = vm.AssignedToGroupId,
                VendorId = vm.VendorId,
                Cadence = vm.Cadence,
                StartDate = vm.StartDate.Date,
                EndDate = vm.EndDate?.Date,
                IsActive = vm.IsActive,
            }, assetIds, vm.OriginalAssetIds ?? [], CurrentUserId);
            TempData["Success"] = "Recurring order schedule updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync(vm);
            ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds);
            return View(vm);
        }
    }

    // Same resolution OrdersController.Create does: cardinality is a property of the selected
    // OrderType, not whichever picker the client happened to have visible — re-derived server-side
    // so a tampered POST can't submit a multi-asset list against a single-asset type or vice versa.
    private async Task<List<int>> ResolveAssetIdsAsync(RecurringOrderViewModel vm)
    {
        var allowsMultipleAssets = await _db.OrderTypes.Where(t => t.Id == vm.OrderTypeId)
            .Select(t => (bool?)t.AllowsMultipleAssets).FirstOrDefaultAsync();
        return allowsMultipleAssets == true
            ? (vm.AssetIds ?? []).Distinct().ToList()
            : (vm.AssetId > 0 ? [vm.AssetId] : []);
    }

    private async Task<List<AssetChip>> BuildChipsAsync(List<int>? assetIds)
    {
        if (assetIds == null || assetIds.Count == 0) return [];
        return await _db.Assets.Where(a => assetIds.Contains(a.Id))
            .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name }).ToListAsync();
    }

    private async Task PopulateLookupsAsync(RecurringOrderViewModel? vm = null)
    {
        ViewBag.OrderTypes = await _db.OrderTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();
        ViewBag.Groups = await _db.Groups.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        ViewBag.Vendors = await _db.Vendors.Where(v => v.Status == "Active").OrderBy(v => v.Name).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        // requiresVendor here means "this schedule is vendor-routed", which mirrors Orders/Create's
        // own rule: only a direct-fix type that ALSO has RequiresVendor asks for a vendor at
        // creation. A survey-style (Inspection/Quick Check) type's RequiresVendor flag governs a
        // different, later flow (a reported Defective outcome spawning its own Work Order) — it
        // should never make this schedule form ask for a vendor up front.
        ViewBag.OrderTypeMetaJson = System.Text.Json.JsonSerializer.Serialize(
            ((List<OrderType>)ViewBag.OrderTypes).ToDictionary(t => t.Id, t => new { RequiresVendor = t.IsDirectFix && t.RequiresVendor, t.AllowsMultipleAssets, t.AssignmentMode }),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        // Redisplay after a failed POST needs the picker's search box populated with a name, not
        // just the hidden AssignedToUserId, otherwise the box goes blank even though the submitted
        // selection is still there under the hood — same as Groups'/UserAssetScopes' redisplay fix.
        string? assignedToUserId = vm?.AssignedToUserId;
        if (!string.IsNullOrEmpty(assignedToUserId) && ViewBag.SelectedEmployeeLabel == null)
        {
            ViewBag.SelectedEmployeeLabel = await _db.Users.Where(u => u.Id == assignedToUserId)
                .Select(u => u.FullName).FirstOrDefaultAsync();
        }
        int assetId = vm?.AssetId ?? 0;
        if (assetId > 0 && ViewBag.SelectedAssetLabel == null)
        {
            var asset = await _db.Assets.FindAsync(assetId);
            if (asset != null) ViewBag.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
        }
    }
}
