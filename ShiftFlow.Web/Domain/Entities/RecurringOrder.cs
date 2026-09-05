namespace ShiftFlow.Domain.Entities;

/// <summary>Admin-defined recurring schedule for a single-asset Order Type — RecurringOrderSchedulerService
/// auto-creates one InspectionOrder or MaintenanceOrder (per OrderType.IsDirectFix) each time a cadence
/// occurrence comes due, mirroring the existing Contract/PreventiveMaintenanceSchedulerService pattern.
/// Scoped to single-asset, single-target types only (v1) — AllowsMultipleAssets types have no fixed
/// per-instance asset list to recur against.</summary>
public class RecurringOrder
{
    public int Id { get; set; }
    public int OrderTypeId { get; set; } public virtual OrderType OrderType { get; set; } = null!;
    public int AssetId { get; set; } public virtual Asset Asset { get; set; } = null!;

    // Exactly one of these two is set — same rule as InspectionOrder/MaintenanceOrder, enforced in
    // RecurringOrdersController rather than a DB CHECK (this table is admin-config, not a hot path).
    public string? AssignedToUserId { get; set; } public virtual ApplicationUser? AssignedToUser { get; set; }
    public int? AssignedToTeamId { get; set; } public virtual Team? AssignedToTeam { get; set; }

    public string Cadence { get; set; } = "Monthly";
    public static readonly string[] Cadences = ["Weekly", "Monthly", "Quarterly", "Semi-Annual", "Annual"];

    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    public string CreatedByUserId { get; set; } = string.Empty; public virtual ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
