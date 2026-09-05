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

/// <summary>Standalone, lightweight in-house fix — an employee is assigned to service an asset
/// directly (e.g. swap a part). No vendor, no Work Order pipeline: just Open -> Done (or
/// Cancelled), no admin confirmation step once the employee reports the fix.</summary>
[Authorize]
public class MaintenanceOrdersController : Controller
{
    private readonly IMaintenanceOrderService _orders;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamService _teams;

    public MaintenanceOrdersController(IMaintenanceOrderService orders, UserManager<ApplicationUser> userManager, ITeamService teams)
    {
        _orders = orders; _userManager = userManager; _teams = teams;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    // Index and Create moved to the unified OrdersController (Orders/Index, Orders/Create) — see
    // that controller. This controller keeps Details/Complete/Cancel/ExportExcel, which are still
    // specific to Maintenance Orders' own fix-report shape (cost/parts/completion date).

    [Authorize(Policy = PermissionCatalog.MaintenanceOrderReport)]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _orders.GetByIdAsync(id);
        if (order == null) return NotFound();

        var isManager = (await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(User, PermissionCatalog.MaintenanceOrderManage)).Succeeded;
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, CurrentUserId);
        if (!isManager && !isAssignee && !isTeamMember) return Forbid();

        ViewBag.IsManager = isManager;
        ViewBag.IsAssignee = isAssignee || isTeamMember;
        if (isManager) ViewBag.Teams = await _teams.GetAllAsync();
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.MaintenanceOrderReport)]
    public async Task<IActionResult> Complete(int id, MaintenanceOrderCompleteVm vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            var parts = (vm.SparePartIds ?? []).Zip(vm.PartQuantities ?? [], (spId, q) => (SparePartId: spId, Quantity: q)).ToList();
            var order = await _orders.CompleteAsync(id, vm.CompletedDate, parts, CurrentUserId);
            TempData["Success"] = order.Status == "PendingApproval"
                ? "Fix reported — awaiting manager approval before it closes."
                : "Fix reported — maintenance order closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.MaintenanceOrderManage)]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            await _orders.ApproveAsync(id, CurrentUserId);
            TempData["Success"] = "Maintenance order approved and closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.MaintenanceOrderManage)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        try
        {
            await _orders.CancelAsync(id, reason, CurrentUserId);
            TempData["Success"] = "Maintenance order cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Manager-only recovery path for an order whose sole assignee has since been
    /// deactivated (Complete has no manager override — it's gated on being the current assignee or
    /// a member of the assigned Team) — moves the order to a different employee or Team instead of
    /// leaving the work permanently un-actionable.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.MaintenanceOrderManage)]
    public async Task<IActionResult> Reassign(int id, string? assignedToUserId, int? assignedToTeamId)
    {
        try
        {
            await _orders.ReassignAsync(id, assignedToUserId, assignedToTeamId, CurrentUserId);
            TempData["Success"] = "Maintenance order reassigned.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // "My assigned maintenance orders" now lives on the unified Users/MyOrders page (combined
    // with Inspection Orders and Work Orders) — see UsersController.MyOrders.

    [Authorize(Policy = PermissionCatalog.MaintenanceOrderExport)]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _orders.ExportToExcelAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"MaintenanceOrders_{DateTime.Today:yyyyMMdd}.xlsx");
    }
}
