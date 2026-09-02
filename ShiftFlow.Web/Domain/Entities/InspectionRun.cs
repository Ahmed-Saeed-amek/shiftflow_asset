namespace ShiftFlow.Domain.Entities;

/// <summary>The per-asset checklist attached to an InspectionOrder. One run per order.</summary>
public class InspectionRun
{
    public int Id { get; set; }
    public int InspectionOrderId { get; set; } public virtual InspectionOrder InspectionOrder { get; set; } = null!;
    /// <summary>Set if this run was created from a Zone snapshot; null if the assets were hand-picked.</summary>
    public int? ZoneId { get; set; } public virtual Zone? Zone { get; set; }

    public virtual ICollection<InspectionRunAsset> Items { get; set; } = new List<InspectionRunAsset>();
}

/// <summary>One asset's checklist entry within an InspectionRun.</summary>
public class InspectionRunAsset
{
    public int Id { get; set; }
    public int InspectionRunId { get; set; } public virtual InspectionRun InspectionRun { get; set; } = null!;
    public int AssetId { get; set; } public virtual Asset Asset { get; set; } = null!;
    public string Outcome { get; set; } = "Pending";
    public string? InspectedByUserId { get; set; } public virtual ApplicationUser? InspectedByUser { get; set; }
    public DateTime? InspectedAt { get; set; }
    /// <summary>Set when Outcome=Defective auto-created a work order for this asset.</summary>
    public int? WorkOrderId { get; set; } public virtual WorkOrder? WorkOrder { get; set; }

    public virtual ICollection<InspectionItemMaintenanceAction> MaintenanceActions { get; set; } = new List<InspectionItemMaintenanceAction>();

    public static readonly string[] Outcomes = ["Pending", "OK", "Defective"];
}

/// <summary>Admin-manageable catalog of maintenance actions an employee can log against an
/// inspected asset (e.g. "Cleaned Outer Unit", "Replaced Filter") — a log entry only, never gates
/// the OK/Defective outcome. Optionally scoped to an AssetCategory or subcategory (null = applies
/// everywhere) — a subcategory-scoped asset also offers its parent category's types, same
/// inheritance rule as AssetActionType.</summary>
public class MaintenanceActionType
{
    public int Id { get; set; }
    public int? CategoryId { get; set; } public virtual AssetCategory? Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>One maintenance action logged against one inspection checklist item — an employee may
/// log several per asset (e.g. cleaned + replaced filter).</summary>
public class InspectionItemMaintenanceAction
{
    public int Id { get; set; }
    public int InspectionRunAssetId { get; set; } public virtual InspectionRunAsset InspectionRunAsset { get; set; } = null!;
    public int MaintenanceActionTypeId { get; set; } public virtual MaintenanceActionType MaintenanceActionType { get; set; } = null!;
}
