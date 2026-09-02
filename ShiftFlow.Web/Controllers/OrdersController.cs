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

/// <summary>Unified entry point for Inspection Orders and Maintenance Orders — one "Orders" nav
/// section, one creation screen, one combined list. The two underlying entities/services stay
/// exactly as they are (InspectionOrdersController/MaintenanceOrdersController still own Details,
/// UpdateItem, Complete, Cancel, Export); this controller only replaces their old separate Create
/// screens and adds a combined Index. Which of the two an order becomes is driven by the picked
/// OrderType's IsDirectFix flag — never trusted from the client, always re-derived server-side.</summary>
[Authorize]
public class OrdersController : Controller
{
    private readonly IInspectionOrderService _inspectionOrders;
    private readonly IMaintenanceOrderService _maintenanceOrders;
    private readonly IWorkOrderService _workOrders;
    private readonly ITeamService _teams;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrdersController(IInspectionOrderService inspectionOrders, IMaintenanceOrderService maintenanceOrders,
        IWorkOrderService workOrders, ITeamService teams, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _inspectionOrders = inspectionOrders; _maintenanceOrders = maintenanceOrders;
        _workOrders = workOrders; _teams = teams; _db = db; _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> Index(string? status, string? search, string? category, bool overdue = false)
    {
        var canViewInspection = (await AuthZ(PermissionCatalog.InspectionOrderView)).Succeeded;
        var canViewMaintenance = (await AuthZ(PermissionCatalog.MaintenanceOrderView)).Succeeded;
        if (!canViewInspection && !canViewMaintenance) return Forbid();

        var rows = new List<MyWorkOrderRow>();

        if (canViewInspection && category != "Maintenance")
        {
            var orders = await _inspectionOrders.GetAllAsync(status, search, overdue);
            rows.AddRange(orders.Select(o => new MyWorkOrderRow
            {
                Category = "Inspection", CategoryLabel = "Inspection", Id = o.Id, OrderNumber = o.OrderNumber,
                AssetLabel = $"{o.InspectionRun?.Items.Count ?? 0} " + ((o.InspectionRun?.Items.Count ?? 0) == 1 ? "asset" : "assets"),
                Status = o.Status, DueDate = o.DueDate, CreatedAt = o.CreatedAt, DetailsController = "InspectionOrders",
                AssignedToLabel = o.AssignedToUser?.FullName ?? (o.AssignedToTeam != null ? $"Team: {o.AssignedToTeam.Name}" : null),
            }));
        }
        // overdue is an Inspection-only concept (DueDate + Status != Done) - a request for the
        // overdue view suppresses Maintenance rows entirely rather than silently mixing in
        // non-overdue Maintenance rows under a filter name that doesn't apply to them.
        if (canViewMaintenance && !overdue && category != "Inspection")
        {
            var orders = await _maintenanceOrders.GetAllAsync(status, search);
            rows.AddRange(orders.Select(m => new MyWorkOrderRow
            {
                Category = "Maintenance", CategoryLabel = "Maintenance", Id = m.Id, OrderNumber = m.OrderNumber,
                AssetLabel = m.Asset?.AssetTag, Status = m.Status, DueDate = m.DueDate, CreatedAt = m.CreatedDate,
                DetailsController = "MaintenanceOrders", AssignedToLabel = m.AssignedToUser?.FullName,
            }));
        }

        ViewBag.Status = status; ViewBag.Search = search; ViewBag.Category = category; ViewBag.Overdue = overdue;
        return View(rows.OrderByDescending(r => r.CreatedAt).ToList());
    }

    public async Task<IActionResult> Create(int? assetId, int? orderTypeId)
    {
        var canManageInspection = (await AuthZ(PermissionCatalog.InspectionOrderManage)).Succeeded;
        var canManageMaintenance = (await AuthZ(PermissionCatalog.MaintenanceOrderManage)).Succeeded;
        if (!canManageInspection && !canManageMaintenance) return Forbid();

        await PopulateCreateViewBagAsync(canManageInspection, canManageMaintenance);
        var offered = (List<OrderType>)ViewBag.OrderTypes;
        var vm = new OrderCreateVm
        {
            OrderTypeId = offered.FirstOrDefault(t => t.Id == orderTypeId)?.Id ?? offered.FirstOrDefault()?.Id ?? 0,
            AssetId = assetId ?? 0,
            AssetIds = assetId.HasValue ? new List<int> { assetId.Value } : null,
        };
        if (assetId.HasValue)
        {
            var asset = await _db.Assets.FindAsync(assetId.Value);
            if (asset != null)
            {
                var label = $"{asset.AssetTag} — {asset.Name}";
                ViewBag.SelectedAssetLabel = label;
                ViewBag.SelectedAssetChips = new List<AssetChip> { new() { Id = asset.Id, Label = label } };
            }
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderCreateVm vm)
    {
        var canManageInspection = (await AuthZ(PermissionCatalog.InspectionOrderManage)).Succeeded;
        var canManageMaintenance = (await AuthZ(PermissionCatalog.MaintenanceOrderManage)).Succeeded;

        var orderType = await _db.OrderTypes.FirstOrDefaultAsync(t => t.Id == vm.OrderTypeId && t.IsActive);
        if (orderType == null)
        {
            ModelState.AddModelError("", "Invalid order type.");
            await PopulateCreateViewBagAsync(canManageInspection, canManageMaintenance, vm);
            return View(vm);
        }
        // Never trust the client-side toggle for which branch to take - re-derive server-side.
        if (orderType.IsDirectFix && !canManageMaintenance) return Forbid();
        if (!orderType.IsDirectFix && !canManageInspection) return Forbid();

        if (!orderType.IsDirectFix)
        {
            var nested = new InspectionOrderCreateVm
            {
                OrderTypeId = vm.OrderTypeId, Description = vm.Description, DueDate = vm.DueDate,
                AssigneeType = vm.AssigneeType, AssignedToUserId = vm.AssignedToUserId,
                AssignedToTeamId = vm.AssignedToTeamId, AssetIds = vm.AssetIds,
            };
            ModelState.Clear();
            // No prefix - TryValidateModel(model) keys ModelState by the nested VM's own property
            // names (AssetIds, AssignedToTeamId, ...), which are identical to OrderCreateVm's, so
            // asp-validation-for on the same-named fields in Views/Orders/Create.cshtml lines up.
            if (!TryValidateModel(nested))
            {
                await PopulateCreateViewBagAsync(canManageInspection, canManageMaintenance, vm);
                return View(vm);
            }
            try
            {
                var order = await _inspectionOrders.CreateAsync(nested.OrderTypeId, nested.Description,
                    nested.AssigneeType == "User" ? nested.AssignedToUserId : null,
                    nested.AssigneeType == "Team" ? nested.AssignedToTeamId : null,
                    nested.AssetIds, nested.DueDate, CurrentUserId);
                TempData["Success"] = $"Order {order.OrderNumber} created.";
                return RedirectToAction("Details", "InspectionOrders", new { id = order.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                await PopulateCreateViewBagAsync(canManageInspection, canManageMaintenance, vm);
                return View(vm);
            }
        }
        else
        {
            var nested = new MaintenanceOrderCreateVm
            {
                AssetId = vm.AssetId, OrderTypeId = vm.OrderTypeId,
                AssignedToUserId = vm.AssignedToUserId, Description = vm.Description, DueDate = vm.DueDate,
            };
            ModelState.Clear();
            if (!TryValidateModel(nested))
            {
                await PopulateCreateViewBagAsync(canManageInspection, canManageMaintenance, vm);
                return View(vm);
            }
            try
            {
                // Preserves MaintenanceOrdersController.Create POST's existing branch verbatim: a
                // RequiresVendor OrderType produces a WorkOrder instead of a MaintenanceOrder.
                if (orderType.RequiresVendor)
                {
                    var wo = await _workOrders.CreateAsync(new WorkOrder
                    {
                        AssetId = nested.AssetId,
                        AssignedToUserId = string.IsNullOrWhiteSpace(nested.AssignedToUserId) ? null : nested.AssignedToUserId,
                        Description = nested.Description, RequiresVendorResponse = true,
                    }, CurrentUserId);
                    TempData["Success"] = $"Work order {wo.WorkOrderNumber} created — this order type requires a vendor.";
                    return RedirectToAction("Details", "WorkOrders", new { id = wo.Id });
                }
                var order = await _maintenanceOrders.CreateAsync(nested.AssetId, nested.AssignedToUserId!,
                    nested.Description, nested.DueDate, CurrentUserId, orderType.Id);
                TempData["Success"] = $"Order {order.OrderNumber} created.";
                return RedirectToAction("Details", "MaintenanceOrders", new { id = order.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                await PopulateCreateViewBagAsync(canManageInspection, canManageMaintenance, vm);
                return View(vm);
            }
        }
    }

    private async Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthZ(string policy) =>
        await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(User, policy);

    // Filtered per-permission: a Manage-Maintenance-only user only ever sees IsDirectFix=true
    // types on this picker; a Manage-Inspection-only user only sees IsDirectFix=false ones;
    // someone with both sees everything. Avoids a manager clicking through options they can't
    // actually use.
    private async Task PopulateCreateViewBagAsync(bool canManageInspection, bool canManageMaintenance, OrderCreateVm? vm = null)
    {
        ViewBag.LocationCategories = await _db.LocationCategories.OrderBy(c => c.Id).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Teams = await _teams.GetAllAsync();

        // ThenBy(Id) breaks ties deterministically - SortOrder alone isn't unique (e.g. the seeded
        // Inspection and Standard rows both default to 0), and without a tiebreaker the picker's
        // order could silently shuffle whenever a new same-SortOrder type is added or the query
        // just re-runs, which is exactly what a fresh-eyes test caught.
        var allTypes = await _db.OrderTypes.Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        var offered = allTypes.Where(t => (t.IsDirectFix && canManageMaintenance) || (!t.IsDirectFix && canManageInspection)).ToList();
        ViewBag.OrderTypes = offered;
        ViewBag.OrderTypeMetaJson = System.Text.Json.JsonSerializer.Serialize(
            offered.ToDictionary(t => t.Id, t => new { t.IsDirectFix, t.RequiresVendor }));

        ViewBag.SelectedAssetChips = vm?.AssetIds is { Count: > 0 }
            ? await _db.Assets.Where(a => vm.AssetIds.Contains(a.Id))
                .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name })
                .ToListAsync()
            : (ViewBag.SelectedAssetChips as List<AssetChip> ?? new List<AssetChip>());

        if (vm != null && vm.AssetId > 0 && ViewBag.SelectedAssetLabel == null)
        {
            var asset = await _db.Assets.FindAsync(vm.AssetId);
            if (asset != null) ViewBag.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
        }

        ViewBag.SelectedEmployeeLabel = !string.IsNullOrEmpty(vm?.AssignedToUserId)
            ? await _db.Users.Where(u => u.Id == vm.AssignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync()
            : null;
    }
}
