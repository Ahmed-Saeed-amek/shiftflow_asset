namespace ShiftFlow.Application.AI;

/// <summary>Every app-relative URL the assistant can hand back. Centralized here so no tool (and
/// certainly no model output) ever concatenates a path with an id by hand — attachments carry
/// links built from these methods only.</summary>
public static class AiLinks
{
    public static string Asset(int id) => $"/Assets/Details/{id}";
    public static string Assets() => "/Assets";
    public static string AssetReport(int assetId) => $"/WorkOrders/Report?assetId={assetId}";
    public static string WorkOrder(int id) => $"/WorkOrders/Details/{id}";
    public static string WorkOrders() => "/WorkOrders";
    public static string MaintenanceOrder(int id) => $"/MaintenanceOrders/Details/{id}";
    /// <summary>MaintenanceOrdersController has no Index — the unified Orders list is the
    /// maintenance-order list page.</summary>
    public static string MaintenanceOrders() => "/Orders";
    public static string InspectionOrder(int id) => $"/InspectionOrders/Details/{id}";
    public static string InspectionOrders() => "/InspectionOrders";
    public static string Orders() => "/Orders";
    public static string NewOrder() => "/Orders/Create";
    public static string SparePart(int id) => $"/SpareParts/Details/{id}";
    public static string SpareParts() => "/SpareParts";
    public static string Contract(int id) => $"/Contracts/Details/{id}";
    public static string Contracts() => "/Contracts";
    public static string Vendor(int id) => $"/Vendors/Details/{id}";
    public static string Vendors() => "/Vendors";
    public static string Zone(int id) => $"/ZoneOverview/Details/{id}";
    public static string ZoneOverview() => "/ZoneOverview";
    public static string Zones() => "/Zones";
    public static string Dashboard() => "/Dashboard";
    public static string AuditLogs() => "/AuditLogs";
    public static string Users() => "/Users";
    public static string Groups() => "/Groups";
    public static string MyOrders() => "/Users/MyOrders";
    public static string MyHome() => "/MyHome";
    public static string RecurringOrders() => "/RecurringOrders";
    public static string SparePartsAnalytics() => "/SparePartsAnalytics";

    // Export endpoints — the existing controller actions, not new file generation.
    public static string ExportAssetsExcel() => "/Assets/ExportExcel";
    public static string ExportAssetsPdf() => "/Assets/ExportPdf";
    public static string ExportWorkOrdersExcel() => "/WorkOrders/ExportExcel";
    public static string ExportWorkOrdersPdf() => "/WorkOrders/ExportPdf";
    public static string ExportContractsExcel() => "/Contracts/ExportExcel";
    public static string ExportContractsPdf() => "/Contracts/ExportPdf";
    public static string ExportInspectionOrdersExcel() => "/InspectionOrders/ExportExcel";
    public static string ExportMaintenanceOrdersExcel() => "/MaintenanceOrders/ExportExcel";
    public static string ExportUsersExcel() => "/Users/ExportExcel";
    public static string ExportDashboardPdf() => "/Dashboard/ExportPdf";
}
