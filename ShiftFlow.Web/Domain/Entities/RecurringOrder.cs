namespace ShiftFlow.Domain.Entities;

/// <summary>Admin-defined recurring schedule for any active Order Type, covering one or more assets —
/// RecurringOrderSchedulerService auto-creates, per (schedule, asset, due date), one InspectionOrder,
/// MaintenanceOrder (per OrderType.IsDirectFix), or — for a RequiresVendor type — a vendor-routed
/// WorkOrder, each time a cadence occurrence comes due. Mirrors Contract/
/// PreventiveMaintenanceSchedulerService's shape (AssetLinks join table, one generated order per
/// linked asset per occurrence) generalized to any order type, not just vendor PM.</summary>
public class RecurringOrder
{
    public int Id { get; set; }
    public int OrderTypeId { get; set; } public virtual OrderType OrderType { get; set; } = null!;

    public virtual ICollection<RecurringOrderAsset> AssetLinks { get; set; } = new List<RecurringOrderAsset>();

    // Exactly one of these two is set — same rule as InspectionOrder/MaintenanceOrder, enforced in
    // RecurringOrderService rather than a DB CHECK (this table is admin-config, not a hot path) except
    // for the "exactly one" shape itself, which IS a DB CHECK below.
    public string? AssignedToUserId { get; set; } public virtual ApplicationUser? AssignedToUser { get; set; }
    public int? AssignedToTeamId { get; set; } public virtual Team? AssignedToTeam { get; set; }

    /// <summary>Required, and AssignedToTeamId must be null, when OrderType.RequiresVendor is true —
    /// same "vendor-routed work orders need an individual employee, not a team" rule
    /// OrdersController.Create already enforces for a manual RequiresVendor order. Null for every
    /// other order type.</summary>
    public int? VendorId { get; set; } public virtual Vendor? Vendor { get; set; }

    public string Cadence { get; set; } = "Monthly";
    public static readonly string[] Cadences = ["Weekly", "Monthly", "Quarterly", "Semi-Annual", "Annual"];

    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    public string CreatedByUserId { get; set; } = string.Empty; public virtual ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>Join table — a schedule can cover many assets, an asset can be covered by many schedules. Mirrors ContractAsset.</summary>
public class RecurringOrderAsset
{
    public int Id { get; set; }
    public int RecurringOrderId { get; set; } public virtual RecurringOrder? RecurringOrder { get; set; }
    public int AssetId { get; set; } public virtual Asset? Asset { get; set; }
}
