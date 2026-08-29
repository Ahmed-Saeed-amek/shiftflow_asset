using System.ComponentModel.DataAnnotations;

namespace ShiftFlow.Web.ViewModels;

public class TeamCreateVm
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public List<string> MemberUserIds { get; set; } = new();
}

public class TeamEditVm
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<string> MemberUserIds { get; set; } = new();
}

/// <summary>A named (not anonymous) shape for the Edit page's ViewBag.CurrentMembers — anonymous
/// types are internal, so Razor's generated view assembly can't dynamic-bind their members.</summary>
public class TeamMemberChip
{
    public string UserId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
