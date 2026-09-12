using ShiftFlow.Application.Services;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

/// <summary>Report export tool. It generates nothing: every entry points at an export action that
/// already exists, and carries that action's own [Authorize] policy, re-checked here before the URL
/// is handed out. The permission varies per report kind, so this tool is registered with no
/// blanket permission in the orchestrator's registry and does its own check instead.</summary>
public class AiReportToolFunctions
{
    private readonly IPermissionService _permissions;

    public AiReportToolFunctions(IPermissionService permissions) => _permissions = permissions;

    private sealed record ReportSpec(string Label, string? ExcelUrl, string? PdfUrl, string Permission);

    private static readonly Dictionary<string, ReportSpec> Reports = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assets"] = new("Assets", AiLinks.ExportAssetsExcel(), AiLinks.ExportAssetsPdf(), PermissionCatalog.AssetExport),
        ["workOrders"] = new("Work orders", AiLinks.ExportWorkOrdersExcel(), AiLinks.ExportWorkOrdersPdf(), PermissionCatalog.WorkOrderExport),
        ["contracts"] = new("Contracts", AiLinks.ExportContractsExcel(), AiLinks.ExportContractsPdf(), PermissionCatalog.ContractView),
        ["inspectionOrders"] = new("Inspection orders", AiLinks.ExportInspectionOrdersExcel(), null, PermissionCatalog.InspectionOrderExport),
        ["maintenanceOrders"] = new("Maintenance orders", AiLinks.ExportMaintenanceOrdersExcel(), null, PermissionCatalog.MaintenanceOrderExport),
        ["users"] = new("Users", AiLinks.ExportUsersExcel(), null, PermissionCatalog.UserView),
        // Dashboard/ExportPdf inherits DashboardController's class-level policy.
        ["dashboardPdf"] = new("Executive dashboard", null, AiLinks.ExportDashboardPdf(), PermissionCatalog.InspectionOrderManage),
    };

    public static IEnumerable<string> Kinds => Reports.Keys;

    public async Task<object> ExportReportAsync(string kind, string? format, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind) || !Reports.TryGetValue(kind.Trim(), out var spec))
            return new { error = "action_failed", message = $"Unknown report. Available: {string.Join(", ", Reports.Keys)}." };

        if (!await _permissions.HasPermissionAsync(userId, spec.Permission))
            return new { error = "forbidden", message = "You don't have permission to export that report." };

        var wantsPdf = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);
        var url = wantsPdf ? spec.PdfUrl : spec.ExcelUrl;
        if (url == null)
        {
            var available = spec.ExcelUrl != null ? "excel" : "pdf";
            return new { error = "action_failed", message = $"The {spec.Label} report is only available as {available}." };
        }

        var label = $"{spec.Label} ({(wantsPdf ? "PDF" : "Excel")})";
        return new
        {
            success = true,
            kind,
            format = wantsPdf ? "pdf" : "excel",
            url,
            note = "The download link is already shown to the user — do not repeat the URL in your reply.",
            ui = new AiDownloadAttachment { Label = label, Url = url },
        };
    }
}
