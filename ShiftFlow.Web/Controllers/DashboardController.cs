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

[Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
public class DashboardController : Controller
{
    private readonly IDashboardService _dash;
    private readonly UserManager<ApplicationUser> _um;
    private readonly ApplicationDbContext _db;
    private readonly ShiftFlow.Web.Localization.ILanguageService _loc;
    private readonly IAssetScopeService _scope;

    public DashboardController(IDashboardService dash, UserManager<ApplicationUser> um, ApplicationDbContext db, ShiftFlow.Web.Localization.ILanguageService loc, IAssetScopeService scope)
    {
        _dash = dash; _um = um; _db = db; _loc = loc; _scope = scope;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _um.GetUserAsync(User);

        // NOTE: a scoped DbContext cannot run multiple queries concurrently — each query
        // must be awaited before the next starts, otherwise EF throws
        // "A second operation was started on this context instance".
        var kpis = await _dash.GetKpisAsync(user?.Id);

        // Same UserAssetScope a scoped user is already restricted to everywhere else (Orders
        // list, Details, the KPI cards above) — without it, the widgets below leaked full order
        // numbers/status/assignee for out-of-scope assets that the same user is 404'd out of one
        // click later on Details.
        List<int>? scopedAssetIds = user != null && await _scope.HasScopeAsync(user.Id)
            ? await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), user.Id)).Select(a => a.Id).ToListAsync()
            : null;

        // Inspection-order-status chart: fixed status list so the chart's shape/order/colors
        // stay stable as data grows instead of silently changing whenever a status count
        // drops to/from zero.
        string[] statusOrder = ["Open", "InProgress", "Done"];
        var statusChartQuery = _db.InspectionOrders.AsNoTracking();
        if (scopedAssetIds != null) statusChartQuery = statusChartQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var statusCounts = (await statusChartQuery
                .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        ViewBag.OrderStatusLabels = statusOrder;
        ViewBag.OrderStatusData = statusOrder.Select(s => statusCounts.GetValueOrDefault(s, 0)).ToList();

        ViewBag.RecentOrders = await BuildRecentOrdersAsync(scopedAssetIds);

        var overdueQuery = _db.InspectionOrders.AsNoTracking()
            .Where(o => o.Status != "Done" && o.Status != "Cancelled" && o.DueDate != null && o.DueDate < DateTime.UtcNow.Date);
        if (scopedAssetIds != null) overdueQuery = overdueQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        // The KPI card's own count comes from GetKpisAsync's 2-minute cache, so it could lag
        // behind this list — which always queries live — right after creating/closing an
        // overdue order. Query the live count here too (cheap: same predicate, no .Include/Take)
        // so the card and the list under it can never visibly disagree on the same page load.
        ViewBag.OverdueOrderCount = await overdueQuery.CountAsync();
        ViewBag.OverdueOrders = await overdueQuery
            .Include(o => o.AssignedToUser).Include(o => o.AssignedToTeam)
            .OrderBy(o => o.DueDate).Take(6).ToListAsync();

        return View(kpis);
    }

    /// <summary>Org-wide "what's happening" feed — the most recent orders across all three
    /// categories (Inspection, Maintenance, Work Order), not just Inspection Orders, so the
    /// dashboard reflects actual recent activity rather than one order type. Restricted to the
    /// viewer's UserAssetScope, same as everywhere else scope is enforced (see Index above).</summary>
    private async Task<List<MyWorkOrderRow>> BuildRecentOrdersAsync(List<int>? scopedAssetIds)
    {
        var inspectionQuery = _db.InspectionOrders.AsNoTracking().Include(o => o.AssignedToUser).Include(o => o.AssignedToTeam).AsQueryable();
        if (scopedAssetIds != null) inspectionQuery = inspectionQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var inspectionRows = (await inspectionQuery
            .OrderByDescending(o => o.CreatedAt).Take(6)
            .ToListAsync())
            .Select(o => new MyWorkOrderRow
            {
                Category = "Inspection", CategoryLabel = "Inspection", Id = o.Id, OrderNumber = o.OrderNumber,
                Status = o.Status, DueDate = o.DueDate, CreatedAt = o.CreatedAt, DetailsController = "InspectionOrders",
                AssignedToLabel = o.AssignedToUser != null ? o.AssignedToUser.FullName
                    : o.AssignedToTeam != null ? _loc.T("Team") + ": " + o.AssignedToTeam.Name : null,
            })
            .ToList();

        var maintenanceQuery = _db.MaintenanceOrders.AsNoTracking().Include(m => m.AssignedToUser).Include(m => m.AssignedToTeam).AsQueryable();
        if (scopedAssetIds != null) maintenanceQuery = maintenanceQuery.Where(m => scopedAssetIds.Contains(m.AssetId));
        var maintenanceRows = (await maintenanceQuery
            .OrderByDescending(m => m.CreatedDate).Take(6)
            .ToListAsync())
            .Select(m => new MyWorkOrderRow
            {
                Category = "Maintenance", CategoryLabel = "Maintenance", Id = m.Id, OrderNumber = m.OrderNumber,
                Status = m.Status, CreatedAt = m.CreatedDate, DetailsController = "MaintenanceOrders",
                AssignedToLabel = m.AssignedToUser != null ? m.AssignedToUser.FullName
                    : m.AssignedToTeam != null ? _loc.T("Team") + ": " + m.AssignedToTeam.Name : null,
            })
            .ToList();

        var workOrderQuery = _db.WorkOrders.AsNoTracking().Include(w => w.AssignedToUser).Include(w => w.Vendor).AsQueryable();
        if (scopedAssetIds != null) workOrderQuery = workOrderQuery.Where(w => scopedAssetIds.Contains(w.AssetId));
        var workOrderRows = await workOrderQuery
            .OrderByDescending(w => w.CreatedDate).Take(6)
            .Select(w => new MyWorkOrderRow
            {
                Category = "WorkOrder", CategoryLabel = "Work Order", Id = w.Id, OrderNumber = w.WorkOrderNumber,
                Status = w.Stage, CreatedAt = w.CreatedDate, DetailsController = "WorkOrders",
                AssignedToLabel = w.AssignedToUser != null ? w.AssignedToUser.FullName : w.Vendor != null ? w.Vendor.Name : null,
            })
            .ToListAsync();

        return inspectionRows.Concat(maintenanceRows).Concat(workOrderRows)
            .OrderByDescending(r => r.CreatedAt)
            .Take(6)
            .ToList();
    }
}
