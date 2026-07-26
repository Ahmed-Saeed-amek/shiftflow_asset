namespace ShiftFlow.Domain.Entities;

/// <summary>Second level of the Asset Location hierarchy (Governorate → Area → Block → Zone). Admin-managed.</summary>
public class Area
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public int GovernorateId{get;set;} public virtual Governorate? Governorate{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public virtual ICollection<Block> Blocks{get;set;}=new List<Block>();
}
