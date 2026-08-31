using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Services;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

/// <summary>
/// Vendor-only portal. Every action re-verifies the work order belongs to the caller's own Vendor
/// record — a vendor login must never reach another vendor's work order, even by guessing a URL.
/// </summary>
[Authorize(Roles = "Vendor")]
public class VendorPortalController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWorkOrderService _workOrderService;
    private readonly UserManager<ApplicationUser> _userManager;
    public VendorPortalController(ApplicationDbContext db, IWorkOrderService workOrderService, UserManager<ApplicationUser> userManager)
    {
        _db = db; _workOrderService = workOrderService; _userManager = userManager;
    }

    private async Task<int?> GetMyVendorIdAsync()
    {
        var userId = _userManager.GetUserId(User);
        return await _db.Vendors.Where(v => v.UserId == userId).Select(v => (int?)v.Id).FirstOrDefaultAsync();
    }

    public async Task<IActionResult> Index(string? stage, string? q)
    {
        var vendorId = await GetMyVendorIdAsync();
        if (vendorId == null) return Forbid();
        var query = _db.WorkOrders.Include(w => w.Asset).Where(w => w.VendorId == vendorId);
        if (!string.IsNullOrWhiteSpace(stage)) query = query.Where(w => w.Stage == stage);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(w => w.WorkOrderNumber.Contains(q) || (w.Asset != null && w.Asset.AssetTag.Contains(q)));
        ViewBag.Stage = stage;
        ViewBag.Q = q;
        var workOrders = await query.OrderByDescending(w => w.CreatedDate).ToListAsync();
        return View(workOrders);
    }

    /// <summary>Locations (via each work order's asset's Zone) of every work order assigned to this vendor.
    /// Only assets whose Zone has coordinates set are included, matching the pattern used by Zones/MapData.</summary>
    public async Task<IActionResult> MapData()
    {
        var vendorId = await GetMyVendorIdAsync();
        if (vendorId == null) return Forbid();
        var workOrders = await _db.WorkOrders
            .Include(w => w.Asset).ThenInclude(a => a!.Zone).ThenInclude(z => z!.LocationCategory)
            .Where(w => w.VendorId == vendorId && w.Asset!.Zone!.Latitude != null && w.Asset.Zone.Longitude != null)
            .Select(w => new
            {
                w.Id, w.WorkOrderNumber, w.Stage, w.Priority,
                AssetTag = w.Asset!.AssetTag, AssetName = w.Asset.Name,
                ZoneName = w.Asset.Zone!.Name, CategoryName = w.Asset.Zone.LocationCategory!.Name,
                Latitude = w.Asset.Zone.Latitude, Longitude = w.Asset.Zone.Longitude,
            })
            .ToListAsync();
        return Json(workOrders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var vendorId = await GetMyVendorIdAsync();
        if (vendorId == null) return Forbid();
        var wo = await _db.WorkOrders
            .Include(w => w.Asset).ThenInclude(a => a!.Zone).ThenInclude(z => z!.LocationCategory)
            .Include(w => w.Asset).ThenInclude(a => a!.Category)
            .Include(w => w.ActionType).Include(w => w.Cause)
            .Include(w => w.BlockReason).Include(w => w.Parts).Include(w => w.Attachments)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound();
        // NotFound, not Forbid, when the work order exists but belongs to a different vendor —
        // Forbid's distinct "Access Denied" response let any vendor session distinguish "exists,
        // not mine" from "doesn't exist" by ID alone (an IDOR side-channel for enumerating other
        // tenants' record count/IDs), even though the actual content was already correctly blocked.
        if (wo.VendorId != vendorId) return NotFound();

        ViewBag.BlockReasons = await _db.WorkOrderBlockReasons.Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();
        return View(wo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Fix(int id, VendorFixViewModel vm)
    {
        var vendorId = await GetMyVendorIdAsync();
        if (vendorId == null) return Forbid();
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound();
        // NotFound, not Forbid, when the work order exists but belongs to a different vendor —
        // Forbid's distinct "Access Denied" response let any vendor session distinguish "exists,
        // not mine" from "doesn't exist" by ID alone (an IDOR side-channel for enumerating other
        // tenants' record count/IDs), even though the actual content was already correctly blocked.
        if (wo.VendorId != vendorId) return NotFound();

        // Parsed directly off the raw form as a defensive belt-and-braces measure alongside
        // vm.CompletionDate — a custom-format DateTime.ToString (as this app's TempData retention
        // round-trip uses) is calendar-dependent on the current culture, so anchoring the value
        // read here to InvariantCulture keeps it correct regardless of locale.
        var completionDate = FixFormRetainer.ParseCompletionDate(Request.Form);

        if (!ModelState.IsValid)
        {
            FixFormRetainer.Stash(TempData, vm, completionDate);
            TempData["Error"] = "Fix not submitted — a description is required.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Validate attachments before touching any work order state — a bad file must block the
        // whole submission, not silently go through with the fix report saved and the file dropped.
        if (vm.Files is { Count: > 0 })
        {
            var (_, rejected) = await FileUploadValidator.ValidateAllAsync(vm.Files);
            if (rejected.Count > 0)
            {
                FixFormRetainer.Stash(TempData, vm, completionDate);
                TempData["Error"] = "Fix not submitted — invalid attachment(s): " + string.Join("; ", rejected.Select(r => $"{r.FileName} — {r.Reason}"));
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        var userId = _userManager.GetUserId(User)!;
        try
        {
            var parts = (vm.PartNames ?? []).Zip(vm.PartQuantities ?? [], (n, q) => (Name: n, Quantity: q)).ToList();
            await _workOrderService.VendorFixAsync(id, vm.Description ?? "", vm.Cost, completionDate, parts, userId);
            await WorkOrderAttachmentStorage.SaveAsync(_db, id, vm.Files, userId);
            TempData["Success"] = "Fix report submitted.";
        }
        catch (InvalidOperationException ex) { FixFormRetainer.Stash(TempData, vm, completionDate); TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(int id, int blockReasonId, string? detail)
    {
        var vendorId = await GetMyVendorIdAsync();
        if (vendorId == null) return Forbid();
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound();
        // NotFound, not Forbid, when the work order exists but belongs to a different vendor —
        // Forbid's distinct "Access Denied" response let any vendor session distinguish "exists,
        // not mine" from "doesn't exist" by ID alone (an IDOR side-channel for enumerating other
        // tenants' record count/IDs), even though the actual content was already correctly blocked.
        if (wo.VendorId != vendorId) return NotFound();

        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.VendorBlockAsync(id, blockReasonId, detail, userId); TempData["Success"] = "Reported as blocked."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
