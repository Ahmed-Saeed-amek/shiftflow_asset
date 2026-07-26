using Microsoft.AspNetCore.Identity;
namespace ShiftFlow.Domain.Entities;
public class ApplicationUser : IdentityUser
{
    public string FullName{get;set;}=string.Empty;
    public string? FullNameAr{get;set;}
    public string? EmployeeNumber{get;set;}
    public string? Department{get;set;}
    public string? Specialization{get;set;}
    public string? Phone{get;set;}
    public int? LocationId{get;set;}
    public bool IsActive{get;set;}=true;
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public DateTime? LastLoginDate{get;set;}
    public virtual Location? Location{get;set;}
    public virtual ICollection<AuditLog> AuditLogs{get;set;}=new List<AuditLog>();
    public virtual ICollection<TeamMember> TeamMemberships{get;set;}=new List<TeamMember>();
    public virtual ICollection<InspectionOrder> AssignedInspectionOrders{get;set;}=new List<InspectionOrder>();
}
