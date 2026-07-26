namespace ShiftFlow.Domain.Entities;

/// <summary>Leaf of the Asset Location hierarchy (Governorate → Area → Block → Zone). Assets attach here. Coordinates and address are optional.</summary>
public class Zone
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public int BlockId{get;set;} public virtual Block? Block{get;set;}
    public string? Address{get;set;}
    public double? Latitude{get;set;} public double? Longitude{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public virtual ICollection<Asset> Assets{get;set;}=new List<Asset>();
}
