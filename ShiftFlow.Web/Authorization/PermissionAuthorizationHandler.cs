using Microsoft.AspNetCore.Authorization;
using ShiftFlow.Application.Services;

namespace ShiftFlow.Web.Authorization;

/// <summary>
/// Handles all <see cref="PermissionRequirement"/> requirements.
/// Resolves <see cref="IPermissionService"/> from the request scope so it participates
/// correctly in the scoped DI lifetime (IMemoryCache is singleton, DbContext is scoped).
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly IServiceProvider _services;
    private readonly IHttpContextAccessor _http;

    public PermissionAuthorizationHandler(IServiceProvider services, IHttpContextAccessor http)
    {
        _services = services;
        _http = http;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Must be authenticated
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return;
        }

        var userId = context.User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            context.Fail();
            return;
        }

        // Prefer the current request's own scope: this handler is a singleton, so creating a
        // fresh scope per check spun up a second DbContext (and a second change tracker) for
        // every [Authorize(Policy=...)] hit. Fall back to a scope of our own when there is no
        // request (background/hosted-service callers).
        var requestServices = _http.HttpContext?.RequestServices;
        if (requestServices is not null)
        {
            var permissionService = requestServices.GetRequiredService<IPermissionService>();
            if (await permissionService.HasPermissionAsync(userId, requirement.Permission))
                context.Succeed(requirement);
            else
                context.Fail();
            return;
        }

        using var scope = _services.CreateScope();
        var scopedPermissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        if (await scopedPermissionService.HasPermissionAsync(userId, requirement.Permission))
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
