using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using ShiftFlow.Application.Services;

namespace ShiftFlow.Web.Authorization;

/// <summary>
/// Bridges the IsAdmin permission into the role system: a user who effectively holds
/// IsAdmin (role-granted or user-override) gets a synthetic "Admin" role claim added to
/// their principal for this request. This makes every existing [Authorize(Roles="Admin")]
/// attribute and raw User.IsInRole("Admin")/IsPrivileged()/IsFieldWorker() check treat them
/// as Admin, without touching any of those call sites. Runs per-request and is never
/// persisted to the auth cookie, so revoking IsAdmin takes effect on the very next request.
/// </summary>
public sealed class AdminClaimsTransformation : IClaimsTransformation
{
    private readonly IPermissionService _permissions;

    public AdminClaimsTransformation(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.IsInRole("Admin"))
            return principal;

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return principal;

        if (!await _permissions.HasPermissionAsync(userId, PermissionCatalog.IsAdmin))
            return principal;

        var identity = (ClaimsIdentity)principal.Identity;
        identity.AddClaim(new Claim(identity.RoleClaimType, "Admin"));
        return principal;
    }
}
