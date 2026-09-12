namespace ShiftFlow.Domain.Entities;

/// <summary>One counter row per (Prefix, Year) — the single atomically-claimed source of order
/// numbers for Work Orders, Inspection Orders and Maintenance Orders. Replaces the three
/// "load every number for the year and take the max" helpers each service used to carry.
/// Requires a unique index on (Prefix, Year) — see NOTES-orders.md for the DbContext mapping.</summary>
public class OrderNumberSequence
{
    public int Id { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int Year { get; set; }
    /// <summary>Highest sequence value handed out so far for this prefix/year.</summary>
    public int LastValue { get; set; }
}
