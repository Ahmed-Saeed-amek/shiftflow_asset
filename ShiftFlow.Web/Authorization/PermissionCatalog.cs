namespace ShiftFlow.Web.Authorization;

/// <summary>
/// Single source of truth for permission name constants.
/// Used both as policy names in AddAuthorization() and as attribute values.
/// </summary>
public static class PermissionCatalog
{
    // ── Users ──────────────────────────────────────────────────────────────────
    public const string UserManage        = "User.Manage";
    public const string UserView          = "User.View";

    // ── Inspection Orders ──────────────────────────────────────────────────────
    public const string InspectionOrderView   = "InspectionOrder.View";
    public const string InspectionOrderManage = "InspectionOrder.Manage";
    public const string InspectionOrderReport = "InspectionOrder.Report";
    public const string InspectionOrderExport = "InspectionOrder.Export";

    // ── Teams ──────────────────────────────────────────────────────────────────
    public const string TeamView              = "Team.View";
    public const string TeamManage            = "Team.Manage";

    // ── AI Assistant ────────────────────────────────────────────────────────────
    public const string AiAssistantUse              = "AiAssistant.Use";

    // ── Audit Logs ──────────────────────────────────────────────────────────────
    public const string AuditLogView                = "AuditLog.View";

    // ── Locations ───────────────────────────────────────────────────────────────
    public const string LocationManage              = "Location.Manage";

    // ── RBAC Administration ─────────────────────────────────────────────────────
    public const string RbacManage                  = "Rbac.Manage";

    // ── Asset Management ────────────────────────────────────────────────────────
    public const string AssetView                   = "Asset.View";
    public const string AssetManage                 = "Asset.Manage";
    public const string AssetCategoryManage         = "AssetCategory.Manage";
    public const string VendorView                  = "Vendor.View";
    public const string VendorManage                = "Vendor.Manage";
    public const string WorkOrderView                = "WorkOrder.View";
    public const string WorkOrderManage              = "WorkOrder.Manage";
    public const string WorkOrderAssign              = "WorkOrder.Assign";
    public const string WorkOrderExport              = "WorkOrder.Export";
    public const string ContractView                 = "Contract.View";
    public const string ContractManage                = "Contract.Manage";
    public const string AssetReportAction             = "Asset.ReportAction";
    public const string AssetScopeManage              = "Asset.ScopeManage";

    // ── Maintenance Orders (standalone in-house fix, no vendor/Work Order pipeline) ────
    public const string MaintenanceOrderView          = "MaintenanceOrder.View";
    public const string MaintenanceOrderManage        = "MaintenanceOrder.Manage";
    public const string MaintenanceOrderReport        = "MaintenanceOrder.Report";
    public const string MaintenanceOrderExport        = "MaintenanceOrder.Export";

    // ── Order Types (shared catalog: Inspection Orders + Maintenance Orders) ──────
    public const string OrderTypeManage               = "OrderType.Manage";

    // ── Super Permission ───────────────────────────────────────────────────────
    /// <summary>Grants every permission and every hardcoded Admin role-check, everywhere (see AdminClaimsTransformation and PermissionService).</summary>
    public const string IsAdmin                     = "System.IsAdmin";

    /// <summary>All permission names. One authorization policy is registered per entry at startup.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        UserManage, UserView,
        InspectionOrderView, InspectionOrderManage, InspectionOrderReport, InspectionOrderExport,
        TeamView, TeamManage,
        AiAssistantUse,
        AuditLogView,
        LocationManage,
        RbacManage,
        AssetView, AssetManage, AssetCategoryManage,
        VendorView, VendorManage,
        WorkOrderView, WorkOrderManage, WorkOrderAssign, WorkOrderExport,
        ContractView, ContractManage, AssetReportAction, AssetScopeManage,
        MaintenanceOrderView, MaintenanceOrderManage, MaintenanceOrderReport, MaintenanceOrderExport,
        OrderTypeManage,
        IsAdmin,
    ];
}
