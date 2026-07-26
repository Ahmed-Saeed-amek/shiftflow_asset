namespace ShiftFlow.Domain.Entities;
public class Location
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    public string Code{get;set;}=string.Empty; public string? Address{get;set;} public string? City{get;set;}
    public string Status{get;set;}="Active"; public string? Notes{get;set;}
    public double? Latitude{get;set;} public double? Longitude{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
}