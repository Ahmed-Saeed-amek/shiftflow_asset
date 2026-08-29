using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

/// <summary>Admin-managed catalog shared by Inspection Orders and Maintenance Orders — each row
/// declares whether it tracks a defect outcome (Action Type + Cause required) and whether it
/// requires a vendor (routes into the WorkOrder pipeline). See OrderType.</summary>
[Authorize(Policy = PermissionCatalog.OrderTypeManage)]
public class OrderTypesController : Controller
{
    private readonly ApplicationDbContext _db;
    public OrderTypesController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var types = await _db.OrderTypes.OrderBy(t => t.SortOrder).ToListAsync();
        return View(types);
    }

    public IActionResult Create()
    {
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new OrderTypeViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderTypeViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.OrderTypes.Add(new OrderType
        {
            Name = vm.Name, NameAr = vm.NameAr, Prefix = vm.Prefix,
            TracksDefectOutcome = vm.TracksDefectOutcome, RequiresVendor = vm.RequiresVendor,
            IsActive = vm.IsActive, SortOrder = vm.SortOrder,
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var type = await _db.OrderTypes.FindAsync(id);
        if (type == null) return NotFound();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new OrderTypeViewModel
        {
            Id = type.Id, Name = type.Name, NameAr = type.NameAr, Prefix = type.Prefix,
            TracksDefectOutcome = type.TracksDefectOutcome, RequiresVendor = type.RequiresVendor,
            IsActive = type.IsActive, SortOrder = type.SortOrder,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(OrderTypeViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var type = await _db.OrderTypes.FindAsync(vm.Id);
        if (type == null) return NotFound();
        type.Name = vm.Name; type.NameAr = vm.NameAr; type.Prefix = vm.Prefix;
        type.TracksDefectOutcome = vm.TracksDefectOutcome; type.RequiresVendor = vm.RequiresVendor;
        type.IsActive = vm.IsActive; type.SortOrder = vm.SortOrder;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type updated.";
        return RedirectToAction(nameof(Index));
    }
}
