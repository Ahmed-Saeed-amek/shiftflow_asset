using System.ComponentModel.DataAnnotations;

namespace ShiftFlow.Web.ViewModels;

public class GroupCreateVm
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public List<string> MemberUserIds { get; set; } = new();
}

public class GroupEditVm
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<string> MemberUserIds { get; set; } = new();
    /// <summary>Snapshot of the member ids the edit form was loaded with — carried as hidden fields
    /// so SetMembersAsync can detect a concurrent edit (someone else added/removed a member between
    /// this page loading and this submit) instead of blindly diffing against whatever is live in the
    /// DB, which would silently discard the other editor's change.</summary>
    public List<string> OriginalMemberUserIds { get; set; } = new();
}

/// <summary>A named (not anonymous) shape for the Edit page's ViewBag.CurrentMembers — anonymous
/// types are internal, so Razor's generated view assembly can't dynamic-bind their members.</summary>
public class GroupMemberChip
{
    public string UserId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
