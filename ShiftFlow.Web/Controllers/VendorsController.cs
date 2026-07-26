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
public class VendorsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IVendorService _vendorService;
    private readonly UserManager<ApplicationUser> _userManager;
    public VendorsController(ApplicationDbContext db, IVendorService vendorService, UserManager<ApplicationUser> userManager)
    {
        _db = db; _vendorService = vendorService; _userManager = userManager;
    }

    [Authorize(Policy = PermissionCatalog.VendorView)]
    public async Task<IActionResult> Index()
    {
        var vendors = await _db.Vendors.OrderBy(v => v.Name).ToListAsync();
        return View(vendors);
    }

    [Authorize(Policy = PermissionCatalog.VendorView)]
    public async Task<IActionResult> Details(int id)
    {
        var vendor = await _db.Vendors.Include(v => v.WorkOrders).ThenInclude(w => w.Asset).Include(v => v.User).FirstOrDefaultAsync(v => v.Id == id);
        if (vendor == null) return NotFound();
        return View(vendor);
    }

    /// <summary>Creates the one portal login this vendor uses. Temp password is shown once via TempData — no email is sent (admin shares it with the vendor directly).</summary>
    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLogin(int vendorId, string email)
    {
        var vendor = await _db.Vendors.FindAsync(vendorId);
        if (vendor == null) return NotFound();
        if (vendor.UserId != null) { TempData["Error"] = "This vendor already has a login."; return RedirectToAction(nameof(Details), new { id = vendorId }); }
        if (string.IsNullOrWhiteSpace(email)) { TempData["Error"] = "An email is required to create a login."; return RedirectToAction(nameof(Details), new { id = vendorId }); }

        var tempPassword = $"Vendor1{Guid.NewGuid():N}"[..12].ToUpperInvariant().Insert(4, "ab");
        var user = new ApplicationUser { UserName = email, Email = email, FullName = vendor.Name, IsActive = true, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Details), new { id = vendorId });
        }
        await _userManager.AddToRoleAsync(user, "Vendor");
        vendor.UserId = user.Id;
        vendor.Email = email;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Login created.";
        TempData["TempPassword"] = tempPassword;
        return RedirectToAction(nameof(Details), new { id = vendorId });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int vendorId)
    {
        var vendor = await _db.Vendors.Include(v => v.User).FirstOrDefaultAsync(v => v.Id == vendorId);
        if (vendor?.User == null) return NotFound();

        var tempPassword = $"Vendor1{Guid.NewGuid():N}"[..12].ToUpperInvariant().Insert(4, "ab");
        var token = await _userManager.GeneratePasswordResetTokenAsync(vendor.User);
        var result = await _userManager.ResetPasswordAsync(vendor.User, token, tempPassword);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Details), new { id = vendorId });
        }
        TempData["Success"] = "Password reset.";
        TempData["TempPassword"] = tempPassword;
        return RedirectToAction(nameof(Details), new { id = vendorId });
    }

    [Authorize(Policy = PermissionCatalog.VendorManage)]
    public IActionResult Create()
    {
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new VendorViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VendorViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = _userManager.GetUserId(User)!;
        await _vendorService.CreateAsync(new Vendor
        {
            Name = vm.Name, NameAr = vm.NameAr, ContactName = vm.ContactName, Phone = vm.Phone,
            Email = vm.Email, Specialization = vm.Specialization, Status = vm.Status,
        }, userId);
        TempData["Success"] = "Vendor created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCatalog.VendorManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var vendor = await _db.Vendors.FindAsync(id);
        if (vendor == null) return NotFound();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new VendorViewModel
        {
            Id = vendor.Id, Name = vendor.Name, NameAr = vendor.NameAr, ContactName = vendor.ContactName,
            Phone = vendor.Phone, Email = vendor.Email, Specialization = vendor.Specialization, Status = vendor.Status,
        });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(VendorViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = _userManager.GetUserId(User)!;
        await _vendorService.UpdateAsync(new Vendor
        {
            Id = vm.Id, Name = vm.Name, NameAr = vm.NameAr, ContactName = vm.ContactName, Phone = vm.Phone,
            Email = vm.Email, Specialization = vm.Specialization, Status = vm.Status,
        }, userId);
        TempData["Success"] = "Vendor updated.";
        return RedirectToAction(nameof(Index));
    }
}
