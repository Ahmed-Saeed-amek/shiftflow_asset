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
        var types = await _db.OrderTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        return View(types);
    }

    // Create/Edit are modals on Index now — 6 fields is still small enough that a separate
    // full-page form was pure overhead. Both just redirect back to Index either way, so an
    // invalid submit reports the error there instead of returning a page that no longer exists.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderTypeViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name and prefix are required.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.OrderTypes.AnyAsync(t => t.Prefix == vm.Prefix))
        {
            TempData["Error"] = $"Prefix '{vm.Prefix}' is already used by another order type.";
            return RedirectToAction(nameof(Index));
        }
        _db.OrderTypes.Add(new OrderType
        {
            Name = vm.Name, NameAr = vm.NameAr, Prefix = vm.Prefix,
            TracksDefectOutcome = vm.TracksDefectOutcome, RequiresVendor = vm.RequiresVendor,
            IsDirectFix = vm.IsDirectFix, IsActive = vm.IsActive, SortOrder = vm.SortOrder,
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(OrderTypeViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name and prefix are required.";
            return RedirectToAction(nameof(Index));
        }
        var type = await _db.OrderTypes.FindAsync(vm.Id);
        if (type == null) return NotFound();
        if (await _db.OrderTypes.AnyAsync(t => t.Id != vm.Id && t.Prefix == vm.Prefix))
        {
            TempData["Error"] = $"Prefix '{vm.Prefix}' is already used by another order type.";
            return RedirectToAction(nameof(Index));
        }
        type.Name = vm.Name; type.NameAr = vm.NameAr; type.Prefix = vm.Prefix;
        type.TracksDefectOutcome = vm.TracksDefectOutcome; type.RequiresVendor = vm.RequiresVendor;
        type.IsDirectFix = vm.IsDirectFix; type.IsActive = vm.IsActive; type.SortOrder = vm.SortOrder;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type updated.";
        return RedirectToAction(nameof(Index));
    }
}
