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
public class DashboardKpis{public int TotalEngineers{get;set;}public int OpenInspectionOrders{get;set;}public int InspectionOrdersOverdue{get;set;}public int ActiveGroups{get;set;}public int TotalAssets{get;set;}public int DefectiveAssets{get;set;}public int OpenWorkOrders{get;set;}public int CriticalOpenWorkOrders{get;set;}public int LowStockPartsCount{get;set;}}

// -- Inspection Orders / Groups ------------------------------------------------
public interface IInspectionOrderService
{
    Task<InspectionOrder> CreateAsync(int orderTypeId, string? description, string? assignedToUserId, int? assignedToGroupId,
        List<int>? assetIds, DateTime? dueDate, string createdByUserId, int? sourceRecurringOrderId = null, DateTime? scheduledDate = null);
    Task<InspectionOrder?> GetByIdAsync(int id);
    Task<List<InspectionOrder>> GetMyOrdersAsync(string userId, bool includeDone = false, DateTime? from = null, DateTime? to = null);
    Task<List<InspectionOrder>> GetAllAsync(string? status, string? search, bool overdue, string userId);
    Task UpdateInspectionItemAsync(int itemId, string outcome, int? workOrderId, string updatedByUserId);
    /// <summary>Records maintenance actions performed on an asset independent of the OK/Defective
    /// outcome decision � logs what maintenance was done without requiring (or changing) an
    /// outcome, unlike UpdateInspectionItemAsync. Safe to call on an item at any point, including
    /// after its outcome is already recorded, since it never touches Outcome/WorkOrderId or the
    /// order's completion status.</summary>
    Task UpdateMaintenanceActionsAsync(int itemId, List<int>? maintenanceActionTypeIds, string updatedByUserId);
    Task CancelAsync(int orderId, string? reason, string userId);
    /// <summary>Manager sign-off for an order whose OrderType.RequiresApproval is true � PendingApproval -> Done.</summary>
    Task ApproveAsync(int orderId, string managerUserId);
    /// <summary>Manager-only re-assignment to a different employee or Group � the only way to recover
    /// an order whose sole assignee has since been deactivated, since report/complete actions are
    /// otherwise gated on being the current assignee or a member of the assigned Group with no
    /// manager override. Exactly one of assignedToUserId/assignedToGroupId must be set.</summary>
    Task ReassignAsync(int orderId, string? assignedToUserId, int? assignedToGroupId, string managerUserId);
    Task<byte[]> ExportToExcelAsync(string userId);
}

public interface IGroupService
{
    Task<List<Group>> GetAllAsync(bool includeInactive = false);
    Task<Group?> GetByIdAsync(int id);
    Task<Group> CreateAsync(string name, string? nameAr, string? description, List<string> initialMemberUserIds, string userId);
    Task UpdateAsync(int groupId, string name, string? nameAr, string? description, string userId);
    Task SetActiveAsync(int groupId, bool isActive, string userId);
    Task AddMemberAsync(int groupId, string userId, string actingUserId);
    Task RemoveMemberAsync(int groupId, string userId, string actingUserId);
    Task<bool> IsMemberAsync(int groupId, string userId);
    /// <summary>Reconciles a group's membership to exactly the given list � used by the Edit page
    /// instead of separate AddMember/RemoveMember calls. originalMemberUserIds is the snapshot the
    /// edit form was loaded with; rejects the call if the DB's current membership no longer matches
    /// it (someone else changed it concurrently), instead of silently discarding their change.</summary>
    Task SetMembersAsync(int groupId, List<string> memberUserIds, List<string> originalMemberUserIds, string actingUserId);
}

// -- Asset Management ---------------------------------------------------------
public interface IAssetService
{
    Task<Asset> CreateAsync(Asset asset, string userId);
    Task UpdateAsync(Asset asset, string userId);
    Task DeleteAsync(int id, string userId);
    /// <summary>Both exports respect the caller's UserAssetScope (Zone/LocationCategory/Category),
    /// same as Assets/Index � a scoped user must not be able to pull the full, unscoped inventory
    /// just by hitting the export link directly instead of the (correctly scoped) list page.</summary>
    Task<byte[]> ExportToExcelAsync(string userId);
    Task<byte[]> ExportToPdfAsync(string userId);
}

public interface IVendorService
{
    Task<Vendor> CreateAsync(Vendor vendor, string userId);
    Task UpdateAsync(Vendor vendor, string userId);
}

public interface IRecurringOrderService
{
    Task<RecurringOrder> CreateAsync(RecurringOrder schedule, List<int> assetIds, string userId);
    /// <summary>originalAssetIds is the linked-asset snapshot the edit form was loaded with; the
    /// call is rejected if the schedule's actual linked assets no longer match it (someone else
    /// changed them concurrently), instead of silently discarding their change. Mirrors
    /// IContractService.UpdateAsync's same pattern.</summary>
    Task UpdateAsync(RecurringOrder schedule, List<int> assetIds, List<int> originalAssetIds, string userId);
}

