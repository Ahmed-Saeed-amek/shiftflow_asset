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
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
