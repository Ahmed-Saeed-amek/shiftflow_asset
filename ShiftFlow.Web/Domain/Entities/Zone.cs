namespace ShiftFlow.Domain.Entities;

/// <summary>Leaf of the Asset Location model — belongs to one of the 3 fixed LocationCategories (Main/Side/Governmental). Assets attach here. Coordinates and address are optional.</summary>
public class Zone
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public int LocationCategoryId{get;set;} public virtual LocationCategory? LocationCategory{get;set;}
    public string? Address{get;set;}
    public double? Latitude{get;set;} public double? Longitude{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public virtual ICollection<Asset> Assets{get;set;}=new List<Asset>();
}