public interface IContractService
{
    /// <summary>newAssets are created and linked in the same SaveChangesAsync call as the contract
    /// itself — if the contract save fails (bad vendor, invalid cost, etc.) none of the new assets
    /// are persisted either, since nothing about them is ever saved standalone.</summary>
    Task<Contract> CreateAsync(Contract contract, List<int> assetIds, List<NewAssetInput> newAssets, string userId);
    /// <summary>originalAssetIds is the linked-asset snapshot the edit form was loaded with; the
    /// call is rejected if the contract's actual linked assets no longer match it (someone else
    /// changed them concurrently), instead of silently discarding their change.</summary>
    Task UpdateAsync(Contract contract, List<int> assetIds, List<int> originalAssetIds, List<NewAssetInput> newAssets, string userId);
    /// <summary>Picks the vendor from the asset's most recently-started contract that's currently active (EndDate null or in the future); falls back to the most recent contract overall; null if the asset has no contracts.</summary>
    Task<Vendor?> GetDerivedVendorAsync(int assetId);
    Task<Dictionary<int, Vendor?>> GetDerivedVendorsAsync(IEnumerable<int> assetIds);
    /// <summary>Active (EndDate null or in the future) Service-type contracts covering this asset � the candidate pool a Work Order's vendor must be resolved from. Empty means the asset isn't covered by any Service contract yet.</summary>
    Task<List<ServiceVendorCandidate>> GetActiveServiceVendorsAsync(int assetId);
    /// <summary>Every computed due date for a Preventive Maintenance contract, per linked asset, cross-referenced
    /// against work orders already generated for it. Empty list if the contract isn't PM-type or is missing
    /// PmCadence/EndDate.</summary>
    Task<List<PmScheduleRow>> GetPreventiveMaintenanceScheduleAsync(int contractId);
    Task<byte[]> ExportToExcelAsync();
    Task<byte[]> ExportToPdfAsync();
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

/// <summary>One row of the Contract form's inline asset creator — a new Asset to create and link
/// to the contract, in the same save as the contract itself. Rows with a blank AssetTag are
/// ignored (an added-then-untouched row), so nothing needs to be posted at all when unused.</summary>
public class NewAssetInput
{
    public string AssetTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public int ZoneId { get; set; }
}

public interface ISparePartService
{
    Task<SparePart> CreateAsync(SparePart part, List<int> assetIds, string userId);
    Task UpdateAsync(SparePart part, List<int> assetIds, string userId);
    Task AdjustStockAsync(int sparePartId, int newQuantity, string? reason, string userId);
    /// <summary>Spare parts linked to this specific asset, active only � backs the fix-report pickers.</summary>
    Task<List<SparePart>> GetCompatiblePartsAsync(int assetId);
    /// <summary>Atomic, race-safe decrement guarded by StockQuantity >= quantity � same TOCTOU-safe
    /// ExecuteUpdateAsync-with-WHERE-guard pattern WorkOrderService uses for stage transitions.
    /// Returns false (0 rows affected) if stock is insufficient.</summary>
    Task<bool> TryDecrementStockAsync(int sparePartId, int quantity);
}

public interface IWorkOrderService
{
    Task<WorkOrder> CreateAsync(WorkOrder workOrder, string userId);
    /// <summary>Employee-facing report � creates a WorkOrder with Stage="Draft", outside the normal pipeline, awaiting admin review.</summary>
    Task<WorkOrder> ReportAsync(WorkOrder workOrder, string userId);
    /// <summary>Admin approves a Draft: sets priority, and either sends it to any active vendor (Stage="Sent to Vendor") or � when only an employee is assigned, no vendor � moves it straight to "New" so the employee's Report Fix action becomes available. Requires at least one of vendorId/the work order's own AssignedToUserId to be set.</summary>
    Task AcceptAsync(int workOrderId, int? vendorId, string priority, string userId);
    /// <summary>Admin dismisses a Draft as not actionable � terminal state, stays out of the active pipeline.</summary>
    Task RejectAsync(int workOrderId, string? reason, string userId);
    /// <summary>Sends an admin-created ("New") work order to any active vendor � Stage="Sent to Vendor".</summary>
    Task SendToVendorAsync(int workOrderId, int vendorId, string userId);
    /// <summary>Admin assigns/reassigns/clears the internal employee on a work order � independent of and combinable with VendorId, usable at any stage.</summary>
    Task AssignEmployeeAsync(int workOrderId, string? employeeUserId, string userId);
    /// <summary>The assigned employee's own equivalent of VendorFixAsync � only when no vendor is in play (VendorId == null) and only from Stage "New" (skips the vendor pipeline entirely). FixCost is computed from the parts used, not a manual input.</summary>
    Task<WorkOrder> EmployeeFixAsync(int workOrderId, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId);
    /// <summary>Bypasses waiting on the vendor's own response for a work order at Stage="Sent to Vendor" whose RequiresVendorResponse is false � usable by a manager (isManager=true) or the assigned employee. Ends at "Fixed - Pending Confirmation" like VendorFixAsync/EmployeeFixAsync.</summary>
    Task<WorkOrder> AdvanceWithoutVendorAsync(int workOrderId, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string userId, bool isManager = false);
    /// <summary>Admin override � force-closes a work order from any non-Closed stage without waiting on the vendor's or employee's own reply.</summary>
    Task ForceCloseAsync(int workOrderId, string? reason, string userId);
    /// <summary>Vendor submits a fix � Stage="Fixed - Pending Confirmation". FixCost is computed from the parts used, not a manual input.</summary>
    Task VendorFixAsync(int workOrderId, DateTime? completionDate, List<(int SparePartId, int Quantity)> parts, string vendorUserId);
    /// <summary>Vendor reports they can't proceed � Stage="Blocked".</summary>
    Task VendorBlockAsync(int workOrderId, int blockReasonId, string? detail, string vendorUserId);
    /// <summary>Admin resolves whatever blocked the vendor and sends it back to the same vendor � Stage="Sent to Vendor", block fields cleared.</summary>
    Task ResendToVendorAsync(int workOrderId, string userId);
    /// <summary>Admin accepts the vendor's fix as complete � Stage="Closed".</summary>
    Task ConfirmFixAsync(int workOrderId, string userId);
    /// <summary>Admin re-judges priority while reviewing the work order � allowed only before it's
    /// sent to a vendor (Stage is "Draft" or "New"), not once a vendor is already acting on it.</summary>
    Task UpdatePriorityAsync(int workOrderId, string priority, string userId);
    /// <summary>Auto-generated by the Preventive Maintenance scheduler � creates a work order already at
    /// Stage="Sent to Vendor" (no Draft/New review step), vendor taken directly from the PM contract.</summary>
    Task<WorkOrder> CreatePreventiveMaintenanceOccurrenceAsync(int assetId, int vendorId, int sourceContractId, DateTime scheduledDate, string? contractNumber, string systemUserId);
    /// <summary>Auto-generated by the Recurring Order scheduler for a RequiresVendor order type �
    /// the single-asset, no-contract counterpart to CreatePreventiveMaintenanceOccurrenceAsync. Also
    /// creates the work order already at Stage="Sent to Vendor", but carries the schedule's own
    /// AssignedToUserId (an internal employee overseeing the vendor's work), which PM contracts have
    /// no equivalent field for.</summary>
    Task<WorkOrder> CreateRecurringVendorOccurrenceAsync(int assetId, int vendorId, string? assignedToUserId, int sourceRecurringOrderId, DateTime scheduledDate, string systemUserId);
    Task<byte[]> ExportToExcelAsync(string userId);
    Task<byte[]> ExportToPdfAsync(string userId);
}

public interface IMaintenanceOrderService
{
    /// <summary>Admin/manager assigns an employee or a Group to fix an asset in-house � no vendor, no
    /// Work Order. Exactly one of assignedToUserId/assignedToGroupId must be set. Sets Asset.Status
    /// to "Maintenance" (unless Retired).</summary>
    Task<MaintenanceOrder> CreateAsync(int assetId, string? assignedToUserId, int? assignedToGroupId, string? description, DateTime? dueDate, string createdByUserId, int? orderTypeId = null, int? sourceRecurringOrderId = null, DateTime? scheduledDate = null);
    /// <summary>The assigned employee reports the fix � Status "Open" -> "Done". Restores Asset.Status
    /// to "Working" unless another Work Order or Maintenance Order is still open on the same asset.</summary>
    Task<MaintenanceOrder> CompleteAsync(int orderId, DateTime? completedDate, List<(int SparePartId, int Quantity)> parts, string employeeUserId);
    /// <summary>Manager sign-off for an order whose OrderType.RequiresApproval is true � PendingApproval -> Done.</summary>
    Task ApproveAsync(int orderId, string managerUserId);
    /// <summary>Admin cancels an Open order � same asset-status restore rule as CompleteAsync.</summary>
    Task CancelAsync(int orderId, string? reason, string userId);
    /// <summary>Manager-only re-assignment to a different employee or Group � the only way to recover
    /// an order whose sole assignee has since been deactivated, since Complete is otherwise gated on
    /// being the current assignee or a member of the assigned Group with no manager override. Exactly
    /// one of assignedToUserId/assignedToGroupId must be set.</summary>
    Task ReassignAsync(int orderId, string? assignedToUserId, int? assignedToGroupId, string managerUserId);
    Task<MaintenanceOrder?> GetByIdAsync(int id);
    Task<List<MaintenanceOrder>> GetAllAsync(string? status, string? search, string userId);
    Task<byte[]> ExportToExcelAsync(string userId);
}

