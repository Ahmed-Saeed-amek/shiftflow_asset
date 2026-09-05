using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.Services;

namespace ShiftFlow.Web.Controllers;

/// <summary>Personal landing page for field-worker roles — an at-a-glance summary of "my" work.</summary>
[Authorize]
public class MyHomeController : Controller
{
    private readonly UserManager<ApplicationUser> _um;
    private readonly ApplicationDbContext _db;
    private readonly ILanguageService _loc;

    public MyHomeController(UserManager<ApplicationUser> um, ApplicationDbContext db, ILanguageService loc)
    {
        _um = um; _db = db; _loc = loc;
    }

    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Vendor")) return RedirectToAction("Index", "VendorPortal");

        var user = await _um.GetUserAsync(User);
        var id = user!.Id;

        // Combines Inspection Orders, Maintenance Orders, and Work Orders — this page previously
        // only looked at Inspection Orders, so a user with open Maintenance/Work Orders and zero
        // Inspection Orders saw "you're all caught up" while real open work sat unaddressed.
        var myOrders = await MyWorkOrderRowBuilder.BuildAsync(_db, _loc, id, showAll: false);
        var overdueCount = myOrders.Count(o => o.DueDate.HasValue && o.DueDate < DateTime.UtcNow.Date);

        ViewBag.UserName = user.FullName;
        ViewBag.OpenOrderCount = myOrders.Count;
        ViewBag.OverdueOrderCount = overdueCount;
        ViewBag.RecentOrders = myOrders.Take(5).ToList();
        return View();
    }
}
