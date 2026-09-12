namespace ShiftFlow.Web.Services;

/// <summary>
/// Status/priority/stage -> Bootstrap subtle-badge class map, shared by the _StatusBadge partial
/// (and usable from any view or tag helper that needs the same colour for a status outside a badge).
/// Lives here rather than inline in the partial so the map is built once at type-init instead of
/// on every single render of every row of every list page.
/// </summary>
public static class StatusStyle
{
    /// <summary>Fallback for an unknown status — neutral grey, same as before.</summary>
    public const string Default = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle";

    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Active"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Inactive"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Planned"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["Completed"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Done"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Cancelled"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Pending"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Approved"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Rejected"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["New"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["Assigned"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["In Progress"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["Open"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Dispatched"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["Resolved"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Closed"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Escalated"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Under Maintenance"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Faulty"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Decommissioned"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["In Storage"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["In Delivery"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["Spare Part"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["In Stock"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Low Stock"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Out of Stock"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Discontinued"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Low"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Medium"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["High"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Critical"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Emergency"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Urgent"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Normal"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Draft"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Submitted"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["Overdue"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Working"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Defective"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Maintenance"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Retired"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Suspended"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["Acknowledged"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["Expired"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["Purchase"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["Warranty"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["Service"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["Insurance"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Preventive Maintenance"] = "bg-dark-subtle text-dark-emphasis border-dark-subtle",
        ["Sent to Vendor"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["PendingApproval"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Fixed - Pending Confirmation"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["Blocked"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["OK"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["QuickCheck"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["Inspection"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
    };

    /// <summary>
    /// Statuses/stages that JS needs translated copies of (zone-map.js popups). Emitted into
    /// window.i18n.status by _Layout — keep it to what the client actually renders rather than
    /// shipping the whole map on every page.
    /// </summary>
    public static readonly string[] JsLabelNames =
    [
        "Working", "Maintenance", "Defective",
        "New", "Assigned", "In Progress", "Open", "Dispatched", "Resolved", "Closed",
        "Sent to Vendor", "Blocked", "Fixed - Pending Confirmation",
    ];

    /// <summary>Badge classes for a status name; <see cref="Default"/> when unmapped or null.</summary>
    public static string For(string? status) =>
        status is not null && Map.TryGetValue(status, out var cls) ? cls : Default;
}
