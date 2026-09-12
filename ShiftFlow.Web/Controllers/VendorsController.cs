using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class VendorsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IVendorService _vendorService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILanguageService _loc;
    private readonly IAuditService _audit;
    public VendorsController(ApplicationDbContext db, IVendorService vendorService, UserManager<ApplicationUser> userManager, ILanguageService loc, IAuditService audit)
    {
        _db = db; _vendorService = vendorService; _userManager = userManager; _loc = loc; _audit = audit;
    }

    private const int PageSize = 25;

    [Authorize(Policy = PermissionCatalog.VendorView)]
    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        if (page < 1) page = 1;
        var vendorQuery = _db.Vendors.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            vendorQuery = vendorQuery.Where(v =>
                EF.Functions.Like(v.Name, $"%{term}%") ||
                (v.ContactName != null && EF.Functions.Like(v.ContactName, $"%{term}%")) ||
                (v.Email != null && EF.Functions.Like(v.Email, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim();
            vendorQuery = vendorQuery.Where(v => v.Status == s);
        }

        var query = vendorQuery.OrderBy(v => v.Name);
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages) page = totalPages;
        ViewBag.Pagination = new ShiftFlow.Web.ViewModels.PaginationModel { Page = page, TotalPages = totalPages };
        ViewBag.SearchFilter = search;
        ViewBag.StatusFilter = status;
        ViewBag.TotalCount = totalCount;
        var vendors = await query.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        return View(vendors);
    }

    /// <summary>Real Edit page. Index used to be linked to with ?edit=id and a script that
    /// auto-clicked the row's modal button — a deep link that broke whenever the vendor wasn't on
    /// the current page (and now that Index is paged, that is the normal case).</summary>
    [Authorize(Policy = PermissionCatalog.VendorManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
        if (vendor == null) return NotFound();
        return View(new VendorViewModel
        {
            Id = vendor.Id, Name = vendor.Name, NameAr = vendor.NameAr, ContactName = vendor.ContactName,
            Phone = vendor.Phone, Email = vendor.Email, Specialization = vendor.Specialization, Status = vendor.Status,
        });
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

        email = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email)) { TempData["Error"] = _loc.T("An email is required to create a login."); return RedirectToAction(nameof(Details), new { id = vendorId }); }
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
        {
            TempData["Error"] = _loc.T("Enter a valid email address.");
            return RedirectToAction(nameof(Details), new { id = vendorId });
        }

        var tempPassword = ShiftFlow.Web.Services.TempPasswordGenerator.Generate();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = vendor.Name, IsActive = true, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Details), new { id = vendorId });
        }
        await _userManager.AddToRoleAsync(user, "Vendor");
        // Same first-login gate as UsersController.Create — without it the admin-generated temp
        // password stayed valid indefinitely as the vendor's real credential.
        await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("must_change_password", "true"));
        vendor.UserId = user.Id;
        // Only fill in the vendor's contact email when it's blank. Overwriting an existing,
        // curated contact address with whatever was typed into the login form silently discarded it.
        if (string.IsNullOrWhiteSpace(vendor.Email)) vendor.Email = email;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CreateLogin", "Vendor", vendorId.ToString(), _userManager.GetUserId(User)!, newValue: $"{vendor.Name} ({email})");
        TempData["Success"] = _loc.T("Login created.");
        TempData["TempPassword"] = tempPassword;
        return RedirectToAction(nameof(Details), new { id = vendorId });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int vendorId)
    {
        var vendor = await _db.Vendors.Include(v => v.User).FirstOrDefaultAsync(v => v.Id == vendorId);
        if (vendor?.User == null) return NotFound();

        var tempPassword = ShiftFlow.Web.Services.TempPasswordGenerator.Generate();
        var token = await _userManager.GeneratePasswordResetTokenAsync(vendor.User);
        var result = await _userManager.ResetPasswordAsync(vendor.User, token, tempPassword);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Details), new { id = vendorId });
        }
        var existingClaims = await _userManager.GetClaimsAsync(vendor.User);
        if (!existingClaims.Any(c => c.Type == "must_change_password"))
            await _userManager.AddClaimAsync(vendor.User, new System.Security.Claims.Claim("must_change_password", "true"));

        await _audit.LogAsync("ResetPassword", "Vendor", vendorId.ToString(), _userManager.GetUserId(User)!, newValue: vendor.Name);
        TempData["Success"] = _loc.T("Password reset.");
        TempData["TempPassword"] = tempPassword;
        return RedirectToAction(nameof(Details), new { id = vendorId });
    }

    // Create/Edit are modals on Index now — 7 fields is still small enough that a separate
    // full-page form was pure overhead. Both just redirect back to Index either way, so an
    // invalid submit reports the error there instead of returning a page that no longer exists.
    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VendorViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name and status are required, and email must be valid.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.Vendors.AnyAsync(v => v.Name == vm.Name))
        {
            TempData["Error"] = _loc.T("A vendor named '{0}' already exists.", vm.Name);
            return RedirectToAction(nameof(Index));
        }
        var userId = _userManager.GetUserId(User)!;
        await _vendorService.CreateAsync(new Vendor
        {
            Name = vm.Name, NameAr = vm.NameAr, ContactName = vm.ContactName, Phone = vm.Phone,
            Email = vm.Email, Specialization = vm.Specialization, Status = vm.Status,
        }, userId);
        TempData["Success"] = "Vendor created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.VendorManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(VendorViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Name and status are required, and email must be valid.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.Vendors.AnyAsync(v => v.Id != vm.Id && v.Name == vm.Name))
        {
            TempData["Error"] = _loc.T("A vendor named '{0}' already exists.", vm.Name);
            return RedirectToAction(nameof(Index));
        }
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
