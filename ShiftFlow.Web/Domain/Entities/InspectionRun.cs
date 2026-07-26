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
    public string? Notes { get; set; }
    public string? InspectedByUserId { get; set; } public virtual ApplicationUser? InspectedByUser { get; set; }
    public DateTime? InspectedAt { get; set; }
    /// <summary>Set when Outcome=Defective auto-created a work order for this asset.</summary>
    public int? WorkOrderId { get; set; } public virtual WorkOrder? WorkOrder { get; set; }

    public static readonly string[] Outcomes = ["Pending", "OK", "Defective"];
}
