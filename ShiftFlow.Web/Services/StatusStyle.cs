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
        // Both spellings of every multi-word status map to the same colour. The raw enum-ish
        // form ("InProgress") comes off entity/enum values, the spaced form ("In Progress")
        // off display strings and filter dropdowns — before this, whichever form a given view
        // happened to hold decided whether the chip was coloured or fell back to grey.
        ["Pending Approval"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["InProgress"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["UnderMaintenance"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["InStorage"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["InDelivery"] = "bg-primary-subtle text-primary-emphasis border-primary-subtle",
        ["SparePart"] = "bg-secondary-subtle text-secondary-emphasis border-secondary-subtle",
        ["InStock"] = "bg-success-subtle text-success-emphasis border-success-subtle",
        ["LowStock"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle",
        ["OutOfStock"] = "bg-danger-subtle text-danger-emphasis border-danger-subtle",
        ["PreventiveMaintenance"] = "bg-dark-subtle text-dark-emphasis border-dark-subtle",
        ["SentToVendor"] = "bg-info-subtle text-info-emphasis border-info-subtle",
        ["Quick Check"] = "bg-info-subtle text-info-emphasis border-info-subtle",
    };

    /// <summary>
    /// Raw status spellings that need a display label other than plain
    /// "split on capitals" — the hyphenated/compound ones.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LabelOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["FixedPendingConfirmation"] = "Fixed - Pending Confirmation",
        ["OutOfStock"] = "Out of Stock",
        ["OK"] = "OK",
        ["HR"] = "HR",
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
        "Draft", "Pending Approval", "Done", "Cancelled", "Overdue", "Low Stock", "Active", "Inactive", "Expired", "Retired", "Pending", "OK",
    ];

    /// <summary>Badge classes for a status name; <see cref="Default"/> when unmapped or null.
    /// Both spellings of a multi-word status ("InProgress" and "In Progress") resolve to the
    /// same colour.</summary>
    public static string For(string? status) =>
        status is not null && Map.TryGetValue(status, out var cls) ? cls : Default;

    /// <summary>
    /// THE canonical display label for a status — call this everywhere a status is rendered
    /// (chips, filter dropdowns, exports), then pass the result through Loc.T for Arabic:
    /// <c>Loc.T(StatusStyle.Label(row.Status))</c>. Returns the spaced English form, so a raw
    /// "PendingApproval" off an entity and an already-spaced "Pending Approval" off a filter
    /// both render — and translate — identically.
    /// </summary>
    public static string Label(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return string.Empty;
        var s = status.Trim();
        if (LabelOverrides.TryGetValue(s, out var mapped)) return mapped;
        if (s.Contains(' ')) return s;

        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            // Split before a capital that starts a new word, keeping acronym runs together.
            if (i > 0 && char.IsUpper(c) &&
                (!char.IsUpper(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
