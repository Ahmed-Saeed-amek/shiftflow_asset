using Microsoft.AspNetCore.Mvc;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Web.Controllers;

public class LanguageController : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string lang, string? returnUrl = "/")
    {
        var value = lang == "ar" ? "ar" : "en";
        Response.Cookies.Append(LanguageService.CookieName, value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
        });
        // LocalRedirect() throws InvalidOperationException on a non-local URL rather than
        // safely rejecting it — Url.IsLocalUrl must be checked first, or a crafted
        // ?returnUrl=//evil.example.com/... link crashes this action with a raw stack trace
        // (the redirect itself was never actually vulnerable — LocalRedirect never follows an
        // external URL — but the unhandled throw leaks server internals to an unauthenticated caller).
        var target = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return LocalRedirect(target);
    }
}
