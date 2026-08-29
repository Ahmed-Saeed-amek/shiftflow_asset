using Microsoft.AspNetCore.Identity;

namespace ShiftFlow.Domain.Entities;

/// <summary>Restricts which assets a specific employee can see, assigned by a manager (Asset.ScopeManage).
/// At most one row per user — unique index on UserId. Each dimension (Zone/LocationCategory/Category) is
/// independently optional; whichever are set combine with AND to narrow further (e.g. Zone AND Category
/// together). No row, or a row with every dimension null, means unrestricted (sees everything).</summary>
public class UserAssetScope
{
    public int Id{get;set;}
    public string UserId{get;set;}=string.Empty; public virtual ApplicationUser? User{get;set;}
    public int? ZoneId{get;set;} public virtual Zone? Zone{get;set;}
    public int? LocationCategoryId{get;set;} public virtual LocationCategory? LocationCategory{get;set;}
    public int? CategoryId{get;set;} public virtual AssetCategory? Category{get;set;}
}
