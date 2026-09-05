namespace ShiftFlow.Domain.Entities;

/// <summary>Admin-managed catalog of order types shared by Inspection Orders and Maintenance
/// Orders. Replaces the old hardcoded InspectionOrder.OrderKind ("Inspection"/"QuickCheck") string
/// and gives Maintenance Orders a type concept for the first time — a type's RequiresVendor flag
/// decides whether a request routes into the WorkOrder vendor pipeline instead of staying a plain,
/// vendor-free order. Every active type is selectable from both Inspection Order and Maintenance
/// Order Create forms — there's no restriction on which order kind can use which type.</summary>
public class OrderType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameAr { get; set; }
    /// <summary>Order-number prefix for this type, e.g. "INS", "QC", "MO".</summary>
    public string Prefix { get; set; } = "INS";
    /// <summary>Whether Inspection Order items of this type require an Action Type + Cause when marked Defective (was OrderKind == "Inspection").</summary>
    public bool TracksDefectOutcome { get; set; }
    /// <summary>Whether a request of this type must route through the WorkOrder vendor pipeline rather than resolve as a simple in-house fix.</summary>
    public bool RequiresVendor { get; set; }
    /// <summary>True = maintenance-style: assign one employee to fix one specific asset directly,
    /// no survey, straight to a cost/parts/completion-date fix report (today's Maintenance Order
    /// behavior). False = inspection-style: survey one or more assets, record a per-asset
    /// Pending/OK/Defective outcome, Defective spawns a Work Order (today's Inspection Order
    /// behavior). Drives which field-set - and which of IInspectionOrderService/
    /// IMaintenanceOrderService - the unified Orders/Create screen uses.</summary>
    public bool IsDirectFix { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    /// <summary>Hex badge color, auto-assigned at creation from <see cref="OrderTypeColors.Palette"/>
    /// (round-robin, no repeats until the 20-color palette is exhausted) — never user-editable, so
    /// every type gets a distinct identity on the Orders list without an admin having to pick one.</summary>
    public string Color { get; set; } = "#6c757d";
}

/// <summary>A fixed, hand-picked 20-color categorical palette (good contrast on a white badge
/// background, distinguishable from each other) that OrderType.Color is auto-assigned from.</summary>
public static class OrderTypeColors
{
    public static readonly IReadOnlyList<string> Palette =
    [
        "#4C6EF5", "#F76707", "#2F9E44", "#E03131", "#9C36B5",
        "#0C8599", "#F08C00", "#5C940D", "#C2255C", "#1971C2",
        "#E8590C", "#37B24D", "#862E9C", "#1098AD", "#F59F00",
        "#495057", "#D6336C", "#3B5BDB", "#099268", "#A61E4D",
    ];

    /// <summary>First palette color not already used by an active OrderType; once all 20 are
    /// taken, cycles back round-robin (by count) rather than erroring — a 21st type just shares a
    /// color with the 1st instead of blocking creation.</summary>
    public static string NextColor(IEnumerable<string> usedColors)
    {
        var used = new HashSet<string>(usedColors, StringComparer.OrdinalIgnoreCase);
        var next = Palette.FirstOrDefault(c => !used.Contains(c));
        return next ?? Palette[used.Count % Palette.Count];
    }
}
