namespace ShiftFlow.Domain.Entities;

/// <summary>Fixed, seeded 3-row list — Main Locations / Side Locations / Governmental Locations. Replaces the old Governorate→Area→Block hierarchy as the top level under Zone. Not admin-editable, like Governorate was.</summary>
public class LocationCategory
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public virtual ICollection<Zone> Zones{get;set;}=new List<Zone>();
}
