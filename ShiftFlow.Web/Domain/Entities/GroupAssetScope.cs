namespace ShiftFlow.Domain.Entities;

/// <summary>Restricts which assets every member of a Group can see, assigned by a manager
/// (Asset.ScopeManage) — same shape as UserAssetScope, one level up. At most one row per group —
/// unique index on GroupId. Each dimension (Zone/LocationCategory/Category) is independently
/// optional; whichever are set combine with AND to narrow further. A member's own UserAssetScope,
/// if they have one, always overrides whatever their group's scope says (see
/// AssetScopeService.GetEffectiveScopeAsync) — a group scope only applies to a member with no
/// individual scope of their own.</summary>
public class GroupAssetScope
{
    public int Id{get;set;}
    public int GroupId{get;set;} public virtual Group? Group{get;set;}
    public int? ZoneId{get;set;} public virtual Zone? Zone{get;set;}
    public int? LocationCategoryId{get;set;} public virtual LocationCategory? LocationCategory{get;set;}
    public int? CategoryId{get;set;} public virtual AssetCategory? Category{get;set;}
}
