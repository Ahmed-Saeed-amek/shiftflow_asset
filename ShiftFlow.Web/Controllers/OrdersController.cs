using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.Services;
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
    private readonly IOrderCreationService _orderCreation;
    private readonly IGroupService _groups;
    private readonly IAssetScopeService _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILanguageService _loc;

    public OrdersController(IOrderCreationService orderCreation, IGroupService groups, IAssetScopeService scope,
        ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILanguageService loc)
    {
        _orderCreation = orderCreation; _groups = groups; _scope = scope;
        _db = db; _userManager = userManager; _loc = loc;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;
    private const int PageSize = 25;

    public async Task<IActionResult> Index(string? status, string? search, int? orderTypeId, bool overdue = false, int page = 1)
    {
        var canViewInspection = (await AuthZ(PermissionCatalog.InspectionOrderView)).Succeeded;
        var canViewMaintenance = (await AuthZ(PermissionCatalog.MaintenanceOrderView)).Succeeded;
        if (!canViewInspection && !canViewMaintenance) return Forbid();
        if (page < 1) page = 1;
        search = SearchQuery.Cap(search);

        // A scoped user must not even see rows they'd be 404'd out of on Details.
        List<int>? scopedAssetIds = null;
        if (await _scope.HasScopeAsync(CurrentUserId))
            scopedAssetIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsNoTracking(), CurrentUserId)).Select(a => a.Id).ToListAsync();

        // Both halves are filtered, ordered and truncated in SQL - only the page's worth of rows
        // from each side is ever materialised, then merged.
        var take = page * PageSize;
        var rows = new List<OrderListRow>();
        var totalCount = 0;

        if (canViewInspection)
        {
            var q = _db.InspectionOrders.AsNoTracking().AsQueryable();
            if (scopedAssetIds != null) q = q.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(o => o.OrderNumber.Contains(search));
            if (orderTypeId.HasValue) q = q.Where(o => o.OrderTypeId == orderTypeId);
            if (overdue)
            {
                var today = DateTime.UtcNow.Date;
                q = q.Where(o => o.Status != OrderStatuses.Done && o.Status != OrderStatuses.Cancelled && o.DueDate != null && o.DueDate < today);
            }
            totalCount += await q.CountAsync();
            rows.AddRange(await q.OrderByDescending(o => o.CreatedAt).Take(take)
                .Select(o => new OrderListRow
                {
                    Category = "Inspection", Id = o.Id, OrderNumber = o.OrderNumber,
                    OrderTypeName = o.OrderType!.Name, OrderTypeNameAr = o.OrderType.NameAr,
                    OrderTypeColor = o.OrderType.Color, OrderTypeId = o.OrderTypeId,
                    AssetCount = o.InspectionRun!.Items.Count,
                    Status = o.Status, DueDate = o.DueDate, CreatedAt = o.CreatedAt,
                    AssignedToUserName = o.AssignedToUser!.FullName, AssignedToGroupName = o.AssignedToGroup!.Name,
                }).ToListAsync());
        }
        // overdue is an Inspection-only concept (DueDate + Status != Done) - a request for the
        // overdue view suppresses Maintenance rows rather than mixing in non-overdue ones.
        if (canViewMaintenance && !overdue)
        {
            var q = _db.MaintenanceOrders.AsNoTracking().AsQueryable();
            if (scopedAssetIds != null) q = q.Where(m => scopedAssetIds.Contains(m.AssetId));
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(m => m.Status == status);
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(m => m.OrderNumber.Contains(search) || m.Asset!.AssetTag.Contains(search));
            if (orderTypeId.HasValue) q = q.Where(m => m.OrderTypeId == orderTypeId);
            totalCount += await q.CountAsync();
            rows.AddRange(await q.OrderByDescending(m => m.CreatedDate).Take(take)
                .Select(m => new OrderListRow
                {
                    Category = "Maintenance", Id = m.Id, OrderNumber = m.OrderNumber,
                    OrderTypeName = m.OrderType!.Name, OrderTypeNameAr = m.OrderType.NameAr,
                    OrderTypeColor = m.OrderType.Color, OrderTypeId = m.OrderTypeId,
                    AssetTag = m.Asset!.AssetTag,
                    Status = m.Status, DueDate = m.DueDate, CreatedAt = m.CreatedDate,
                    AssignedToUserName = m.AssignedToUser!.FullName, AssignedToGroupName = m.AssignedToGroup!.Name,
                }).ToListAsync());
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages) page = totalPages;
        var pageRows = rows.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize).ToList();
        foreach (var r in pageRows) Localize(r);

        ViewBag.Status = status; ViewBag.Search = search; ViewBag.OrderTypeId = orderTypeId; ViewBag.Overdue = overdue;
        ViewBag.Pagination = new PaginationModel { Page = page, TotalPages = totalPages };
        ViewBag.TotalCount = totalCount;
        // Every active Order Type, each carrying its own auto-assigned color - drives the per-type
        // filter chips on the list.
        ViewBag.ActiveOrderTypes = await _db.OrderTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        return View(pageRows);
    }

    /// <summary>Display text that needs the request's language - applied after the rows come back
    /// from SQL, since none of it can be translated in the database.</summary>
    private void Localize(OrderListRow r)
    {
        r.OrderTypeLabel = r.OrderTypeName != null
            ? _loc.LocalizedName(r.OrderTypeName, r.OrderTypeNameAr)
            : _loc.T(r.Category == "Inspection" ? "Inspection" : "Maintenance");
        r.AssetLabel = r.Category == "Inspection"
            ? $"{r.AssetCount} " + (r.AssetCount == 1 ? _loc.T("asset") : _loc.T("assets"))
            : r.AssetTag;
        r.AssignedToLabel = r.AssignedToUserName
            ?? (r.AssignedToGroupName != null ? $"{_loc.T("Group")}: {r.AssignedToGroupName}" : null);
    }

    public async Task<IActionResult> Create(int? assetId, int? orderTypeId)
    {
        var canManageInspection = (await AuthZ(PermissionCatalog.InspectionOrderManage)).Succeeded;
        var canManageMaintenance = (await AuthZ(PermissionCatalog.MaintenanceOrderManage)).Succeeded;
        if (!canManageInspection && !canManageMaintenance) return Forbid();

        var vm = new OrderCreateVm
        {
            AssetId = assetId ?? 0,
            AssetIds = assetId.HasValue ? new List<int> { assetId.Value } : null,
        };
        await PopulateOptionsAsync(vm, canManageInspection, canManageMaintenance);
        vm.OrderTypeId = vm.Options.OrderTypes.FirstOrDefault(t => t.Id == orderTypeId)?.Id
            ?? vm.Options.OrderTypes.FirstOrDefault()?.Id ?? 0;
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderCreateVm vm)
    {
        var canManageInspection = (await AuthZ(PermissionCatalog.InspectionOrderManage)).Succeeded;
        var canManageMaintenance = (await AuthZ(PermissionCatalog.MaintenanceOrderManage)).Succeeded;

        var orderType = await _orderCreation.GetActiveOrderTypeAsync(vm.OrderTypeId);
        if (orderType == null) return await CreateFailedAsync(vm, canManageInspection, canManageMaintenance, "Invalid order type.");
        // Never trust the client-side toggle for which branch to take - re-derive server-side.
        if (orderType.IsDirectFix && !canManageMaintenance) return Forbid();
        if (!orderType.IsDirectFix && !canManageInspection) return Forbid();

        OrderCreationResult result;
        try
        {
            result = await _orderCreation.CreateAsync(new OrderCreationRequest
            {
                OrderTypeId = vm.OrderTypeId, DueDate = vm.DueDate, AssigneeType = vm.AssigneeType,
                AssignedToUserId = vm.AssignedToUserId, AssignedToGroupId = vm.AssignedToGroupId,
                AssetId = vm.AssetId, AssetIds = vm.AssetIds,
            }, CurrentUserId);
        }
        catch (InvalidOperationException ex)
        {
            return await CreateFailedAsync(vm, canManageInspection, canManageMaintenance, ex.Message);
        }

        if (result.Count > 1)
        {
            TempData["Success"] = result.Kind == OrderCreationKind.WorkOrder
                ? $"{result.Count} work orders created — this order type requires a vendor."
                : $"{result.Count} orders created.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = result.Kind == OrderCreationKind.WorkOrder
            ? $"Work order {result.FirstOrderNumber} created — this order type requires a vendor."
            : $"Order {result.FirstOrderNumber} created.";
        var controller = result.Kind switch
        {
            OrderCreationKind.Inspection => "InspectionOrders",
            OrderCreationKind.WorkOrder => "WorkOrders",
            _ => "MaintenanceOrders",
        };
        return RedirectToAction("Details", controller, new { id = result.FirstOrderId });
    }

    private async Task<IActionResult> CreateFailedAsync(OrderCreateVm vm, bool canManageInspection, bool canManageMaintenance, string message)
    {
        ModelState.AddModelError("", message);
        await PopulateOptionsAsync(vm, canManageInspection, canManageMaintenance);
        return View(vm);
    }

    private async Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthZ(string policy) =>
        await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(User, policy);

    // Filtered per-permission: a Manage-Maintenance-only user only ever sees IsDirectFix=true
    // types on this picker; a Manage-Inspection-only user only sees IsDirectFix=false ones.
    // ThenBy(Id) breaks SortOrder ties deterministically.
    private async Task PopulateOptionsAsync(OrderCreateVm vm, bool canManageInspection, bool canManageMaintenance)
    {
        var o = vm.Options;
        o.LocationCategories = await _db.LocationCategories.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        o.Categories = await _db.AssetCategories.AsNoTracking().Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
        o.Groups = await _groups.GetAllAsync();

        var allTypes = await _db.OrderTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        o.OrderTypes = allTypes.Where(t => (t.IsDirectFix && canManageMaintenance) || (!t.IsDirectFix && canManageInspection)).ToList();
        o.OrderTypeMetaJson = System.Text.Json.JsonSerializer.Serialize(
            o.OrderTypes.ToDictionary(t => t.Id, t => new { t.IsDirectFix, t.RequiresVendor, t.AllowsMultipleAssets, t.AssignmentMode }),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        if (vm.AssetIds is { Count: > 0 })
            o.SelectedAssetChips = await _db.Assets.AsNoTracking().Where(a => vm.AssetIds.Contains(a.Id))
                .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name })
                .ToListAsync();

        if (vm.AssetId > 0)
        {
            var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == vm.AssetId);
            if (asset != null) o.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
        }

        o.SelectedEmployeeLabel = !string.IsNullOrEmpty(vm.AssignedToUserId)
            ? await _db.Users.AsNoTracking().Where(u => u.Id == vm.AssignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync()
            : null;
    }

}
