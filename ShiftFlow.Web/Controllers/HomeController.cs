using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    // Target of both Program.cs's app.UseExceptionHandler("/Home/Error") (unhandled 500s) and
    // app.UseStatusCodePagesWithReExecute("/Home/Error/{0}") (404s, 403s, etc. with no body of
    // their own) — must stay anonymous since either can occur before/without an authenticated
    // session (e.g. a bad link hit while logged out).
    [AllowAnonymous]
    public IActionResult Error(int? statusCode = null)
    {
        ViewBag.StatusCode = statusCode;
        return View();
    }
}
