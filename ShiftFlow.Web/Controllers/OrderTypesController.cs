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
        if (!ModelState.IsValid || !OrderType.AssignmentModes.Contains(vm.AssignmentMode))
        {
            TempData["Error"] = "Name and prefix are required.";
            return RedirectToAction(nameof(Index));
        }
        // WorkOrder (what a RequiresVendor type actually creates) has no team-assignment concept —
        // TeamOnly/Either here would let an admin configure a type that silently drops the team an
        // employee picks at Orders/Create time.
        if (vm.RequiresVendor && vm.AssignmentMode != "EmployeeOnly")
        {
            TempData["Error"] = "A vendor-required order type can only be assigned to an employee, not a team — team assignment isn't supported for vendor-routed work orders yet.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.OrderTypes.AnyAsync(t => t.Prefix == vm.Prefix))
        {
            TempData["Error"] = $"Prefix '{vm.Prefix}' is already used by another order type.";
            return RedirectToAction(nameof(Index));
        }
        var usedColors = await _db.OrderTypes.Select(t => t.Color).ToListAsync();
        _db.OrderTypes.Add(new OrderType
        {
            Name = vm.Name, NameAr = vm.NameAr, Prefix = vm.Prefix,
            TracksDefectOutcome = vm.TracksDefectOutcome, RequiresVendor = vm.RequiresVendor,
            IsDirectFix = vm.IsDirectFix, IsActive = vm.IsActive, SortOrder = vm.SortOrder,
            AllowsMultipleAssets = vm.AllowsMultipleAssets, AssignmentMode = vm.AssignmentMode,
            RequiresApproval = vm.RequiresApproval,
            Color = OrderTypeColors.NextColor(usedColors),
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(OrderTypeViewModel vm)
    {
        if (!ModelState.IsValid || !OrderType.AssignmentModes.Contains(vm.AssignmentMode))
        {
            TempData["Error"] = "Name and prefix are required.";
            return RedirectToAction(nameof(Index));
        }
        if (vm.RequiresVendor && vm.AssignmentMode != "EmployeeOnly")
        {
            TempData["Error"] = "A vendor-required order type can only be assigned to an employee, not a team — team assignment isn't supported for vendor-routed work orders yet.";
            return RedirectToAction(nameof(Index));
        }
        var type = await _db.OrderTypes.FindAsync(vm.Id);
        if (type == null) return NotFound();
        if (await _db.OrderTypes.AnyAsync(t => t.Id != vm.Id && t.Prefix == vm.Prefix))
        {
            TempData["Error"] = $"Prefix '{vm.Prefix}' is already used by another order type.";
            return RedirectToAction(nameof(Index));
        }
        // IsDirectFix decides which table (InspectionOrders vs MaintenanceOrders) every order or
        // recurring schedule under this type lives in — flipping it after the fact doesn't move any
        // existing rows, so RecurringOrderSchedulerService (which re-derives the target table from
        // the type's *current* IsDirectFix on every tick) would start looking in the wrong table for
        // a schedule's already-generated occurrences and re-attempt generating duplicates.
        if (type.IsDirectFix != vm.IsDirectFix && await IsInUseAsync(vm.Id))
        {
            TempData["Error"] = $"'{type.Name}' has orders or a recurring schedule using it — Direct Fix can't be changed on an in-use type.";
            return RedirectToAction(nameof(Index));
        }
        type.Name = vm.Name; type.NameAr = vm.NameAr; type.Prefix = vm.Prefix;
        type.TracksDefectOutcome = vm.TracksDefectOutcome; type.RequiresVendor = vm.RequiresVendor;
        type.IsDirectFix = vm.IsDirectFix; type.IsActive = vm.IsActive; type.SortOrder = vm.SortOrder;
        type.AllowsMultipleAssets = vm.AllowsMultipleAssets; type.AssignmentMode = vm.AssignmentMode;
        type.RequiresApproval = vm.RequiresApproval;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Only ever hard-deletes a type with zero orders/schedules referencing it (checked up
    /// front, not just caught as a DB error) — InspectionOrder/MaintenanceOrder/RecurringOrder all
    /// have a Restrict FK to OrderTypeId, so any order ever created under this type would otherwise
    /// leave a dangling reference. A type that's actually been used should be deactivated instead
    /// (the IsActive checkbox on Edit), which already hides it from the Orders/Create picker.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var type = await _db.OrderTypes.FindAsync(id);
        if (type == null) return NotFound();

        if (await IsInUseAsync(id))
        {
            TempData["Error"] = $"'{type.Name}' has orders or a recurring schedule using it — deactivate it instead of deleting.";
            return RedirectToAction(nameof(Index));
        }

        _db.OrderTypes.Remove(type);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order type deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> IsInUseAsync(int orderTypeId) =>
        await _db.InspectionOrders.AnyAsync(o => o.OrderTypeId == orderTypeId)
        || await _db.MaintenanceOrders.AnyAsync(m => m.OrderTypeId == orderTypeId)
        || await _db.RecurringOrders.AnyAsync(r => r.OrderTypeId == orderTypeId)
        || await _db.WorkOrders.AnyAsync(w => w.OrderTypeId == orderTypeId);
}
