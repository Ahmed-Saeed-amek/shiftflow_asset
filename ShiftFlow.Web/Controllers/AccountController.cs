using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _si;
    private readonly UserManager<ApplicationUser> _um;
    private readonly IConfiguration _config;
    private readonly IPermissionService _permissions;
    private readonly ILanguageService _loc;

    public AccountController(SignInManager<ApplicationUser> si, UserManager<ApplicationUser> um, IConfiguration config, IPermissionService permissions, ILanguageService loc)
    { _si = si; _um = um; _config = config; _permissions = permissions; _loc = loc; }

    // Mirrors the Program.cs check that gates whether the "EntraID" scheme
    // was registered at all — the button/action must agree with that or
    // ExternalLogin() throws trying to challenge a scheme that doesn't exist.
    private bool IsEntraConfigured =>
        !string.IsNullOrWhiteSpace(_config["AzureAd:ClientId"]) && !string.IsNullOrWhiteSpace(_config["AzureAd:TenantId"]);

    // The site root ("/") routes here (see Program.cs's default route) so every role lands
    // somewhere they actually have access to — Dashboard for managers, VendorPortal for
    // vendors, MyHome for everyone else — instead of the old default of routing straight to
    // DashboardController, which Access-Denied anyone without InspectionOrder.Manage.
    [Authorize]
    public async Task<IActionResult> Home() => await HomeRedirect();

    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null, string? remoteError = null)
    {
        if (_si.IsSignedIn(User)) return await HomeRedirect();
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.EntraConfigured = IsEntraConfigured;
        if (!string.IsNullOrEmpty(remoteError))
        {
            // Recognized failures get a friendly message; anything else falls
            // back to a generic one rather than surfacing raw IdP error text.
            ModelState.AddModelError("", remoteError.Contains("access_denied") || remoteError.Contains("declined to consent")
                ? _loc.T("Microsoft sign-in was cancelled.")
                : _loc.T("Microsoft sign-in failed. Please try again or use your email and password."));
        }
        return View();
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _si.PasswordSignInAsync(vm.Email, vm.Password, vm.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _um.FindByEmailAsync(vm.Email);
            if (user != null && !user.IsActive)
            {
                await _si.SignOutAsync();
                ModelState.AddModelError("", _loc.T("This account has been deactivated. Contact an administrator."));
                return View(vm);
            }
            if (user != null)
            {
                user.LastLoginDate = DateTime.UtcNow;
                await _um.UpdateAsync(user);

                // Redirect to password change if this is a first-login temp password
                var claims = await _um.GetClaimsAsync(user);
                if (claims.Any(c => c.Type == "must_change_password"))
                    return RedirectToAction(nameof(ChangePassword));
            }

            var canViewDashboard = await _permissions.HasPermissionAsync(user!.Id, PermissionCatalog.InspectionOrderManage);
            var isVendor = await _um.IsInRoleAsync(user, "Vendor");
            var isHR = await _um.IsInRoleAsync(user, "HR");
            return LocalRedirect(BuildLandingPath(canViewDashboard, returnUrl, isVendor, isHR));
        }

        ModelState.AddModelError("", result.IsLockedOut
            ? _loc.T("Account locked. Try in 5 min.")
            : _loc.T("Invalid email or password."));

        return View(vm);
    }

    // ── Sign in with Microsoft (Azure AD / Entra ID) ────────────────────────────

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string? returnUrl = null)
    {
        if (!IsEntraConfigured)
        {
            ModelState.AddModelError("", _loc.T("Microsoft sign-in is not configured yet."));
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.EntraConfigured = false;
            return View(nameof(Login), new LoginViewModel());
        }
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = _si.ConfigureExternalAuthenticationProperties("EntraID", redirectUrl);
        return Challenge(properties, "EntraID");
    }

    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            ModelState.AddModelError("", $"{_loc.T("Microsoft sign-in error:")} {remoteError}");
            return View(nameof(Login), new LoginViewModel());
        }

        var info = await _si.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ModelState.AddModelError("", _loc.T("Could not load Microsoft sign-in information."));
            return View(nameof(Login), new LoginViewModel());
        }

        var signInResult = await _si.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
            isPersistent: false, bypassTwoFactor: true);

        ApplicationUser? user;
        if (signInResult.Succeeded)
        {
            user = await _um.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        }
        else
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                ModelState.AddModelError("", _loc.T("Microsoft account has no email claim; cannot sign in."));
                return View(nameof(Login), new LoginViewModel());
            }

            user = await _um.FindByEmailAsync(email);
            if (user is not null)
            {
                // Link this Entra identity to the existing account so future
                // sign-ins go straight through ExternalLoginSignInAsync above.
                var addLoginResult = await _um.AddLoginAsync(user, info);
                if (!addLoginResult.Succeeded)
                {
                    ModelState.AddModelError("", _loc.T("Could not link Microsoft account."));
                    return View(nameof(Login), new LoginViewModel());
                }
            }
            else
            {
                var fullName = ResolveDisplayName(info.Principal, email);
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    IsActive = true,
                    EmailConfirmed = true,
                    CreatedDate = DateTime.UtcNow,
                };
                var createResult = await _um.CreateAsync(user); // no password — Entra-only account
                if (!createResult.Succeeded)
                {
                    foreach (var e in createResult.Errors) ModelState.AddModelError("", e.Description);
                    return View(nameof(Login), new LoginViewModel());
                }
                // Lowest-privilege role — an admin promotes via the Users page,
                // same as any manually created account. Deliberately no
                // "must_change_password" claim: Entra users never set a local password.
                await _um.AddToRoleAsync(user, "Technician");
                await _um.AddLoginAsync(user, info);
            }

            await _si.SignInAsync(user, isPersistent: false);
        }

        if (!user!.IsActive)
        {
            await _si.SignOutAsync();
            ModelState.AddModelError("", "This account has been deactivated. Contact an administrator.");
            return View(nameof(Login), new LoginViewModel());
        }

        user.LastLoginDate = DateTime.UtcNow;
        await _um.UpdateAsync(user);

        var canViewDashboard = await _permissions.HasPermissionAsync(user.Id, PermissionCatalog.InspectionOrderManage);
        return LocalRedirect(BuildLandingPath(canViewDashboard, returnUrl));
    }

    // The "name" claim is absent for Entra accounts whose directory profile has no
    // display name set — falling straight to the raw email produced ugly, wrong-looking
    // names (e.g. "AHMED.SAEED@amekcapital.com"). Try given+family name next since Entra
    // populates those far more reliably than "name" for sparsely-provisioned accounts.
    private static string ResolveDisplayName(ClaimsPrincipal principal, string email)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        var given = principal.FindFirstValue(ClaimTypes.GivenName);
        var surname = principal.FindFirstValue(ClaimTypes.Surname);
        var combined = string.Join(" ", new[] { given, surname }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(combined) ? email : combined;
    }

    // Shared by Login() POST, ExternalLoginCallback, and HomeRedirect(). canViewDashboard
    // is resolved explicitly via IPermissionService rather than reading the ambient
    // ClaimsPrincipal in the two sign-in paths, since that reflects the request's
    // principal from before this sign-in happened. Mirrors _Layout.cshtml's
    // sidebar's manager-tier sections exactly (both key off InspectionOrder.Manage),
    // so the landing page always matches the sidebar the user is about to see.
    // LocalRedirect() throws InvalidOperationException on a non-local URL instead of safely
    // rejecting it, so returnUrl must be validated with Url.IsLocalUrl before it's ever handed
    // to LocalRedirect() at every call site below — a crafted
    // POST /Account/Login?ReturnUrl=//evil.example.com/... otherwise crashes the login action
    // with a raw stack trace instead of just falling back to the default landing page.
    private string BuildLandingPath(bool canViewDashboard, string? returnUrl = null, bool isVendor = false, bool isHR = false) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            // HR has no inspection-order permissions at all, so MyHome's "My Open Inspection
            // Orders"/"Recent Inspection Orders" widgets would always render empty/zero for
            // them — Users (the employee directory, HR's actual permission) is the meaningful
            // landing page instead. Mirrors the sidebar's existing !IsInRole("HR") My Home hide.
            : (isVendor ? "/VendorPortal" : isHR ? "/Users" : canViewDashboard ? "/Dashboard" : "/MyHome");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _si.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => View();

    private async Task<IActionResult> HomeRedirect()
    {
        var userId = _um.GetUserId(User);
        var canViewDashboard = userId != null && await _permissions.HasPermissionAsync(userId, PermissionCatalog.InspectionOrderManage);
        return LocalRedirect(BuildLandingPath(canViewDashboard, isVendor: User.IsInRole("Vendor"), isHR: User.IsInRole("HR")));
    }

    // ── Change password (forced on first login) ────────────────────────────────

    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _um.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var result = await _um.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }

        // Remove the first-login claim so they are not prompted again
        var claims = await _um.GetClaimsAsync(user);
        var flag = claims.FirstOrDefault(c => c.Type == "must_change_password");
        if (flag is not null)
            await _um.RemoveClaimAsync(user, flag);

        // Refresh the auth cookie so the updated claims take effect
        await _si.RefreshSignInAsync(user);

        TempData["Success"] = _loc.T("Password changed successfully.");
        return await HomeRedirect();
    }
}
