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

    [Authorize]
    public async Task<IActionResult> MyOrders(bool showAll = false)
    {
        var orders = await _orders.GetMyOrdersAsync(CurrentUserId, includeDone: showAll);
        ViewBag.ShowAll = showAll;
        return View(orders);
    }

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
            await LoadCreateViewBagAsync();
            return View(vm);
        }

        try
        {
            var order = await _orders.CreateAsync(
                vm.Title, vm.Description,
                vm.AssigneeType == "User" ? vm.AssignedToUserId : null,
                vm.AssigneeType == "Team" ? vm.AssignedToTeamId : null,
                vm.ZoneId, vm.AssetIds, vm.DueDate, CurrentUserId);

            TempData["Success"] = $"Inspection order {order.OrderNumber} created.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await LoadCreateViewBagAsync();
            return View(vm);
        }
    }

    private async Task LoadCreateViewBagAsync()
    {
        ViewBag.Zones = await _db.Zones.OrderBy(z => z.Name).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Teams = await _teams.GetAllAsync();
    }

    [Authorize(Policy = PermissionCatalog.InspectionOrderReport)]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _orders.GetByIdAsync(id);
        if (order == null) return NotFound();

        var isManager = await IsManagerAsync();
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isTeamMember)
            return Forbid();

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

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.InspectionOrderReport)]
    public async Task<IActionResult> UpdateItem(int itemId, string outcome, string? notes, int? actionTypeId, int? causeId)
    {
        if (!InspectionRunAsset.Outcomes.Contains(outcome) || outcome == "Pending")
            return BadRequest(new { error = "Invalid outcome." });

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
            int? workOrderId = null;
            if (outcome == "Defective")
            {
                if (actionTypeId == null || causeId == null)
                    return BadRequest(new { error = "Action Type and Cause are required to report a defect." });
                var wo = await _workOrderService.ReportAsync(new WorkOrder
                {
                    AssetId = item.AssetId, ActionTypeId = actionTypeId, CauseId = causeId, Notes = notes,
                }, CurrentUserId);
                workOrderId = wo.Id;
            }

            await _orders.UpdateInspectionItemAsync(itemId, outcome, notes, workOrderId, CurrentUserId);
            return Ok(new { outcome, workOrderId });
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
