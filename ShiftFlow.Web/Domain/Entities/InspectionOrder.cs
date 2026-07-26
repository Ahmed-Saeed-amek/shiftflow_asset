namespace ShiftFlow.Domain.Entities;

/// <summary>An admin-created assignment: inspect a set of assets (picked directly or via a Zone
/// snapshot) either individually or as a Team. Replaces the old ShiftTask+InspectionRun pairing
/// with a standalone header not tied to any shift/roster machinery.</summary>
public class InspectionOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";      // "INS-{year}-{seq:D4}", same pattern as WorkOrder.WorkOrderNumber
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    // Exactly one of these two is set — enforced in the service layer + a DB CHECK constraint.
    public string? AssignedToUserId { get; set; } public virtual ApplicationUser? AssignedToUser { get; set; }
    public int? AssignedToTeamId { get; set; } public virtual Team? AssignedToTeam { get; set; }

    public string CreatedByUserId { get; set; } = ""; public virtual ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>Open (nothing reported) -> InProgress (first item reported) -> Done (every item non-Pending)</summary>
    public string Status { get; set; } = "Open";
    public static readonly string[] Statuses = ["Open", "InProgress", "Done"];

    public virtual InspectionRun? InspectionRun { get; set; }
}
