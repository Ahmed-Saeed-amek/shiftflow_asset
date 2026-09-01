namespace ShiftFlow.Domain.Entities;

/// <summary>Catalog row for a physical spare part. Global stock — no per-warehouse tracking.
/// Compatibility with assets is exact (SparePartAsset join), not by category/model.</summary>
public class SparePart
{
    public int Id{get;set;}
    public string Name{get;set;}=string.Empty;
    public string? NameAr{get;set;}
    public string? Sku{get;set;}
    /// <summary>Current unit cost, used for NEW usage going forward. Historical usage rows snapshot
    /// their own cost onto WorkOrderPart/MaintenanceOrderPart.UnitCostAtUsage instead of reading this
    /// live, so a later price change never rewrites the cost of a fix reported last month.</summary>
    public decimal? UnitCost{get;set;}
    /// <summary>Single global on-hand quantity. Decremented atomically at fix-report time
    /// (SparePartService.TryDecrementStockAsync), adjusted manually via AdjustStock.</summary>
    public int StockQuantity{get;set;}=0;
    /// <summary>When StockQuantity <= this, the part counts toward the Dashboard "Low Stock Parts"
    /// KPI. Null = alerting disabled for this part.</summary>
    public int? ReorderThreshold{get;set;}
    public bool IsActive{get;set;}=true;
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;

    public virtual ICollection<SparePartAsset> AssetLinks{get;set;}=new List<SparePartAsset>();
}

/// <summary>Join table — a part can fit many assets, an asset can take many parts. Same shape as
/// ContractAsset (surrogate key, not composite).</summary>
public class SparePartAsset
{
    public int Id{get;set;}
    public int SparePartId{get;set;} public virtual SparePart? SparePart{get;set;}
    public int AssetId{get;set;} public virtual Asset? Asset{get;set;}
}
