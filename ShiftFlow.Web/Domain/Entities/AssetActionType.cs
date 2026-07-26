namespace ShiftFlow.Domain.Entities;

/// <summary>An action an employee can report against an asset (e.g. "Report Failure", "Request Maintenance"), scoped to one AssetCategory. Admin-managed under Asset Categories → Manage Actions.</summary>
public class AssetActionType
{
    public int Id{get;set;}
    public int CategoryId{get;set;} public virtual AssetCategory? Category{get;set;}
    public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public bool IsActive{get;set;}=true;

    public virtual ICollection<AssetActionCause> Causes{get;set;}=new List<AssetActionCause>();
}

/// <summary>A cause option under a specific AssetActionType (e.g. under "Report Failure" for HVAC: "Compressor Failure", "Refrigerant Leak").</summary>
public class AssetActionCause
{
    public int Id{get;set;}
    public int ActionTypeId{get;set;} public virtual AssetActionType? ActionType{get;set;}
    public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public bool IsActive{get;set;}=true;
}
