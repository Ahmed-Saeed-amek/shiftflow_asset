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
}
