using Microsoft.AspNetCore.Http;
using ShiftFlow.Domain.Entities;
namespace ShiftFlow.Application.Services;

public interface IPermissionService
{
    /// <summary>
    /// Evaluates: Deny override > Allow override > Role permissions.
    /// Result is cached per user for 5 minutes.
    /// </summary>
    Task<bool> HasPermissionAsync(string userId, string permission);

    /// <summary>Returns all permissions effectively granted to the user (after deny filtering).</summary>
    Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(string userId);

    /// <summary>Evicts the permission cache for the given user.</summary>
    Task InvalidateCacheAsync(string userId);

    // --- Admin operations ---
    Task<IReadOnlyList<Permission>> GetAllPermissionsAsync();
    Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(string roleId);
    Task AssignRolePermissionAsync(string roleId, string permissionName);
    Task RemoveRolePermissionAsync(string roleId, string permissionName);
    Task<IReadOnlyList<UserPermission>> GetUserPermissionOverridesAsync(string userId);
    Task SetUserPermissionOverrideAsync(string userId, string permissionName, bool isGranted);
    Task RemoveUserPermissionOverrideAsync(string userId, string permissionName);
}

public interface IAuditService{Task LogAsync(string action,string entityType,string? entityId,string userId,string? oldValue=null,string? newValue=null,string? details=null);}

public interface IDashboardService{Task<DashboardKpis> GetKpisAsync(string? userId=null,string? userRole=null);}
public class DashboardKpis{public int TotalEngineers{get;set;}public int OpenInspectionOrders{get;set;}public int InspectionOrdersOverdue{get;set;}public int ActiveTeams{get;set;}public int TotalAssets{get;set;}public int DefectiveAssets{get;set;}public int OpenWorkOrders{get;set;}public int CriticalOpenWorkOrders{get;set;}}

// ── Inspection Orders / Teams ────────────────────────────────────────────────
public interface IInspectionOrderService
{
    Task<InspectionOrder> CreateAsync(string title, string? description, string? assignedToUserId, int? assignedToTeamId,
        int? zoneId, List<int>? assetIds, DateTime? dueDate, string createdByUserId);
    Task<InspectionOrder?> GetByIdAsync(int id);
    Task<List<InspectionOrder>> GetMyOrdersAsync(string userId, bool includeDone = false);
    Task<List<InspectionOrder>> GetAllAsync(string? status, string? search);
    Task UpdateInspectionItemAsync(int itemId, string outcome, string? notes, int? workOrderId, string updatedByUserId);
    Task CancelAsync(int orderId, string userId);
    Task<byte[]> ExportToExcelAsync();
}

public interface ITeamService
{
    Task<List<Team>> GetAllAsync(bool includeInactive = false);
    Task<Team?> GetByIdAsync(int id);
    Task<Team> CreateAsync(string name, string? nameAr, string? description, List<string> initialMemberUserIds, string userId);
    Task UpdateAsync(int teamId, string name, string? nameAr, string? description, string userId);
    Task SetActiveAsync(int teamId, bool isActive, string userId);
    Task AddMemberAsync(int teamId, string userId, string actingUserId);
    Task RemoveMemberAsync(int teamId, string userId, string actingUserId);
    Task<bool> IsMemberAsync(int teamId, string userId);
}

// ── Asset Management ─────────────────────────────────────────────────────────
public interface IAssetService
{
    Task<Asset> CreateAsync(Asset asset, string userId);
    Task UpdateAsync(Asset asset, string userId);
    Task DeleteAsync(int id, string userId);
    Task<byte[]> ExportToExcelAsync();
    Task<byte[]> ExportToPdfAsync();
}

public interface IVendorService
{
    Task<Vendor> CreateAsync(Vendor vendor, string userId);
    Task UpdateAsync(Vendor vendor, string userId);
}

