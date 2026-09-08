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
    private readonly IGroupService _groups;
    private readonly IWorkOrderService _workOrderService;
    private readonly ApplicationDbContext _db;
    private readonly IAssetScopeService _scope;

    public InspectionOrdersController(IInspectionOrderService orders, IGroupService groups, IWorkOrderService workOrderService, ApplicationDbContext db, IAssetScopeService scope)
    {
        _orders = orders;
        _groups = groups;
        _workOrderService = workOrderService;
        _db = db;
        _scope = scope;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [Authorize(Policy = PermissionCatalog.InspectionOrderView)]
    public async Task<IActionResult> Index(string? status, string? search, bool overdue = false)
    {
        var orders = await _orders.GetAllAsync(status, search, overdue, CurrentUserId);
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.Overdue = overdue;
        return View(orders);
    }

    // "My assigned inspection orders" now lives on the unified Users/MyOrders page (combined
    // with Maintenance Orders and Work Orders) — see UsersController.MyOrders. GetMyOrdersAsync
    // stays on the service since the AI assistant tool functions still call it directly.

    // Create moved to the unified OrdersController (Orders/Create) — see that controller.

    // No policy attribute here — access is decided below by manager/assignee/group-member
    // status instead, since InspectionOrder.Report is a role-level permission that doesn't
    // account for group membership (a group can include members outside roles that normally
    // hold it, e.g. HR on a mixed group — they should still be able to open an order their
    // group is assigned).
    public async Task<IActionResult> Details(int id)
    {
        var order = await _orders.GetByIdAsync(id);
        if (order == null) return NotFound();

        var isManager = await IsManagerAsync();
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isGroupMember = order.AssignedToGroupId.HasValue && await _groups.IsMemberAsync(order.AssignedToGroupId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isGroupMember)
            return Forbid();

        // UserAssetScope restricts which assets a user can see even when they'd otherwise have
        // access via role/assignment — AssetsController enforces this uniformly for every viewer,
        // with no manager exception, so this must too or a scoped manager can view an out-of-scope
        // asset's full inspection history just by knowing an order ID. Exempted for the order's own
        // assignee/group member — a scope narrowed/added after assignment must not lock them out of
        // viewing (and reporting on) their own already-assigned work.
        if (!isAssignee && !isGroupMember)
        {
            var assetIds = order.InspectionRun?.Items.Select(i => i.AssetId).Distinct().ToList() ?? [];
            if (assetIds.Count > 0)
            {
                var inScopeCount = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), CurrentUserId)).CountAsync(a => assetIds.Contains(a.Id));
                if (inScopeCount != assetIds.Count) return NotFound();
            }
        }

        if (isManager) ViewBag.Groups = await _groups.GetAllAsync();
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
    public async Task<IActionResult> UpdateItem(int itemId, string outcome, int? actionTypeId, int? causeId)
    {
        if (!InspectionRunAsset.Outcomes.Contains(outcome) || outcome == "Pending")
            return BadRequest(new { error = "Invalid outcome." });

        var item = await _db.InspectionRunAssets.Include(i => i.InspectionRun).ThenInclude(r => r.InspectionOrder).ThenInclude(o => o.OrderType)
            .FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return NotFound();
        var order = item.InspectionRun.InspectionOrder;

        var isManager = await IsManagerAsync();
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isGroupMember = order.AssignedToGroupId.HasValue && await _groups.IsMemberAsync(order.AssignedToGroupId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isGroupMember)
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

            await _orders.UpdateInspectionItemAsync(itemId, outcome, workOrderId, CurrentUserId);
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
        var isGroupMember = order.AssignedToGroupId.HasValue && await _groups.IsMemberAsync(order.AssignedToGroupId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isGroupMember)
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
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            await _orders.ApproveAsync(id, CurrentUserId);
            TempData["Success"] = "Inspection order approved and closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        try
        {
            await _orders.CancelAsync(id, reason, CurrentUserId);
            TempData["Success"] = "Inspection order cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Manager-only recovery path for an order whose sole assignee has since been
    /// deactivated (UpdateItem has no manager override for the assignee/group-member gate) — moves
    /// the order to a different employee or Group instead of leaving it permanently un-actionable.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
    public async Task<IActionResult> Reassign(int id, string? assignedToUserId, int? assignedToGroupId)
    {
        try
        {
            await _orders.ReassignAsync(id, assignedToUserId, assignedToGroupId, CurrentUserId);
            TempData["Success"] = "Inspection order reassigned.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = PermissionCatalog.InspectionOrderExport)]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _orders.ExportToExcelAsync(CurrentUserId);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"InspectionOrders_{DateTime.Today:yyyyMMdd}.xlsx");
    }
}
