namespace ShiftFlow.Domain.Entities;

/// <summary>
/// Per-user permission override.
/// IsGranted = true  → explicit Allow  (overrides role inheritance)
/// IsGranted = false → explicit Deny   (highest priority — blocks even role grants)
/// Composite PK: (UserId, PermissionName).
/// </summary>
public class UserPermission
{
    /// <summary>ApplicationUser.Id (GUID string)</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Matches Permission.Name</summary>
    public string PermissionName { get; set; } = string.Empty;

    /// <summary>true = Allow override, false = Deny override</summary>
    public bool IsGranted { get; set; }
}
