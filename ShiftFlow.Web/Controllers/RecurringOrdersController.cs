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
/// ever reads these rows outside this CRUD. Any active Order Type can be scheduled — cardinality is
/// handled by the schedule's own AssetLinks (mirroring Contract/ContractAsset), not the OrderType's
/// AllowsMultipleAssets flag: one order is generated per linked asset per occurrence, the same "one
/// row per asset" shape PreventiveMaintenanceSchedulerService already uses for vendor PM.</summary>
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
            .Include(r => r.AssignedToTeam)
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
            await _recurringOrders.CreateAsync(new RecurringOrder
            {
                OrderTypeId = vm.OrderTypeId,
                AssignedToUserId = string.IsNullOrEmpty(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
                AssignedToTeamId = vm.AssignedToTeamId,
                VendorId = vm.VendorId,
                Cadence = vm.Cadence,
                StartDate = vm.StartDate.Date,
                EndDate = vm.EndDate?.Date,
                IsActive = vm.IsActive,
            }, vm.AssetIds ?? [], CurrentUserId);
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
        ViewBag.SelectedAssetChips = schedule.AssetLinks
            .Select(l => new AssetChip { Id = l.AssetId, Label = $"{l.Asset!.AssetTag} — {l.Asset.Name}" }).ToList();
        var linkedAssetIds = schedule.AssetLinks.Select(l => l.AssetId).ToList();
        return View(new RecurringOrderViewModel
        {
            Id = schedule.Id,
            OrderTypeId = schedule.OrderTypeId,
            AssetIds = linkedAssetIds,
            OriginalAssetIds = linkedAssetIds,
            AssignedToUserId = schedule.AssignedToUserId,
            AssignedToTeamId = schedule.AssignedToTeamId,
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
            await _recurringOrders.UpdateAsync(new RecurringOrder
            {
                Id = vm.Id,
                OrderTypeId = vm.OrderTypeId,
                AssignedToUserId = string.IsNullOrEmpty(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
                AssignedToTeamId = vm.AssignedToTeamId,
                VendorId = vm.VendorId,
                Cadence = vm.Cadence,
                StartDate = vm.StartDate.Date,
                EndDate = vm.EndDate?.Date,
                IsActive = vm.IsActive,
            }, vm.AssetIds ?? [], vm.OriginalAssetIds ?? [], CurrentUserId);
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

    private async Task<List<AssetChip>> BuildChipsAsync(List<int>? assetIds)
    {
        if (assetIds == null || assetIds.Count == 0) return [];
        return await _db.Assets.Where(a => assetIds.Contains(a.Id))
            .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name }).ToListAsync();
    }

    private async Task PopulateLookupsAsync(RecurringOrderViewModel? vm = null)
    {
        ViewBag.OrderTypes = await _db.OrderTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();
        ViewBag.Teams = await _db.Teams.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        ViewBag.Vendors = await _db.Vendors.Where(v => v.Status == "Active").OrderBy(v => v.Name).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        // Redisplay after a failed POST needs the picker's search box populated with a name, not
        // just the hidden AssignedToUserId, otherwise the box goes blank even though the submitted
        // selection is still there under the hood — same as Teams'/UserAssetScopes' redisplay fix.
        string? assignedToUserId = vm?.AssignedToUserId;
        if (!string.IsNullOrEmpty(assignedToUserId) && ViewBag.SelectedEmployeeLabel == null)
        {
            ViewBag.SelectedEmployeeLabel = await _db.Users.Where(u => u.Id == assignedToUserId)
                .Select(u => u.FullName).FirstOrDefaultAsync();
        }
    }
}
