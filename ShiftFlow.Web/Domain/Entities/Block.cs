namespace ShiftFlow.Domain.Entities;

/// <summary>Third level of the Asset Location hierarchy (Governorate → Area → Block → Zone) — fixed, seeded numbering per Area (see DbSeeder), not admin-editable, like Area.</summary>
public class Block
{
    public int Id{get;set;} public int Number{get;set;}
    public int AreaId{get;set;} public virtual Area? Area{get;set;}
    public virtual ICollection<Zone> Zones{get;set;}=new List<Zone>();
}
