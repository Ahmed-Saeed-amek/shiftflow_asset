using System.Text;

namespace ShiftFlow.Web.Authorization;

/// <summary>
/// Human-friendly display text for permissions, kept separate from PermissionCatalog
/// so the technical permission-name strings (used as policy names, DB keys, and
/// [Authorize(Policy=...)] values everywhere) never have to change just to improve
/// what an admin sees on the RBAC screens.
/// </summary>
public static class PermissionDisplay
{
    /// <summary>A Read/Write pair rendered as one None/Read/Write/Both control instead of two checkboxes.</summary>
    public sealed record PermissionPair(string Label, string ReadPermission, string WritePermission);

    public static readonly IReadOnlyList<PermissionPair> Pairs =
    [
        new("Users",              PermissionCatalog.UserView,             PermissionCatalog.UserManage),
        new("Inspection Orders",  PermissionCatalog.InspectionOrderView,  PermissionCatalog.InspectionOrderManage),
        new("Teams",              PermissionCatalog.TeamView,             PermissionCatalog.TeamManage),
    ];

    /// <summary>Permission names covered by a pair above — used to skip them when rendering standalone checkboxes.</summary>
    public static readonly IReadOnlySet<string> PairedPermissionNames = Pairs
        .SelectMany(p => new[] { p.ReadPermission, p.WritePermission })
        .ToHashSet();

    private static readonly Dictionary<string, string> Titles = new()
    {
        [PermissionCatalog.UserView]                = "View Users",
        [PermissionCatalog.UserManage]               = "Manage Users",
        [PermissionCatalog.InspectionOrderView]      = "View Inspection Orders",
        [PermissionCatalog.InspectionOrderManage]    = "Manage Inspection Orders",
        [PermissionCatalog.InspectionOrderReport]    = "Report Inspection Outcomes",
        [PermissionCatalog.InspectionOrderExport]    = "Export Inspection Orders",
        [PermissionCatalog.TeamView]                 = "View Teams",
        [PermissionCatalog.TeamManage]                = "Manage Teams",
        [PermissionCatalog.AiAssistantUse]           = "Use AI Assistant",
        [PermissionCatalog.AuditLogView]             = "View Audit Log",
        [PermissionCatalog.RbacManage]                = "Manage Roles & Permissions",
        [PermissionCatalog.OrderTypeManage]          = "Manage Order Types",
        [PermissionCatalog.SparePartView]            = "View Spare Parts",
        [PermissionCatalog.SparePartManage]          = "Manage Spare Parts",
        [PermissionCatalog.IsAdmin]                  = "Full Administrator Access",
    };

    /// <summary>Friendly title for a permission name. Falls back to a humanized version of the
    /// dotted technical name for anything not in the table above, so a newly added permission
    /// never renders as a raw/blank string while its title is still being written.</summary>
    public static string GetTitle(string permissionName) =>
        Titles.TryGetValue(permissionName, out var title) ? title : Humanize(permissionName);

    private static string Humanize(string permissionName)
    {
        var sb = new StringBuilder();
        foreach (var part in permissionName.Split('.'))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(part);
        }
        return sb.ToString();
    }
}
