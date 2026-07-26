namespace ShiftFlow.Domain.Entities;

/// <summary>Contracts a vendor holds against one or more assets. Asset.Vendor is derived from this (see ContractService.GetDerivedVendorAsync), not freely picked.</summary>
public class Contract
{
    public int Id{get;set;}
    public int VendorId{get;set;} public virtual Vendor? Vendor{get;set;}
    public string ContractType{get;set;}="Warranty";
    public string? ContractNumber{get;set;}
    public DateTime StartDate{get;set;}=DateTime.UtcNow;
    public DateTime? EndDate{get;set;}
    public decimal? Cost{get;set;}
    public string? Notes{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;

    /// <summary>Recurrence cadence for "Preventive Maintenance" contracts (one of PmCadences). Null for every other contract type.</summary>
    public string? PmCadence{get;set;}

    public virtual ICollection<ContractAsset> AssetLinks{get;set;}=new List<ContractAsset>();

    public static readonly string[] ContractTypes = ["Purchase", "Warranty", "Service", "Insurance", "Preventive Maintenance"];
    public static readonly string[] PmCadences = ["Weekly", "Monthly", "Quarterly", "Semi-Annual", "Annual"];

    /// <summary>Every due date from startDate to endDate (inclusive) stepping by cadence. The first occurrence
    /// is startDate itself. Used identically by the background generator and the Details-page schedule view
    /// so both always agree on what's due.</summary>
    public static List<DateTime> ComputeOccurrenceDueDates(DateTime startDate, DateTime endDate, string cadence)
    {
        var dates = new List<DateTime>();
        var current = startDate.Date;
        var end = endDate.Date;
        while (current <= end)
        {
            dates.Add(current);
            current = cadence switch
            {
                "Weekly" => current.AddDays(7),
                "Monthly" => current.AddMonths(1),
                "Quarterly" => current.AddMonths(3),
                "Semi-Annual" => current.AddMonths(6),
                "Annual" => current.AddYears(1),
                _ => throw new InvalidOperationException($"Unknown PM cadence: {cadence}"),
            };
        }
        return dates;
    }
}

/// <summary>Join table — a contract can cover many assets, an asset can be covered by many contracts.</summary>
public class ContractAsset
{
    public int Id{get;set;}
    public int ContractId{get;set;} public virtual Contract? Contract{get;set;}
    public int AssetId{get;set;} public virtual Asset? Asset{get;set;}
}
