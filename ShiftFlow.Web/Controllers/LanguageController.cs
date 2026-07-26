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
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    }
}
