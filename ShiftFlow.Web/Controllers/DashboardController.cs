using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.InspectionOrderManage)]
public class DashboardController : Controller
{
    private readonly IDashboardService _dash;
    private readonly UserManager<ApplicationUser> _um;
    private readonly ApplicationDbContext _db;
    private readonly ShiftFlow.Web.Localization.ILanguageService _loc;
    private readonly IAssetScopeService _scope;

    public DashboardController(IDashboardService dash, UserManager<ApplicationUser> um, ApplicationDbContext db, ShiftFlow.Web.Localization.ILanguageService loc, IAssetScopeService scope)
    {
        _dash = dash; _um = um; _db = db; _loc = loc; _scope = scope;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _um.GetUserAsync(User);

        // NOTE: a scoped DbContext cannot run multiple queries concurrently — each query
        // must be awaited before the next starts, otherwise EF throws
        // "A second operation was started on this context instance".
        var kpis = await _dash.GetKpisAsync(user?.Id);

        // Same UserAssetScope a scoped user is already restricted to everywhere else (Orders
        // list, Details, the KPI cards above) — without it, the widgets below leaked full order
        // numbers/status/assignee for out-of-scope assets that the same user is 404'd out of one
        // click later on Details.
        List<int>? scopedAssetIds = user != null && await _scope.HasScopeAsync(user.Id)
            ? await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), user.Id)).Select(a => a.Id).ToListAsync()
            : null;

        // Inspection-order-status chart: fixed status list so the chart's shape/order/colors
        // stay stable as data grows instead of silently changing whenever a status count
        // drops to/from zero.
        string[] statusOrder = ["Open", "InProgress", "Done"];
        var statusChartQuery = _db.InspectionOrders.AsNoTracking();
        if (scopedAssetIds != null) statusChartQuery = statusChartQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var statusCounts = (await statusChartQuery
                .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        ViewBag.OrderStatusLabels = statusOrder;
        ViewBag.OrderStatusData = statusOrder.Select(s => statusCounts.GetValueOrDefault(s, 0)).ToList();

        ViewBag.RecentOrders = await BuildRecentOrdersAsync(scopedAssetIds);

        var (overdueCount, overdueOrders) = await BuildOverdueOrdersAsync(scopedAssetIds);
        ViewBag.OverdueOrderCount = overdueCount;
        ViewBag.OverdueOrders = overdueOrders;

        return View(kpis);
    }

    public async Task<IActionResult> ExportPdf()
    {
        var user = await _um.GetUserAsync(User);
        var kpis = await _dash.GetKpisAsync(user?.Id);
        List<int>? scopedAssetIds = user != null && await _scope.HasScopeAsync(user.Id)
            ? await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), user.Id)).Select(a => a.Id).ToListAsync()
            : null;
        var (overdueCount, overdueOrders) = await BuildOverdueOrdersAsync(scopedAssetIds);

        string[] statusOrder = ["Open", "InProgress", "Done"];
        var statusChartQuery = _db.InspectionOrders.AsNoTracking();
        if (scopedAssetIds != null) statusChartQuery = statusChartQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var statusCounts = (await statusChartQuery
                .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);

        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            var doc = new Document(pdf);
            PdfReportHelper.AddHeader(doc, "Executive Dashboard", "Ministry of Electricity, Water & Renewable Energy — Kuwait · " + DateTime.Today.ToString("dddd, dd MMMM yyyy"));

            PdfReportHelper.AddKpiRow(doc,
                ("Open Inspection Orders", kpis.OpenInspectionOrders.ToString(), PdfReportHelper.Primary),
                ("Overdue Orders", overdueCount.ToString(), PdfReportHelper.Danger),
                ("Active Groups", kpis.ActiveGroups.ToString(), PdfReportHelper.Violet),
                ("Defective Assets", $"{kpis.DefectiveAssets}/{kpis.TotalAssets}", PdfReportHelper.Danger),
                ("Open Work Orders", kpis.OpenWorkOrders.ToString(), PdfReportHelper.Warning),
                ("Low Stock Parts", kpis.LowStockPartsCount.ToString(), PdfReportHelper.Warning));

            var statusData = statusOrder.Select(s => (s == "InProgress" ? "In Progress" : s, statusCounts.GetValueOrDefault(s, 0)));
            PdfReportHelper.AddBarChart(doc, "Inspection Orders by Status", statusData, PdfReportHelper.Primary);

            doc.Add(new Paragraph("Overdue Orders").SetBold().SetFontSize(13).SetMarginBottom(8));
            var overdueTable = PdfReportHelper.StyledTable(new float[] { 1.4f, 1.2f, 1.6f, 1f }, new[] { "Order Number", "Category", "Assigned To", "Due Date" });
            var i = 0;
            foreach (var o in overdueOrders)
            {
                PdfReportHelper.AddRow(overdueTable, i++, 9,
                    o.OrderNumber, o.CategoryLabel, o.AssignedToLabel ?? "-", o.DueDate?.ToString("yyyy-MM-dd") ?? "-");
            }
            doc.Add(overdueTable);
        }
        return File(ms.ToArray(), "application/pdf", $"Dashboard_{DateTime.Today:yyyyMMdd}.pdf");
    }

    /// <summary>Overdue = Inspection or Maintenance order, still open, past its DueDate — combined
    /// across both categories, same as BuildRecentOrdersAsync, since an executive checking "what's
    /// overdue" cares about both, not just Inspection Orders (the previous version's scope). Work
    /// Orders have no DueDate/deadline concept (only ScheduledDate, used for recurring-schedule
    /// dedup), so there's nothing comparable to include for them.</summary>
    private async Task<(int Count, List<MyWorkOrderRow> Rows)> BuildOverdueOrdersAsync(List<int>? scopedAssetIds)
    {
        var today = DateTime.UtcNow.Date;

        var inspectionQuery = _db.InspectionOrders.AsNoTracking()
            .Where(o => o.Status != "Done" && o.Status != "Cancelled" && o.DueDate != null && o.DueDate < today);
        if (scopedAssetIds != null) inspectionQuery = inspectionQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var inspectionCount = await inspectionQuery.CountAsync();
        var inspectionRows = (await inspectionQuery.Include(o => o.AssignedToUser).Include(o => o.AssignedToGroup)
            .OrderBy(o => o.DueDate).Take(6).ToListAsync())
            .Select(o => new MyWorkOrderRow
            {
                Category = "Inspection", CategoryLabel = "Inspection", Id = o.Id, OrderNumber = o.OrderNumber,
                Status = o.Status, DueDate = o.DueDate, CreatedAt = o.CreatedAt, DetailsController = "InspectionOrders",
                AssignedToLabel = o.AssignedToUser != null ? o.AssignedToUser.FullName
                    : o.AssignedToGroup != null ? _loc.T("Group") + ": " + o.AssignedToGroup.Name : null,
            });

        var maintenanceQuery = _db.MaintenanceOrders.AsNoTracking()
            .Where(m => m.Status != "Done" && m.Status != "Cancelled" && m.DueDate != null && m.DueDate < today);
        if (scopedAssetIds != null) maintenanceQuery = maintenanceQuery.Where(m => scopedAssetIds.Contains(m.AssetId));
        var maintenanceCount = await maintenanceQuery.CountAsync();
        var maintenanceRows = (await maintenanceQuery.Include(m => m.AssignedToUser).Include(m => m.AssignedToGroup)
            .OrderBy(m => m.DueDate).Take(6).ToListAsync())
            .Select(m => new MyWorkOrderRow
            {
                Category = "Maintenance", CategoryLabel = "Maintenance", Id = m.Id, OrderNumber = m.OrderNumber,
                Status = m.Status, DueDate = m.DueDate, CreatedAt = m.CreatedDate, DetailsController = "MaintenanceOrders",
                AssignedToLabel = m.AssignedToUser != null ? m.AssignedToUser.FullName
                    : m.AssignedToGroup != null ? _loc.T("Group") + ": " + m.AssignedToGroup.Name : null,
            });

        var rows = inspectionRows.Concat(maintenanceRows).OrderBy(r => r.DueDate).Take(6).ToList();
        return (inspectionCount + maintenanceCount, rows);
    }

    /// <summary>Org-wide "what's happening" feed — the most recent orders across all three
    /// categories (Inspection, Maintenance, Work Order), not just Inspection Orders, so the
    /// dashboard reflects actual recent activity rather than one order type. Restricted to the
    /// viewer's UserAssetScope, same as everywhere else scope is enforced (see Index above).</summary>
    private async Task<List<MyWorkOrderRow>> BuildRecentOrdersAsync(List<int>? scopedAssetIds)
    {
        var inspectionQuery = _db.InspectionOrders.AsNoTracking().Include(o => o.AssignedToUser).Include(o => o.AssignedToGroup).AsQueryable();
        if (scopedAssetIds != null) inspectionQuery = inspectionQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var inspectionRows = (await inspectionQuery
            .OrderByDescending(o => o.CreatedAt).Take(6)
            .ToListAsync())
            .Select(o => new MyWorkOrderRow
            {
                Category = "Inspection", CategoryLabel = "Inspection", Id = o.Id, OrderNumber = o.OrderNumber,
                Status = o.Status, DueDate = o.DueDate, CreatedAt = o.CreatedAt, DetailsController = "InspectionOrders",
                AssignedToLabel = o.AssignedToUser != null ? o.AssignedToUser.FullName
                    : o.AssignedToGroup != null ? _loc.T("Group") + ": " + o.AssignedToGroup.Name : null,
            })
            .ToList();

        var maintenanceQuery = _db.MaintenanceOrders.AsNoTracking().Include(m => m.AssignedToUser).Include(m => m.AssignedToGroup).AsQueryable();
        if (scopedAssetIds != null) maintenanceQuery = maintenanceQuery.Where(m => scopedAssetIds.Contains(m.AssetId));
        var maintenanceRows = (await maintenanceQuery
            .OrderByDescending(m => m.CreatedDate).Take(6)
            .ToListAsync())
            .Select(m => new MyWorkOrderRow
            {
                Category = "Maintenance", CategoryLabel = "Maintenance", Id = m.Id, OrderNumber = m.OrderNumber,
                Status = m.Status, CreatedAt = m.CreatedDate, DetailsController = "MaintenanceOrders",
                AssignedToLabel = m.AssignedToUser != null ? m.AssignedToUser.FullName
                    : m.AssignedToGroup != null ? _loc.T("Group") + ": " + m.AssignedToGroup.Name : null,
            })
            .ToList();

        var workOrderQuery = _db.WorkOrders.AsNoTracking().Include(w => w.AssignedToUser).Include(w => w.Vendor).AsQueryable();
        if (scopedAssetIds != null) workOrderQuery = workOrderQuery.Where(w => scopedAssetIds.Contains(w.AssetId));
        var workOrderRows = await workOrderQuery
            .OrderByDescending(w => w.CreatedDate).Take(6)
            .Select(w => new MyWorkOrderRow
            {
                Category = "WorkOrder", CategoryLabel = "Work Order", Id = w.Id, OrderNumber = w.WorkOrderNumber,
                Status = w.Stage, CreatedAt = w.CreatedDate, DetailsController = "WorkOrders",
                AssignedToLabel = w.AssignedToUser != null ? w.AssignedToUser.FullName : w.Vendor != null ? w.Vendor.Name : null,
            })
            .ToListAsync();

        return inspectionRows.Concat(maintenanceRows).Concat(workOrderRows)
            .OrderByDescending(r => r.CreatedAt)
            .Take(6)
            .ToList();
    }
}