public interface IContractService
{
    Task<Contract> CreateAsync(Contract contract, List<int> assetIds, string userId);
    Task UpdateAsync(Contract contract, List<int> assetIds, string userId);
    /// <summary>Picks the vendor from the asset's most recently-started contract that's currently active (EndDate null or in the future); falls back to the most recent contract overall; null if the asset has no contracts.</summary>
    Task<Vendor?> GetDerivedVendorAsync(int assetId);
    Task<Dictionary<int, Vendor?>> GetDerivedVendorsAsync(IEnumerable<int> assetIds);
    /// <summary>Active (EndDate null or in the future) Service-type contracts covering this asset — the candidate pool a Work Order's vendor must be resolved from. Empty means the asset isn't covered by any Service contract yet.</summary>
    Task<List<ServiceVendorCandidate>> GetActiveServiceVendorsAsync(int assetId);
    /// <summary>Every computed due date for a Preventive Maintenance contract, per linked asset, cross-referenced
    /// against work orders already generated for it. Empty list if the contract isn't PM-type or is missing
    /// PmCadence/EndDate.</summary>
    Task<List<PmScheduleRow>> GetPreventiveMaintenanceScheduleAsync(int contractId);
}

public class ServiceVendorCandidate
{
    public int VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public int ContractId { get; set; }
    public string? ContractNumber { get; set; }
}

public class PmScheduleRow
{
    public int AssetId { get; set; }
    public string AssetLabel { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int? WorkOrderId { get; set; }
    public string? WorkOrderNumber { get; set; }
}

public interface IWorkOrderService
{
    Task<WorkOrder> CreateAsync(WorkOrder workOrder, string userId);
    /// <summary>Employee-facing report — creates a WorkOrder with Stage="Draft", outside the normal pipeline, awaiting admin review.</summary>
    Task<WorkOrder> ReportAsync(WorkOrder workOrder, string userId);
    /// <summary>Admin approves a Draft: edits priority/description/notes, resolves the given Service-contract vendor, and sends it straight to that vendor (Stage="Sent to Vendor"). Throws if vendorId isn't one of the asset's active Service-contract vendors.</summary>
    Task AcceptAsync(int workOrderId, int vendorId, string priority, string? description, string? notes, string userId);
    /// <summary>Admin dismisses a Draft as not actionable — terminal state, stays out of the active pipeline.</summary>
    Task RejectAsync(int workOrderId, string? reason, string userId);
    /// <summary>Sends an admin-created ("New") work order to a resolved Service-contract vendor — Stage="Sent to Vendor".</summary>
    Task SendToVendorAsync(int workOrderId, int vendorId, string userId);
    /// <summary>Vendor submits a fix — Stage="Fixed - Pending Confirmation".</summary>
    Task VendorFixAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(string Name, int Quantity)> parts, string vendorUserId);
    /// <summary>Vendor reports they can't proceed — Stage="Blocked".</summary>
    Task VendorBlockAsync(int workOrderId, int blockReasonId, string? detail, string vendorUserId);
    /// <summary>Admin resolves whatever blocked the vendor and sends it back to the same vendor — Stage="Sent to Vendor", block fields cleared.</summary>
    Task ResendToVendorAsync(int workOrderId, string userId);
    /// <summary>Admin accepts the vendor's fix as complete — Stage="Closed".</summary>
    Task ConfirmFixAsync(int workOrderId, string userId);
    /// <summary>Admin re-judges priority while reviewing the work order — allowed only before it's
    /// sent to a vendor (Stage is "Draft" or "New"), not once a vendor is already acting on it.</summary>
    Task UpdatePriorityAsync(int workOrderId, string priority, string userId);
    /// <summary>Auto-generated by the Preventive Maintenance scheduler — creates a work order already at
    /// Stage="Sent to Vendor" (no Draft/New review step), vendor taken directly from the PM contract.</summary>
    Task<WorkOrder> CreatePreventiveMaintenanceOccurrenceAsync(int assetId, int vendorId, int sourceContractId, DateTime scheduledDate, string? contractNumber, string systemUserId);
    Task<byte[]> ExportToExcelAsync();
    Task<byte[]> ExportToPdfAsync();
}

