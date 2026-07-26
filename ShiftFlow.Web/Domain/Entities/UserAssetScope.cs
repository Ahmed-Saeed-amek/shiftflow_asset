using Microsoft.AspNetCore.Identity;

namespace ShiftFlow.Domain.Entities;

/// <summary>Restricts which assets a specific employee can see, assigned by a manager (Asset.ScopeManage). At most one row per user — unique index on UserId. No row = unrestricted (sees everything, same as today).</summary>
public class UserAssetScope
{
    public int Id{get;set;}
    public string UserId{get;set;}=string.Empty; public virtual ApplicationUser? User{get;set;}
    public string ScopeType{get;set;}=string.Empty;
    public int ScopeValueId{get;set;}

    public static readonly string[] ScopeTypes = ["Zone", "Area", "Category"];
}
