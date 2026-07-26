using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    // Target of Program.cs's app.UseExceptionHandler("/Home/Error") — must stay anonymous
    // since an unhandled exception can occur before/without an authenticated session.
    [AllowAnonymous]
    public IActionResult Error() => View();
}
