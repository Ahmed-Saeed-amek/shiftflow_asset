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
public class DashboardKpis{public int TotalEngineers{get;set;}public int OpenInspectionOrders{get;set;}public int InspectionOrdersOverdue{get;set;}public int ActiveTeams{get;set;}public int TotalAssets{get;set;}public int DefectiveAssets{get;set;}public int OpenWorkOrders{get;set;}public int CriticalOpenWorkOrders{get;set;}public int LowStockPartsCount{get;set;}}

// ── Inspection Orders / Teams ────────────────────────────────────────────────
public interface IInspectionOrderService
{
    Task<InspectionOrder> CreateAsync(int orderTypeId, string? description, string? assignedToUserId, int? assignedToTeamId,
        List<int>? assetIds, DateTime? dueDate, string createdByUserId);
    Task<InspectionOrder?> GetByIdAsync(int id);
    Task<List<InspectionOrder>> GetMyOrdersAsync(string userId, bool includeDone = false, DateTime? from = null, DateTime? to = null);
    Task<List<InspectionOrder>> GetAllAsync(string? status, string? search, bool overdue = false);
    Task UpdateInspectionItemAsync(int itemId, string outcome, int? workOrderId, List<int>? maintenanceActionTypeIds, string updatedByUserId);
    /// <summary>Records maintenance actions performed on an asset independent of the OK/Defective
    /// outcome decision — logs what maintenance was done without requiring (or changing) an
    /// outcome, unlike UpdateInspectionItemAsync. Safe to call on an item at any point, including
    /// after its outcome is already recorded, since it never touches Outcome/WorkOrderId or the
    /// order's completion status.</summary>
    Task UpdateMaintenanceActionsAsync(int itemId, List<int>? maintenanceActionTypeIds, string updatedByUserId);
    Task CancelAsync(int orderId, string? reason, string userId);
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
    /// <summary>Reconciles a team's membership to exactly the given list — used by the Edit page instead of separate AddMember/RemoveMember calls.</summary>
    Task SetMembersAsync(int teamId, List<string> memberUserIds, string actingUserId);
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

public interface ISparePartService
{
    Task<SparePart> CreateAsync(SparePart part, List<int> assetIds, string userId);
    Task UpdateAsync(SparePart part, List<int> assetIds, string userId);
    Task AdjustStockAsync(int sparePartId, int newQuantity, string? reason, string userId);
    /// <summary>Spare parts linked to this specific asset, active only — backs the fix-report pickers.</summary>
    Task<List<SparePart>> GetCompatiblePartsAsync(int assetId);
    /// <summary>Atomic, race-safe decrement guarded by StockQuantity >= quantity — same TOCTOU-safe
    /// ExecuteUpdateAsync-with-WHERE-guard pattern WorkOrderService uses for stage transitions.
    /// Returns false (0 rows affected) if stock is insufficient.</summary>
    Task<bool> TryDecrementStockAsync(int sparePartId, int quantity);
}

public interface IWorkOrderService
{
    Task<WorkOrder> CreateAsync(WorkOrder workOrder, string userId);
    /// <summary>Employee-facing report — creates a WorkOrder with Stage="Draft", outside the normal pipeline, awaiting admin review.</summary>
    Task<WorkOrder> ReportAsync(WorkOrder workOrder, string userId);
    /// <summary>Admin approves a Draft: sets priority, and either sends it to any active vendor (Stage="Sent to Vendor") or — when only an employee is assigned, no vendor — moves it straight to "New" so the employee's Report Fix action becomes available. Requires at least one of vendorId/the work order's own AssignedToUserId to be set.</summary>
    Task AcceptAsync(int workOrderId, int? vendorId, string priority, string userId);
    /// <summary>Admin dismisses a Draft as not actionable — terminal state, stays out of the active pipeline.</summary>
    Task RejectAsync(int workOrderId, string? reason, string userId);
    /// <summary>Sends an admin-created ("New") work order to any active vendor — Stage="Sent to Vendor".</summary>
    Task SendToVendorAsync(int workOrderId, int vendorId, string userId);
    /// <summary>Admin assigns/reassigns/clears the internal employee on a work order — independent of and combinable with VendorId, usable at any stage.</summary>
    Task AssignEmployeeAsync(int workOrderId, string? employeeUserId, string userId);
    /// <summary>The assigned employee's own equivalent of VendorFixAsync — only when no vendor is in play (VendorId == null) and only from Stage "New" (skips the vendor pipeline entirely).</summary>
    Task<WorkOrder> EmployeeFixAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId);
    /// <summary>Bypasses waiting on the vendor's own response for a work order at Stage="Sent to Vendor" whose RequiresVendorResponse is false — usable by a manager (isManager=true) or the assigned employee. Ends at "Fixed - Pending Confirmation" like VendorFixAsync/EmployeeFixAsync.</summary>
    Task<WorkOrder> AdvanceWithoutVendorAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string userId, bool isManager = false);
    /// <summary>Admin override — force-closes a work order from any non-Closed stage without waiting on the vendor's or employee's own reply.</summary>
    Task ForceCloseAsync(int workOrderId, string? reason, string userId);
    /// <summary>Vendor submits a fix — Stage="Fixed - Pending Confirmation".</summary>
    Task VendorFixAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string vendorUserId);
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

public interface IMaintenanceOrderService
{
    /// <summary>Admin/manager assigns an employee to fix an asset in-house — no vendor, no Work Order.
    /// Sets Asset.Status to "Maintenance" (unless Retired).</summary>
    Task<MaintenanceOrder> CreateAsync(int assetId, string assignedToUserId, string? description, DateTime? dueDate, string createdByUserId, int? orderTypeId = null);
    /// <summary>The assigned employee reports the fix — Status "Open" -> "Done". Restores Asset.Status
    /// to "Working" unless another Work Order or Maintenance Order is still open on the same asset.</summary>
    Task<MaintenanceOrder> CompleteAsync(int orderId, string fixDescription, decimal? cost, DateTime? completedDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId);
    /// <summary>Admin cancels an Open order — same asset-status restore rule as CompleteAsync.</summary>
    Task CancelAsync(int orderId, string? reason, string userId);
    Task<MaintenanceOrder?> GetByIdAsync(int id);
    Task<List<MaintenanceOrder>> GetAllAsync(string? status, string? search);
    Task<byte[]> ExportToExcelAsync();
}

