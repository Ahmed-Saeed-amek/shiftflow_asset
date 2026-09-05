using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ShiftFlow.Web.Authorization;

/// <summary>Global enforcement for the "must_change_password" claim (set on account creation and
/// admin password reset — see UsersController.Create/ResetPassword). Previously this was only acted
/// on inside AccountController.Login's own POST handler, which redirects to ChangePassword right
/// after signing in — but that's a one-time suggestion, not a gate: an authenticated request to any
/// other URL (a bookmark, browser back, an already-open tab, or just typing a different address)
/// skipped the check entirely and used the app normally with a never-changed temporary password.
/// This filter re-checks the claim on every authenticated request and redirects to ChangePassword
/// until it's actually changed (ChangePassword itself removes the claim and refreshes the sign-in
/// cookie on success — see AccountController.ChangePassword).</summary>
public sealed class MustChangePasswordFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true
            && user.HasClaim(c => c.Type == "must_change_password")
            && context.ActionDescriptor is ControllerActionDescriptor cad
            && !(cad.ControllerName == "Account" && (cad.ActionName == nameof(Controllers.AccountController.ChangePassword) || cad.ActionName == nameof(Controllers.AccountController.Logout)))
            && cad.ControllerName != "Language")
        {
            context.Result = new RedirectToActionResult(nameof(Controllers.AccountController.ChangePassword), "Account", null);
            return;
        }
        await next();
    }
}
