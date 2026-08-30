using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class InspectionOrdersController : Controller
{
    private readonly IInspectionOrderService _orders;
    private readonly ITeamService _teams;
    private readonly IWorkOrderService _workOrderService;
    private readonly ApplicationDbContext _db;

    public InspectionOrdersController(IInspectionOrderService orders, ITeamService teams, IWorkOrderService workOrderService, ApplicationDbContext db)
    {
        _orders = orders;
        _teams = teams;
        _workOrderService = workOrderService;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [Authorize(Policy = PermissionCatalog.InspectionOrderView)]
    public async Task<IActionResult> Index(string? status, string? search)
    {
        var orders = await _orders.GetAllAsync(status, search);
        ViewBag.Status = status;
        ViewBag.Search = search;
        return View(orders);
    }

    // "My assigned inspection orders" now lives on the unified Users/MyOrders page (combined
    // with Maintenance Orders and Work Orders) — see UsersController.MyOrders. GetMyOrdersAsync
    // stays on the service since the AI assistant tool functions still call it directly.

    [Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
    public async Task<IActionResult> Create()
    {
        await LoadCreateViewBagAsync();
        return View(new InspectionOrderCreateVm());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
    public async Task<IActionResult> Create(InspectionOrderCreateVm vm)
    {
        if (!ModelState.IsValid)
        {
            await LoadCreateViewBagAsync(vm);
            return View(vm);
        }

        try
        {
            var order = await _orders.CreateAsync(
                vm.OrderTypeId, vm.Description,
                vm.AssigneeType == "User" ? vm.AssignedToUserId : null,
                vm.AssigneeType == "Team" ? vm.AssignedToTeamId : null,
                vm.AssetIds, vm.DueDate, CurrentUserId);

            TempData["Success"] = $"Inspection order {order.OrderNumber} created.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await LoadCreateViewBagAsync(vm);
            return View(vm);
        }
    }

    // On a failed re-render (e.g. no assets selected), rehydrate the asset chips and employee label
    // from what was actually posted so the admin doesn't lose their in-progress selection.
    private async Task LoadCreateViewBagAsync(InspectionOrderCreateVm? vm = null)
    {
        ViewBag.LocationCategories = await _db.LocationCategories.OrderBy(c => c.Id).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Teams = await _teams.GetAllAsync();
        ViewBag.OrderTypes = await _db.OrderTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();

        ViewBag.SelectedAssetChips = vm?.AssetIds is { Count: > 0 }
            ? await _db.Assets.Where(a => vm.AssetIds.Contains(a.Id))
                .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name })
                .ToListAsync()
            : new List<AssetChip>();

        ViewBag.SelectedEmployeeLabel = !string.IsNullOrEmpty(vm?.AssignedToUserId)
            ? await _db.Users.Where(u => u.Id == vm.AssignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync()
            : null;
    }

    // No policy attribute here — access is decided below by manager/assignee/team-member
    // status instead, since InspectionOrder.Report is a role-level permission that doesn't
    // account for team membership (a team can include members outside roles that normally
    // hold it, e.g. HR on a mixed team — they should still be able to open an order their
    // team is assigned).
    public async Task<IActionResult> Details(int id)
    {
        var order = await _orders.GetByIdAsync(id);
        if (order == null) return NotFound();

        var isManager = await IsManagerAsync();
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isTeamMember)
            return Forbid();

        ViewBag.MaintenanceActionTypes = await _db.MaintenanceActionTypes.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync();
        return View(order);
    }

    // Managers are anyone with InspectionOrder.Manage — checked via the policy system rather
    // than a role name so it stays in sync with whoever the RBAC matrix actually grants it to.
    private async Task<bool> IsManagerAsync()
    {
        var authz = HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        var result = await authz.AuthorizeAsync(User, PermissionCatalog.InspectionOrderManage);
        return result.Succeeded;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItem(int itemId, string outcome, int? actionTypeId, int? causeId, List<int>? maintenanceActionTypeIds)
    {
        if (!InspectionRunAsset.Outcomes.Contains(outcome) || outcome == "Pending")
            return BadRequest(new { error = "Invalid outcome." });

        var item = await _db.InspectionRunAssets.Include(i => i.InspectionRun).ThenInclude(r => r.InspectionOrder).ThenInclude(o => o.OrderType)
            .FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return NotFound();
        var order = item.InspectionRun.InspectionOrder;

        var isManager = await IsManagerAsync();
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isTeamMember)
            return Forbid();

        try
        {
            int? workOrderId = null;
            if (outcome == "Defective")
            {
                // Types that TracksDefectOutcome require Action Type + Cause; other types (e.g.
                // Quick Check) still spawn a Work Order for tracking, with both left null.
                if (order.OrderType!.TracksDefectOutcome && (actionTypeId == null || causeId == null))
                    return BadRequest(new { error = "Action Type and Cause are required to report a defect." });

                var wo = await _workOrderService.ReportAsync(new WorkOrder
                {
                    AssetId = item.AssetId,
                    ActionTypeId = order.OrderType.TracksDefectOutcome ? actionTypeId : null,
                    CauseId = order.OrderType.TracksDefectOutcome ? causeId : null,
                    RequiresVendorResponse = order.OrderType.RequiresVendor,
                }, CurrentUserId);
                workOrderId = wo.Id;
            }

            await _orders.UpdateInspectionItemAsync(itemId, outcome, workOrderId, maintenanceActionTypeIds, CurrentUserId);
            return Ok(new { outcome, workOrderId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Logs maintenance actions performed on an asset independent of its OK/Defective
    /// outcome — see UpdateMaintenanceActionsAsync. Unlike UpdateItem, this is safe to call
    /// repeatedly (including after the outcome is already recorded) since it never creates a
    /// Work Order or touches the outcome/order-completion state.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMaintenanceActions(int itemId, List<int>? maintenanceActionTypeIds)
    {
        var item = await _db.InspectionRunAssets.Include(i => i.InspectionRun).ThenInclude(r => r.InspectionOrder)
            .FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return NotFound();
        var order = item.InspectionRun.InspectionOrder;

        var isManager = await IsManagerAsync();
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isTeamMember)
            return Forbid();

        try
        {
            await _orders.UpdateMaintenanceActionsAsync(itemId, maintenanceActionTypeIds, CurrentUserId);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            await _orders.CancelAsync(id, CurrentUserId);
            TempData["Success"] = "Inspection order cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.InspectionOrderExport)]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _orders.ExportToExcelAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"InspectionOrders_{DateTime.Today:yyyyMMdd}.xlsx");
    }
}
