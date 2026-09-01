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
    private readonly IWorkOrderService _workOrders;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public MaintenanceOrdersController(IMaintenanceOrderService orders, IWorkOrderService workOrders, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _orders = orders; _workOrders = workOrders; _db = db; _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    [Authorize(Policy = PermissionCatalog.MaintenanceOrderView)]
    public async Task<IActionResult> Index(string? status, string? search)
    {
        var orders = await _orders.GetAllAsync(status, search);
        ViewBag.Status = status;
        ViewBag.Search = search;
        return View(orders);
    }

    [Authorize(Policy = PermissionCatalog.MaintenanceOrderManage)]
    public async Task<IActionResult> Create(int? assetId)
    {
        if (assetId.HasValue)
        {
            var asset = await _db.Assets.FindAsync(assetId.Value);
            if (asset != null) ViewBag.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
        }
        await LoadOrderTypesViewBagAsync();
        // Default to "Standard" rather than whichever order type happens to sort first (which
        // was "Inspection" — a confusing default on a form specifically for a Maintenance Order).
        var defaultTypeId = ((List<OrderType>)ViewBag.OrderTypes).FirstOrDefault(t => t.Name == "Standard")?.Id ?? 0;
        return View(new MaintenanceOrderCreateVm { AssetId = assetId ?? 0, OrderTypeId = defaultTypeId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.MaintenanceOrderManage)]
    public async Task<IActionResult> Create(MaintenanceOrderCreateVm vm)
    {
        if (!ModelState.IsValid)
        {
            await LoadCreateFailureViewBagAsync(vm);
            return View(vm);
        }

        var orderType = await _db.OrderTypes.FirstOrDefaultAsync(t => t.Id == vm.OrderTypeId && t.IsActive);
        if (orderType == null)
        {
            ModelState.AddModelError("", "Invalid order type.");
            await LoadCreateFailureViewBagAsync(vm);
            return View(vm);
        }

        try
        {
            // A type that requires a vendor doesn't become a MaintenanceOrder at all — MaintenanceOrder
            // has no vendor concept — it routes into a WorkOrder instead, which owns the vendor pipeline.
            if (orderType.RequiresVendor)
            {
                var wo = await _workOrders.CreateAsync(new WorkOrder
                {
                    AssetId = vm.AssetId,
                    AssignedToUserId = string.IsNullOrWhiteSpace(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
                    Description = vm.Description,
                    RequiresVendorResponse = true,
                }, CurrentUserId);
                TempData["Success"] = $"Work order {wo.WorkOrderNumber} created — this order type requires a vendor.";
                return RedirectToAction("Details", "WorkOrders", new { id = wo.Id });
            }

            var order = await _orders.CreateAsync(vm.AssetId, vm.AssignedToUserId!, vm.Description, vm.DueDate, CurrentUserId, orderType.Id);
            TempData["Success"] = $"Maintenance order {order.OrderNumber} created.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await LoadCreateFailureViewBagAsync(vm);
            return View(vm);
        }
    }

    private async Task LoadOrderTypesViewBagAsync() =>
        ViewBag.OrderTypes = await _db.OrderTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();

    private async Task LoadCreateFailureViewBagAsync(MaintenanceOrderCreateVm vm)
    {
        if (vm.AssetId > 0)
        {
            var asset = await _db.Assets.FindAsync(vm.AssetId);
            if (asset != null) ViewBag.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
        }
        if (!string.IsNullOrEmpty(vm.AssignedToUserId))
            ViewBag.SelectedEmployeeLabel = await _db.Users.Where(u => u.Id == vm.AssignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync();
        await LoadOrderTypesViewBagAsync();
    }

    [Authorize(Policy = PermissionCatalog.MaintenanceOrderReport)]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _orders.GetByIdAsync(id);
        if (order == null) return NotFound();

        var isManager = (await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(User, PermissionCatalog.MaintenanceOrderManage)).Succeeded;
        var isAssignee = order.AssignedToUserId == CurrentUserId;
        if (!isManager && !isAssignee) return Forbid();

        ViewBag.IsManager = isManager;
        ViewBag.IsAssignee = isAssignee;
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.MaintenanceOrderReport)]
    public async Task<IActionResult> Complete(int id, MaintenanceOrderCompleteVm vm)
    {
        try
        {
            var parts = (vm.SparePartIds ?? []).Zip(vm.PartQuantities ?? [], (spId, q) => (SparePartId: spId, Quantity: q)).ToList();
            await _orders.CompleteAsync(id, vm.FixDescription ?? "", vm.Cost, vm.CompletedDate, parts, CurrentUserId);
            TempData["Success"] = "Fix reported — maintenance order closed.";
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
