using Microsoft.AspNetCore.Identity;
namespace ShiftFlow.Domain.Entities;
public class ApplicationRole : IdentityRole
{
    public string? NameAr{get;set;}

    public ApplicationRole() : base() { }
    public ApplicationRole(string roleName) : base(roleName) { }
}
