using Microsoft.AspNetCore.Identity;

namespace ShiftFlow.Domain.Entities;

/// <summary>Statuses an asset can be in. Kept as free-text (like Location.Status) rather than an enum for easy localization/display.</summary>
public class Asset
{
    public int Id{get;set;}
    public string AssetTag{get;set;}=string.Empty; public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public int CategoryId{get;set;} public virtual AssetCategory? Category{get;set;}
    public int ZoneId{get;set;} public virtual Zone? Zone{get;set;}
    public string? Model{get;set;} public string? SerialNumber{get;set;} public string? Manufacturer{get;set;}
    /// <summary>Manufacturer part/catalog number — what a vendor orders replacement parts by, distinct from SerialNumber (the unique ID of this specific physical unit).</summary>
    public string? Sku{get;set;}
    public string Status{get;set;}="Working";
    public string? AssignedToUserId{get;set;} public virtual ApplicationUser? AssignedToUser{get;set;}
    public DateTime? PurchaseDate{get;set;} public DateTime? WarrantyExpiry{get;set;}
    public string? Notes{get;set;}
    public string CreatedByUserId{get;set;}=string.Empty; public virtual ApplicationUser? CreatedByUser{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public string? UpdatedByUserId{get;set;} public virtual ApplicationUser? UpdatedByUser{get;set;}
    public DateTime? UpdatedDate{get;set;}

    public virtual ICollection<WorkOrder> WorkOrders{get;set;}=new List<WorkOrder>();
    public virtual ICollection<ContractAsset> ContractLinks{get;set;}=new List<ContractAsset>();

    public static readonly string[] Statuses = ["Working", "Defective", "Maintenance", "Retired"];
}
