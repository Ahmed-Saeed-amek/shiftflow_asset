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

[Authorize]
public class WorkOrdersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWorkOrderService _workOrderService;
    private readonly IContractService _contractService;
    private readonly UserManager<ApplicationUser> _userManager;
    public WorkOrdersController(ApplicationDbContext db, IWorkOrderService workOrderService, IContractService contractService, UserManager<ApplicationUser> userManager)
    {
        _db = db; _workOrderService = workOrderService; _contractService = contractService; _userManager = userManager;
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderView)]
    public async Task<IActionResult> Index(string? stage, string? priority)
    {
        var query = _db.WorkOrders.Include(w => w.Asset).Include(w => w.Vendor).Include(w => w.AssignedToUser).AsQueryable();
        if (!string.IsNullOrWhiteSpace(stage)) query = query.Where(w => w.Stage == stage);
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(w => w.Priority == priority);
        ViewBag.Stage = stage; ViewBag.Priority = priority;
        return View(await query.OrderByDescending(w => w.CreatedDate).ToListAsync());
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderView)]
    public async Task<IActionResult> Details(int id)
    {
        var wo = await _db.WorkOrders
            .Include(w => w.Asset).ThenInclude(a => a!.Zone).ThenInclude(z => z!.LocationCategory)
            .Include(w => w.Vendor).Include(w => w.CreatedByUser).Include(w => w.AssignedToUser)
            .Include(w => w.ActionType).Include(w => w.Cause)
            .Include(w => w.BlockReason)
            .Include(w => w.SourceContract)
            .Include(w => w.Parts).Include(w => w.Attachments)
            .Include(w => w.StageEvents).ThenInclude(s => s.ChangedByUser)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound();
        ViewBag.AllVendors = await _db.Vendors.Where(v => v.Status == "Active").OrderBy(v => v.Name).ToListAsync();
        ViewBag.CurrentUserId = _userManager.GetUserId(User);
        return View(wo);
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderManage)]
    public async Task<IActionResult> Create(int? assetId)
    {
        if (assetId.HasValue)
        {
            var asset = await _db.Assets.FindAsync(assetId.Value);
            if (asset != null) ViewBag.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
        }
        ViewBag.Priorities = WorkOrder.Priorities;
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new WorkOrderViewModel { AssetId = assetId ?? 0 });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkOrderViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            if (vm.AssetId > 0)
            {
                var asset = await _db.Assets.FindAsync(vm.AssetId);
                if (asset != null) ViewBag.SelectedAssetLabel = $"{asset.AssetTag} — {asset.Name}";
            }
            ViewBag.Priorities = WorkOrder.Priorities;
            return View(vm);
        }
        var userId = _userManager.GetUserId(User)!;
        var wo = await _workOrderService.CreateAsync(new WorkOrder
        {
            AssetId = vm.AssetId, Priority = vm.Priority, Description = vm.Description, Notes = vm.Notes,
            AssignedToUserId = string.IsNullOrWhiteSpace(vm.AssignedToUserId) ? null : vm.AssignedToUserId,
            RequiresVendorResponse = vm.RequiresVendorResponse,
        }, userId);
        TempData["Success"] = $"Work order {wo.WorkOrderNumber} created.";
        return RedirectToAction(nameof(Details), new { id = wo.Id });
    }

    [Authorize(Policy = PermissionCatalog.AssetReportAction)]
    public async Task<IActionResult> Report(int assetId)
    {
        var asset = await _db.Assets.Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset == null) return NotFound();
        ViewBag.Asset = asset;
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Details", "Assets", new { id = assetId });
        return View(new ReportActionViewModel { AssetId = assetId });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.AssetReportAction), ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(ReportActionViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Asset = await _db.Assets.Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == vm.AssetId);
            return View(vm);
        }
        var userId = _userManager.GetUserId(User)!;
        var wo = await _workOrderService.ReportAsync(new WorkOrder
        {
            AssetId = vm.AssetId, ActionTypeId = vm.ActionTypeId, CauseId = vm.CauseId, Notes = vm.Notes,
        }, userId);
        TempData["Success"] = $"Reported — {wo.WorkOrderNumber} is awaiting admin review.";
        return RedirectToAction(nameof(Details), "Assets", new { id = vm.AssetId });
    }

    /// <summary>Admin accepts a Draft report: sets priority and either sends it to a vendor or, if only an employee is assigned, moves it to New for that employee to fix directly.</summary>
    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id, int? vendorId, string priority)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.AcceptAsync(id, vendorId, priority, userId); TempData["Success"] = "Accepted."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignEmployee(int id, string? employeeUserId)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.AssignEmployeeAsync(id, employeeUserId, userId); TempData["Success"] = "Employee assignment updated."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> EmployeeFix(int id, VendorFixViewModel vm)
    {
        if (vm.Files is { Count: > 0 })
        {
            var (_, rejected) = await ShiftFlow.Web.Services.FileUploadValidator.ValidateAllAsync(vm.Files);
            if (rejected.Count > 0)
            {
                TempData["Error"] = "Fix not submitted — invalid attachment(s): " + string.Join("; ", rejected.Select(r => $"{r.FileName} — {r.Reason}"));
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        var userId = _userManager.GetUserId(User)!;
        try
        {
            var parts = (vm.PartNames ?? []).Zip(vm.PartQuantities ?? [], (n, q) => (Name: n, Quantity: q)).ToList();
            await _workOrderService.EmployeeFixAsync(id, vm.Description ?? "", vm.Cost, vm.CompletionDate, parts, userId);
            await ShiftFlow.Web.Services.WorkOrderAttachmentStorage.SaveAsync(_db, id, vm.Files, userId);
            TempData["Success"] = "Fix report submitted.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Bypasses waiting on the vendor for a work order sitting at "Sent to Vendor" whose
    /// RequiresVendorResponse flag is off — usable by a manager or the assigned employee.</summary>
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceWithoutVendor(int id, VendorFixViewModel vm)
    {
        var userId = _userManager.GetUserId(User)!;
        var isManager = (await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(User, PermissionCatalog.WorkOrderManage)).Succeeded;
        try
        {
            var parts = (vm.PartNames ?? []).Zip(vm.PartQuantities ?? [], (n, q) => (Name: n, Quantity: q)).ToList();
            await _workOrderService.AdvanceWithoutVendorAsync(id, vm.Description ?? "", vm.Cost, vm.CompletionDate, parts, userId, isManager);
            TempData["Success"] = "Work order advanced without waiting on the vendor.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceClose(int id, string? reason)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.ForceCloseAsync(id, reason, userId); TempData["Success"] = "Work order force-closed."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Employee self-service: work orders assigned directly to me (no vendor portal involved).
    /// Only this month's by default; range=all lifts the date bound.</summary>
    [Authorize]
    public async Task<IActionResult> MyMaintenanceOrders(bool showAll = false, string? range = null)
    {
        var userId = _userManager.GetUserId(User)!;
        // The date range only makes sense when browsing history (showAll) — a still-open work
        // order stays relevant no matter how long ago it was created, so the default "open work"
        // view must never let a "this month" bound silently hide it.
        var (from, to) = showAll ? ResolveRange(range ?? "month") : (null, null);
        var query = _db.WorkOrders.Include(w => w.Asset).Where(w => w.AssignedToUserId == userId);
        if (!showAll) query = query.Where(w => w.Stage != "Closed");
        if (from.HasValue) query = query.Where(w => w.CreatedDate >= from.Value);
        if (to.HasValue) query = query.Where(w => w.CreatedDate <= to.Value);
        ViewBag.ShowAll = showAll;
        ViewBag.SelectedRange = range ?? "month";
        return View(await query.OrderByDescending(w => w.CreatedDate).ToListAsync());
    }

    private static (DateTime? from, DateTime? to) ResolveRange(string? range)
    {
        var today = DateTime.Today;
        return range switch
        {
            // "to" is the current moment, not midnight-today, so anything created later today
            // (the common case right after this feature ships) still falls inside the range.
            "month" => (new DateTime(today.Year, today.Month, 1), DateTime.Now),
            _ => (null, null),
        };
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.RejectAsync(id, reason, userId); TempData["Success"] = "Report rejected."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Sends an admin-created ("New") work order to a resolved Service-contract vendor.</summary>
    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToVendor(int id, int vendorId)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.SendToVendorAsync(id, vendorId, userId); TempData["Success"] = "Sent to vendor."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Admin resolves whatever blocked the vendor and sends the work order back to the same vendor.</summary>
    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Resend(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.ResendToVendorAsync(id, userId); TempData["Success"] = "Resent to vendor."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Admin accepts the vendor's fix as complete, closing the work order.</summary>
    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.ConfirmFixAsync(id, userId); TempData["Success"] = "Work order closed."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Admin re-judges priority while reviewing the work order — not limited to the one-time Accept step.</summary>
    [HttpPost, Authorize(Policy = PermissionCatalog.WorkOrderManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePriority(int id, string priority)
    {
        var userId = _userManager.GetUserId(User)!;
        try { await _workOrderService.UpdatePriorityAsync(id, priority, userId); TempData["Success"] = "Priority updated."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Downloads a vendor's fix-report attachment — staff with WorkOrder.View, or the vendor that owns the work order, only.</summary>
    [Authorize]
    public async Task<IActionResult> DownloadAttachment(int attachmentId)
    {
        var attachment = await _db.WorkOrderAttachments.Include(a => a.WorkOrder).FirstOrDefaultAsync(a => a.Id == attachmentId);
        if (attachment?.WorkOrder == null) return NotFound();

        var isStaff = (await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(User, PermissionCatalog.WorkOrderView)).Succeeded;
        if (!isStaff)
        {
            var userId = _userManager.GetUserId(User);
            var myVendorId = await _db.Vendors.Where(v => v.UserId == userId).Select(v => (int?)v.Id).FirstOrDefaultAsync();
            if (myVendorId == null || myVendorId != attachment.WorkOrder.VendorId) return Forbid();
        }

        var path = ShiftFlow.Web.Services.WorkOrderAttachmentStorage.ResolvePhysicalPath(attachment.FilePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, attachment.FileType ?? "application/octet-stream", attachment.FileName);
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderExport)]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _workOrderService.ExportToExcelAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"WorkOrders_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    [Authorize(Policy = PermissionCatalog.WorkOrderExport)]
    public async Task<IActionResult> ExportPdf()
    {
        var bytes = await _workOrderService.ExportToPdfAsync();
        return File(bytes, "application/pdf", $"WorkOrders_{DateTime.Today:yyyyMMdd}.pdf");
    }
}
