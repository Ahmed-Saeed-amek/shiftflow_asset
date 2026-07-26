namespace ShiftFlow.Domain.Entities;

/// <summary>Fixed, seeded list of the 6 Kuwait governorates — not user-editable.</summary>
public class Governorate
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public double CenterLat{get;set;} public double CenterLng{get;set;}
    public virtual ICollection<Area> Areas{get;set;}=new List<Area>();
}
