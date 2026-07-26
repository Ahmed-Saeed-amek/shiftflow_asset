using Microsoft.AspNetCore.Authorization;

namespace ShiftFlow.Web.Authorization;

/// <summary>
/// Carries the permission name into the authorization handler.
/// One requirement instance is created per policy at startup.
/// </summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
