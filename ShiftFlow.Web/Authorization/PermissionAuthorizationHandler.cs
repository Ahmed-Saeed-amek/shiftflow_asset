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

    public PermissionAuthorizationHandler(IServiceProvider services)
    {
        _services = services;
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

        // Resolve scoped service within a fresh scope to avoid captive-dependency problems
        using var scope = _services.CreateScope();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        if (await permissionService.HasPermissionAsync(userId, requirement.Permission))
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
