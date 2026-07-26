using Microsoft.AspNetCore.Identity;

namespace ShiftFlow.Domain.Entities;
public class Vendor
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public string? ContactName{get;set;} public string? Phone{get;set;} public string? Email{get;set;} public string? Specialization{get;set;}
    public string Status{get;set;}="Active";
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public virtual ICollection<WorkOrder> WorkOrders{get;set;}=new List<WorkOrder>();

    /// <summary>The one portal login this vendor company uses (one login per Vendor). Null until admin creates one via VendorsController.CreateLogin.</summary>
    public string? UserId{get;set;} public virtual ApplicationUser? User{get;set;}

    public static readonly string[] Statuses = ["Active", "Suspended"];
}
