using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

/// <summary>Admin-managed schedules that auto-generate an Inspection or Maintenance order (per
/// OrderType.IsDirectFix) on a repeating cadence — see RecurringOrderSchedulerService, which is the
/// only thing that ever reads these rows outside this CRUD. Scoped to single-asset order types only
/// (AllowsMultipleAssets == false): a recurring schedule needs one fixed per-instance asset, which a
/// multi-asset type has no fixed list for at the type level.</summary>
[Authorize(Policy = PermissionCatalog.OrderTypeManage)]
public class RecurringOrdersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    public RecurringOrdersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db; _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> Index()
    {
        var schedules = await _db.RecurringOrders
            .Include(r => r.OrderType)
            .Include(r => r.Asset)
            .Include(r => r.AssignedToUser)
            .Include(r => r.AssignedToTeam)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
        // RequiresVendor types are excluded too — the scheduler creates a plain MaintenanceOrder/
        // InspectionOrder per OrderType.IsDirectFix, with no WorkOrder+vendor pipeline support yet.
        ViewBag.OrderTypes = await _db.OrderTypes
            .Where(t => t.IsActive && !t.AllowsMultipleAssets && !t.RequiresVendor)
            .OrderBy(t => t.SortOrder).ToListAsync();
        ViewBag.Teams = await _db.Teams.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        return View(schedules);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecurringOrderViewModel vm)
    {
        if (!(await ValidateAsync(vm))) return RedirectToAction(nameof(Index));

        _db.RecurringOrders.Add(new RecurringOrder
        {
            OrderTypeId = vm.OrderTypeId,
            AssetId = vm.AssetId,
            AssignedToUserId = string.IsNullOrEmpty(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
            AssignedToTeamId = vm.AssignedToTeamId,
            Cadence = vm.Cadence,
            StartDate = vm.StartDate.Date,
            EndDate = vm.EndDate?.Date,
            IsActive = vm.IsActive,
            CreatedByUserId = CurrentUserId,
            CreatedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Recurring order schedule created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RecurringOrderViewModel vm)
    {
        if (!(await ValidateAsync(vm))) return RedirectToAction(nameof(Index));

        var schedule = await _db.RecurringOrders.FindAsync(vm.Id);
        if (schedule == null) return NotFound();

        schedule.OrderTypeId = vm.OrderTypeId;
        schedule.AssetId = vm.AssetId;
        schedule.AssignedToUserId = string.IsNullOrEmpty(vm.AssignedToUserId) ? null : vm.AssignedToUserId;
        schedule.AssignedToTeamId = vm.AssignedToTeamId;
        schedule.Cadence = vm.Cadence;
        schedule.StartDate = vm.StartDate.Date;
        schedule.EndDate = vm.EndDate?.Date;
        schedule.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Recurring order schedule updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ValidateAsync(RecurringOrderViewModel vm)
    {
        var hasUser = !string.IsNullOrEmpty(vm.AssignedToUserId);
        var hasTeam = vm.AssignedToTeamId.HasValue;
        if (!ModelState.IsValid || hasUser == hasTeam || !RecurringOrder.Cadences.Contains(vm.Cadence))
        {
            TempData["Error"] = "Order type, asset, cadence, start date and exactly one assignee (employee or team) are required.";
            return false;
        }
        var orderType = await _db.OrderTypes.FindAsync(vm.OrderTypeId);
        if (orderType == null || orderType.AllowsMultipleAssets)
        {
            TempData["Error"] = "Select a single-asset order type.";
            return false;
        }
        if (orderType.RequiresVendor)
        {
            TempData["Error"] = "Vendor-required order types can't be scheduled yet — the recurring generator doesn't support the Work Order/vendor pipeline.";
            return false;
        }
        if (await _db.Assets.AnyAsync(a => a.Id == vm.AssetId && a.Status == "Retired"))
        {
            TempData["Error"] = "This asset is retired and can't be scheduled for new orders.";
            return false;
        }
        return true;
    }
}
