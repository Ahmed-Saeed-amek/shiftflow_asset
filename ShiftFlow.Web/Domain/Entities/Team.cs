namespace ShiftFlow.Domain.Entities;

/// <summary>A named, reusable group of employees that an InspectionOrder can be assigned to as a
/// whole, instead of a single user.</summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedByUserId { get; set; } = ""; public virtual ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public virtual ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
}

public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; } public virtual Team Team { get; set; } = null!;
    public string UserId { get; set; } = ""; public virtual ApplicationUser User { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
