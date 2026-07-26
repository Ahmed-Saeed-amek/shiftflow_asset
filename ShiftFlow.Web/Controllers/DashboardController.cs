using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
public class DashboardController : Controller
{
    private readonly IDashboardService _dash;
    private readonly UserManager<ApplicationUser> _um;
    private readonly ApplicationDbContext _db;

    public DashboardController(IDashboardService dash, UserManager<ApplicationUser> um, ApplicationDbContext db)
    {
        _dash = dash; _um = um; _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _um.GetUserAsync(User);

        // NOTE: a scoped DbContext cannot run multiple queries concurrently — each query
        // must be awaited before the next starts, otherwise EF throws
        // "A second operation was started on this context instance".
        var kpis = await _dash.GetKpisAsync(user?.Id);

        // Inspection-order-status chart: fixed status list so the chart's shape/order/colors
        // stay stable as data grows instead of silently changing whenever a status count
        // drops to/from zero.
        string[] statusOrder = ["Open", "InProgress", "Done"];
        var statusCounts = (await _db.InspectionOrders.AsNoTracking()
                .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        ViewBag.OrderStatusLabels = statusOrder;
        ViewBag.OrderStatusData = statusOrder.Select(s => statusCounts.GetValueOrDefault(s, 0)).ToList();

        ViewBag.RecentOrders = await _db.InspectionOrders.AsNoTracking()
            .Include(o => o.AssignedToUser).Include(o => o.AssignedToTeam)
            .OrderByDescending(o => o.CreatedAt).Take(6).ToListAsync();

        ViewBag.OverdueOrders = await _db.InspectionOrders.AsNoTracking()
            .Include(o => o.AssignedToUser).Include(o => o.AssignedToTeam)
            .Where(o => o.Status != "Done" && o.DueDate != null && o.DueDate < DateTime.Today)
            .OrderBy(o => o.DueDate).Take(6).ToListAsync();

        return View(kpis);
    }
}
