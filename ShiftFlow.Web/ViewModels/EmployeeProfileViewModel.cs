namespace ShiftFlow.Web.ViewModels;

public sealed class EmployeeProfileViewModel
{
    public string   Id             { get; init; } = "";
    public string   FullName       { get; init; } = "";
    public string   Email          { get; init; } = "";
    public string   EmployeeNumber { get; init; } = "";
    public string   Department     { get; init; } = "";
    public string   Specialization { get; init; } = "";
    public string   Phone          { get; init; } = "";
    public string   Role           { get; init; } = "";
    public List<string> Roles      { get; init; } = new();
    public bool     IsActive       { get; init; }
    public DateTime CreatedDate    { get; init; }
    public DateTime? LastLogin     { get; init; }

    // Inspection order KPIs
    public int OrdersAssigned   { get; init; }
    public int OrdersOpen       { get; init; }
    public int OrdersInProgress { get; init; }
    public int OrdersDone       { get; init; }
    public int DefectsFound     { get; init; }

    public List<InspectionOrderRow> Orders { get; init; } = new();
    public List<EmpAuditRow>        AuditLog { get; init; } = new();
}

public sealed class EmpAuditRow
{
    public DateTime When       { get; init; }
    public string   Action     { get; init; } = "";
    public string   EntityType { get; init; } = "";
    public string   Details    { get; init; } = "";
}
