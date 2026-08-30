using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;

namespace ShiftFlow.Web.Controllers;

/// <summary>Personal landing page for field-worker roles — an at-a-glance summary of "my" work.</summary>
[Authorize]
public class MyHomeController : Controller
{
    private readonly UserManager<ApplicationUser> _um;
    private readonly IInspectionOrderService _orders;

    public MyHomeController(UserManager<ApplicationUser> um, IInspectionOrderService orders)
    {
        _um = um; _orders = orders;
    }

    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Vendor")) return RedirectToAction("Index", "VendorPortal");

        var user = await _um.GetUserAsync(User);
        var id = user!.Id;

        var myOrders = await _orders.GetMyOrdersAsync(id, includeDone: false);
        var overdueCount = myOrders.Count(o => o.DueDate.HasValue && o.DueDate < DateTime.Today);

        ViewBag.UserName = user.FullName;
        ViewBag.OpenOrderCount = myOrders.Count;
        ViewBag.OverdueOrderCount = overdueCount;
        ViewBag.RecentOrders = myOrders.Take(5).ToList();
        return View();
    }
}
