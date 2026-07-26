using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Services;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _um;
    private readonly ApplicationDbContext _db;
    private readonly RoleManager<ApplicationRole> _rm;
    private readonly IEmailService _email;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEntraDirectoryService _directory;

    public UsersController(
        UserManager<ApplicationUser> um,
        ApplicationDbContext db,
        RoleManager<ApplicationRole> rm,
        IEmailService email,
        IWhatsAppService whatsApp,
        IEntraDirectoryService directory)
    { _um = um; _db = db; _rm = rm; _email = email; _whatsApp = whatsApp; _directory = directory; }

    [Authorize(Policy = PermissionCatalog.UserView)]
    public async Task<IActionResult> Index(string? role, string? search)
    {
        var users = await _db.Users.AsNoTracking().Include(u => u.Location).OrderBy(u => u.FullName).ToListAsync();

        // Build userId→roles map in one query instead of N per-user GetRolesAsync calls
        var userIds = users.Select(u => u.Id).ToList();
        var rolesByUser = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(g => g.Key, g => (IList<string>)g.Select(x => x.Name!).ToList());

        // Ensure every user has an entry even if they have no roles
        foreach (var u in users)
            rolesByUser.TryAdd(u.Id, []);

        if (!string.IsNullOrWhiteSpace(role))
            users = users.Where(u => rolesByUser[u.Id].Contains(role)).ToList();
        if (!string.IsNullOrWhiteSpace(search))
            users = users.Where(u =>
                (u.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        ViewBag.RolesByUser = rolesByUser;
        ViewBag.RoleFilter = role;
        ViewBag.SearchFilter = search;
        ViewBag.RoleOptions = await _rm.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();
        ViewBag.RoleNameArByName = await _rm.Roles.ToDictionaryAsync(r => r.Name!, r => r.NameAr);
        return View(users);
    }

    [Authorize(Policy = PermissionCatalog.UserView)]
    public async Task<IActionResult> Profile(string id)
    {
        var vm = await BuildProfileAsync(id, null, null);
        if (vm is null) return NotFound();
        ViewBag.RoleNameArByName = await _rm.Roles.ToDictionaryAsync(r => r.Name!, r => r.NameAr);
        return View(vm);
    }

    // Self-service metrics — any authenticated user sees only their own data.
    [Authorize]
    public async Task<IActionResult> MyMetrics(string? range)
    {
        var id = _um.GetUserId(User)!;
        var (from, to) = ResolveRange(range);
        ViewBag.SelectedRange = range ?? "all";
        var vm = await BuildProfileAsync(id, from, to);
        if (vm is null) return NotFound();
        ViewBag.RoleNameArByName = await _rm.Roles.ToDictionaryAsync(r => r.Name!, r => r.NameAr);
        return View(vm);
    }

    // Read-only list of inspection orders assigned to the current user (directly, or via a
    // Team they belong to). Open work by default; showAll=true also includes Done.
    [Authorize]
    public async Task<IActionResult> MyTasks(bool showAll = false)
    {
        var id = _um.GetUserId(User)!;
        var rows = await BuildMyInspectionOrderRowsAsync(id, showAll);
        ViewBag.ShowAll = showAll;
        return View(rows);
    }

    // Same query/shape as MyTasks, rendered to PDF instead of HTML.
    [Authorize]
    public async Task<IActionResult> MyTasksPdf(bool showAll = false)
    {
        var id = _um.GetUserId(User)!;
        var user = await _um.FindByIdAsync(id);
        var rows = await BuildMyInspectionOrderRowsAsync(id, showAll);

        var bytes = BuildTasksPdf(rows, user?.FullName ?? user?.Email ?? id, showAll);
        return File(bytes, "application/pdf", $"MyTasks_{DateTime.Today:yyyyMMdd}.pdf");
    }

    private async Task<List<InspectionOrderRow>> BuildMyInspectionOrderRowsAsync(string userId, bool showAll)
    {
        var myTeamIds = await _db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync();

        var query = _db.InspectionOrders.AsNoTracking()
            .Where(o => o.AssignedToUserId == userId || (o.AssignedToTeamId != null && myTeamIds.Contains(o.AssignedToTeamId.Value)));

        if (!showAll)
            query = query.Where(o => o.Status != "Done");

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new InspectionOrderRow
            {
                OrderId         = o.Id,
                OrderNumber     = o.OrderNumber,
                Title           = o.Title,
                Status          = o.Status,
                DueDate         = o.DueDate,
                AssignedContext = o.AssignedToUserId == userId ? "Me" : "Team",
                CreatedAt       = o.CreatedAt,
                TotalAssets     = o.InspectionRun!.Items.Count,
                CheckedAssets   = o.InspectionRun!.Items.Count(i => i.Outcome != "Pending"),
            })
            .Take(300)
            .ToListAsync();
    }

    private static byte[] BuildTasksPdf(List<InspectionOrderRow> rows, string userName, bool showAll)
    {
        using var ms = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(ms);
        var pdf    = new iText.Kernel.Pdf.PdfDocument(writer);
        var doc    = new iText.Layout.Document(pdf, iText.Kernel.Geom.PageSize.A4);
        doc.SetMargins(20, 20, 20, 20);

        // Shared colors — Rased design system
        var navyBg  = new iText.Kernel.Colors.DeviceRgb(0x1A, 0x27, 0x44);
        var lightBg = new iText.Kernel.Colors.DeviceRgb(0xF0, 0xF4, 0xF8);
        var white   = iText.Kernel.Colors.ColorConstants.WHITE;
        var mutedTx = new iText.Kernel.Colors.DeviceRgb(0x60, 0x7D, 0x8B);
        var borderC = new iText.Kernel.Colors.DeviceRgb(0xDE, 0xE2, 0xE6);

        static (iText.Kernel.Colors.DeviceRgb bg, iText.Kernel.Colors.DeviceRgb fg) StatusColors(string s) => s switch
        {
            "Done"       => (new(0xE8, 0xF5, 0xE9), new(0x1B, 0x5E, 0x20)),
            "InProgress" => (new(0xE3, 0xF2, 0xFD), new(0x0D, 0x47, 0xA1)),
            _            => (new(0xEC, 0xEF, 0xF1), new(0x49, 0x50, 0x57)),
        };

        doc.Add(new iText.Layout.Element.Paragraph("MY TASKS")
            .SetFontSize(8).SetFontColor(white).SetBold()
            .SetBackgroundColor(navyBg).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
            .SetMarginBottom(0));
        doc.Add(new iText.Layout.Element.Paragraph(userName)
            .SetFontSize(14).SetFontColor(white).SetBold()
            .SetBackgroundColor(navyBg).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
            .SetMarginBottom(0));
        doc.Add(new iText.Layout.Element.Paragraph(showAll ? "All orders" : "Open orders")
            .SetFontSize(8).SetFontColor(white)
            .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(0x34, 0x49, 0x5E))
            .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
            .SetMarginBottom(10));

        var tbl = new iText.Layout.Element.Table(new float[] { 1.5f, 2.5f, 1.2f, 1.2f, 1.2f, 1.2f })
            .UseAllAvailableWidth().SetMarginBottom(8).SetFontSize(8);

        foreach (var h in new[] { "Order #", "Title", "Status", "Checked", "Due Date", "Context" })
            tbl.AddHeaderCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph(h).SetBold())
                .SetBackgroundColor(lightBg)
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));

        foreach (var r in rows)
        {
            var (statusBg, statusFg) = StatusColors(r.Status);
            tbl.AddCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph(r.OrderNumber))
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));
            tbl.AddCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph(r.Title))
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));
            tbl.AddCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph(r.Status))
                .SetBackgroundColor(statusBg).SetFontColor(statusFg)
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));
            tbl.AddCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph($"{r.CheckedAssets}/{r.TotalAssets}"))
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));
            tbl.AddCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph(r.DueDate?.ToString("dd/MM/yyyy") ?? "-"))
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));
            tbl.AddCell(new iText.Layout.Element.Cell()
                .Add(new iText.Layout.Element.Paragraph(r.AssignedContext))
                .SetPadding(4).SetBorder(new iText.Layout.Borders.SolidBorder(borderC, 0.5f)));
        }

        doc.Add(tbl);

        if (rows.Count == 0)
            doc.Add(new iText.Layout.Element.Paragraph("No orders to show.")
                .SetFontColor(mutedTx).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER));

        doc.Add(new iText.Layout.Element.Paragraph($"Generated {DateTime.Now:dd/MM/yyyy, HH:mm}  |  {rows.Count} order(s)")
            .SetFontSize(7).SetFontColor(mutedTx)
            .SetBackgroundColor(lightBg)
            .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
            .SetMarginTop(4));

        doc.Close();
        return ms.ToArray();
    }

    private static (DateTime? from, DateTime? to) ResolveRange(string? range)
    {
        var today = DateTime.Today;
        return range switch
        {
            "month" => (new DateTime(today.Year, today.Month, 1), today),
            "30"    => (today.AddDays(-30), today),
            "90"    => (today.AddDays(-90), today),
            _       => (null, null),   // "all" / null → no date filter
        };
    }

    private async Task<EmployeeProfileViewModel?> BuildProfileAsync(string id, DateTime? from, DateTime? to)
    {
        var user = await _um.FindByIdAsync(id);
        if (user is null) return null;

        var roles = await _um.GetRolesAsync(user);

        var myTeamIds = await _db.TeamMembers.Where(m => m.UserId == id).Select(m => m.TeamId).ToListAsync();
        var orderQuery = _db.InspectionOrders.AsNoTracking()
            .Where(o => o.AssignedToUserId == id || (o.AssignedToTeamId != null && myTeamIds.Contains(o.AssignedToTeamId.Value)));
        if (from.HasValue) orderQuery = orderQuery.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue)   orderQuery = orderQuery.Where(o => o.CreatedAt <= to.Value);

        // Aggregate order KPIs in SQL — returns ~3 rows
        var orderKpis = await orderQuery
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int OrderKpi(string status) => orderKpis.FirstOrDefault(k => k.Status == status)?.Count ?? 0;

        var defectCount = await orderQuery
            .SelectMany(o => o.InspectionRun!.Items)
            .CountAsync(i => i.Outcome == "Defective");

        // Detail rows — capped at 500
        var orders = await orderQuery
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .Select(o => new InspectionOrderRow
            {
                OrderId         = o.Id,
                OrderNumber     = o.OrderNumber,
                Title           = o.Title,
                Status          = o.Status,
                DueDate         = o.DueDate,
                AssignedContext = o.AssignedToUserId == id ? "Me" : "Team",
                CreatedAt       = o.CreatedAt,
                TotalAssets     = o.InspectionRun!.Items.Count,
                CheckedAssets   = o.InspectionRun!.Items.Count(i => i.Outcome != "Pending"),
            })
            .ToListAsync();

        var vm = new EmployeeProfileViewModel
        {
            Id             = user.Id,
            FullName       = user.FullName,
            Email          = user.Email ?? "",
            EmployeeNumber = user.EmployeeNumber ?? "—",
            Department     = user.Department ?? "—",
            Specialization = user.Specialization ?? "—",
            Phone          = user.Phone ?? "—",
            Role           = string.Join(", ", roles),
            Roles          = roles.ToList(),
            IsActive       = user.IsActive,
            CreatedDate    = user.CreatedDate,
            LastLogin      = user.LastLoginDate,

            OrdersAssigned   = orderKpis.Sum(k => k.Count),
            OrdersOpen       = OrderKpi("Open"),
            OrdersInProgress = OrderKpi("InProgress"),
            OrdersDone       = OrderKpi("Done"),
            DefectsFound     = defectCount,

            Orders     = orders,
            AuditLog   = [],
        };

        return vm;
    }

    [Authorize(Policy = PermissionCatalog.UserManage)]
    public async Task<IActionResult> Create()
    {
        await LoadVB();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new UserCreateViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.UserManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel vm)
    {
        if (!ModelState.IsValid) { await LoadVB(); return View(vm); }

        var tempPassword = GenerateTempPassword();

        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FullName = vm.FullName,
            EmployeeNumber = vm.EmployeeNumber,
            Department = vm.Department,
            Phone = vm.Phone,
            IsActive = true,
            EmailConfirmed = true,
            CreatedDate = DateTime.UtcNow,
        };

        var result = await _um.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            await LoadVB();
            return View(vm);
        }

        await _um.AddToRoleAsync(user, vm.Role);

        // Mark account as requiring a password change on first login
        await _um.AddClaimAsync(user, new Claim("must_change_password", "true"));

        var credentialsMessage =
            $"Hello {vm.FullName},\n\n" +
            $"Your ShiftFlow account has been created.\n\n" +
            $"Email: {vm.Email}\n" +
            $"Temporary password: {tempPassword}\n\n" +
            $"You will be prompted to set a new password on your first login.\n\n" +
            $"— ShiftFlow Administration";

        // Send credentials email (failure is logged but doesn't block creation)
        await _email.SendAsync(
            vm.Email,
            "Welcome to ShiftFlow — Your Account Details",
            credentialsMessage
                .Replace("\n", "<br>")
                .Replace(tempPassword, $"<strong><code>{tempPassword}</code></strong>"));

        // Send credentials via WhatsApp if a phone number was provided
        if (!string.IsNullOrWhiteSpace(vm.Phone))
            await _whatsApp.SendAsync(vm.Phone, credentialsMessage);

        var notifyNote = string.IsNullOrWhiteSpace(vm.Phone)
            ? $"Credentials emailed to {vm.Email}."
            : $"Credentials emailed to {vm.Email} and sent via WhatsApp to {vm.Phone}.";

        TempData["Success"] = $"User {vm.FullName} created. {notifyNote}";
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string GenerateTempPassword()
    {
        // Produces e.g. "Sf@mK7xP2nQa" — satisfies uppercase, lowercase, digit, special
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string special = "@#!$";

        Span<char> pwd = stackalloc char[12];
        pwd[0] = Pick(upper);
        pwd[1] = Pick(lower);
        pwd[2] = Pick(digits);
        pwd[3] = Pick(special);
        const string all = lower + upper + digits + special;
        for (int i = 4; i < 12; i++) pwd[i] = Pick(all);

        // Shuffle
        for (int i = 11; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
        }

        return new string(pwd);

        static char Pick(string chars) =>
            chars[RandomNumberGenerator.GetInt32(chars.Length)];
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.UserManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var currentUserId = _um.GetUserId(User);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _um.FindByIdAsync(id);
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        // Remove claims and logins first to avoid FK conflicts on databases without cascade delete
        var claims = await _um.GetClaimsAsync(user);
        if (claims.Count > 0)
            await _um.RemoveClaimsAsync(user, claims);

        var logins = await _um.GetLoginsAsync(user);
        foreach (var login in logins)
            await _um.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);

        var roles = await _um.GetRolesAsync(user);
        if (roles.Count > 0)
            await _um.RemoveFromRolesAsync(user, roles);

        try
        {
            var result = await _um.DeleteAsync(user);
            if (result.Succeeded)
                TempData["Success"] = $"User '{user.FullName}' deleted.";
            else
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
        }
        catch (DbUpdateException)
        {
            // Shift assignments, group memberships, and audit log entries are deliberately
            // Restrict/NoAction on the user FK, so deleting anyone with real activity history
            // fails at the DB level — that's intentional, it protects shift/audit records
            // from being silently orphaned or lost. Deactivating instead revokes access
            // without touching that history.
            TempData["Error"] = $"'{user.FullName}' has shift or audit history and cannot be deleted. " +
                "Deactivate the account instead to remove their access.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.UserManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var currentUserId = _um.GetUserId(User);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _um.FindByIdAsync(id);
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        await _um.UpdateAsync(user);
        TempData["Success"] = $"User '{user.FullName}' {(user.IsActive ? "activated" : "deactivated")}.";

        return RedirectToAction(nameof(Index));
    }

    // ── Import from Microsoft Entra ID directory ────────────────────────────────

    [HttpGet, Authorize(Policy = PermissionCatalog.UserManage)]
    public async Task<IActionResult> SearchDirectory(string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Json(new { results = Array.Empty<object>() });

        List<EntraDirectoryUser> matches;
        try
        {
            matches = await _directory.SearchAsync(q);
        }
        catch (EntraDirectorySearchException ex)
        {
            return Json(new { error = ex.Message });
        }

        var results = new List<object>();
        foreach (var m in matches)
        {
            var existing = await _um.FindByEmailAsync(m.Email);
            results.Add(new
            {
                m.Id,
                m.DisplayName,
                m.Email,
                m.JobTitle,
                m.Department,
                m.GivenName,
                m.Surname,
                m.MobilePhone,
                m.EmployeeId,
                m.OfficeLocation,
                m.CompanyName,
                m.City,
                m.Country,
                m.AccountEnabled,
                alreadyImported = existing != null,
            });
        }

        return Json(new { results });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.UserManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportFromDirectory(string email, string fullName, string? department, string role,
        string? employeeNumber, string? specialization, string? phone)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(role))
        {
            TempData["Error"] = "Missing required fields for import.";
            return RedirectToAction(nameof(Index));
        }

        if (await _um.FindByEmailAsync(email) is not null)
        {
            TempData["Error"] = $"'{email}' is already an app user.";
            return RedirectToAction(nameof(Index));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            Department = department,
            EmployeeNumber = string.IsNullOrWhiteSpace(employeeNumber) ? null : employeeNumber,
            Specialization = string.IsNullOrWhiteSpace(specialization) ? null : specialization,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            IsActive = true,
            EmailConfirmed = true,
            CreatedDate = DateTime.UtcNow,
        };

        // No password — Entra-only account, same shape as a self-service Entra sign-up
        // (AccountController.ExternalLoginCallback's account-creation branch). Their first
        // "Sign in with Microsoft" auto-links via that same controller's existing
        // email-match branch — no separate linking logic needed here.
        var result = await _um.CreateAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        await _um.AddToRoleAsync(user, role);

        TempData["Success"] = $"'{fullName}' imported. They'll get access on their next \"Sign in with Microsoft\".";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadVB()
    {
        ViewBag.Roles = await _rm.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();
        ViewBag.RoleNameArByName = await _rm.Roles.ToDictionaryAsync(r => r.Name!, r => r.NameAr);
    }
}
