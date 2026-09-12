namespace ShiftFlow.Application.Services;

/// <summary>WorkOrder.Stage values. <see cref="ShiftFlow.Domain.Entities.WorkOrder.Stages"/> and
/// AllDisplayStages stay the display/filter lists; these are the comparison constants.</summary>
public static class WorkOrderStages
{
    public const string Draft = "Draft";
    public const string New = "New";
    public const string Rejected = "Rejected";
    public const string SentToVendor = "Sent to Vendor";
    public const string Blocked = "Blocked";
    public const string FixedPendingConfirmation = "Fixed - Pending Confirmation";
    public const string Closed = "Closed";

    /// <summary>Stages that still mean "this asset has open work" — the single list previously
    /// re-declared in both WorkOrderService and MaintenanceOrderService.</summary>
    public static readonly string[] Open = [Draft, New, SentToVendor, Blocked, FixedPendingConfirmation];
}

/// <summary>Status values shared by InspectionOrder and MaintenanceOrder.</summary>
public static class OrderStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string PendingApproval = "PendingApproval";
    public const string Done = "Done";
    public const string Cancelled = "Cancelled";
}

/// <summary>Asset.Status values — mirrors <see cref="ShiftFlow.Domain.Entities.Asset.Statuses"/>.</summary>
public static class AssetStatuses
{
    public const string Working = "Working";
    public const string Defective = "Defective";
    public const string Maintenance = "Maintenance";
    public const string Retired = "Retired";
}

/// <summary>InspectionRunAsset.Outcome values.</summary>
public static class InspectionOutcomes
{
    public const string Pending = "Pending";
    public const string OK = "OK";
    public const string Defective = "Defective";
}

/// <summary>OrderType.AssignmentMode values.</summary>
public static class AssignmentModes
{
    public const string EmployeeOnly = "EmployeeOnly";
    public const string GroupOnly = "GroupOnly";
    public const string Either = "Either";
}

/// <summary>Order-number prefixes owned by code rather than the OrderType catalog.</summary>
public static class OrderNumberPrefixes
{
    public const string WorkOrder = "WO";
    public const string Maintenance = "MO";
    public const string Inspection = "INS";
}
