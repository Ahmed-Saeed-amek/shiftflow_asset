namespace ShiftFlow.Domain.Entities;

/// <summary>
/// Grants a permission to an Identity role.
/// Composite PK: (RoleId, PermissionName).
/// </summary>
public class RolePermission
{
    /// <summary>IdentityRole.Id (GUID string)</summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>Matches Permission.Name, stored as plain string for portability.</summary>
    public string PermissionName { get; set; } = string.Empty;
}
