using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ShiftFlow.Web.Services;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Application.Services;

public class AssetService : IAssetService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IContractService _contractService;
    private readonly IAssetScopeService _scopeService;
    private readonly ILanguageService _loc;
    public AssetService(ApplicationDbContext db, IAuditService audit, IContractService contractService, IAssetScopeService scopeService, ILanguageService loc)
    { _db = db; _audit = audit; _contractService = contractService; _scopeService = scopeService; _loc = loc; }

    // The form's assignee dropdown excludes deactivated users, but a direct POST with a stale or
    // tampered id would otherwise save an Asset assigned to someone with no access.
    private async Task EnsureAssigneeIsActiveAsync(string? assignedToUserId)
    {
        if (assignedToUserId is null) return;
        if (!await _db.Users.AnyAsync(u => u.Id == assignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
    }

    // Stale/tampered CategoryId/ZoneId would otherwise hit the FK constraint as an unhandled 500.
    private async Task EnsureCategoryAndZoneExistAsync(int categoryId, int zoneId)
    {
        if (!await _db.AssetCategories.AnyAsync(c => c.Id == categoryId))
            throw new InvalidOperationException("Selected category not found.");
        if (!await _db.Zones.AnyAsync(z => z.Id == zoneId))
            throw new InvalidOperationException("Selected zone not found.");
    }

    public async Task<Asset> CreateAsync(Asset asset, string userId)
    {
        await EnsureCategoryAndZoneExistAsync(asset.CategoryId, asset.ZoneId);
        await EnsureAssigneeIsActiveAsync(asset.AssignedToUserId);
        asset.CreatedByUserId = userId;
        asset.CreatedDate = DateTime.UtcNow;
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Asset", asset.Id.ToString(), userId, newValue: asset.AssetTag);
        return asset;
    }

    // Full snapshot so the audit trail captures which zone/category/assignee the asset moved
    // between, not just Status.
    private async Task<string> SnapshotAsync(Asset a)
    {
        var zoneName = await _db.Zones.Where(z => z.Id == a.ZoneId).Select(z => z.Name).FirstOrDefaultAsync();
        var categoryName = await _db.AssetCategories.Where(c => c.Id == a.CategoryId).Select(c => c.Name).FirstOrDefaultAsync();
        var assigneeName = a.AssignedToUserId != null
            ? await _db.Users.Where(u => u.Id == a.AssignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync()
            : null;
        return $"{a.AssetTag}, {a.Name}, Category: {categoryName ?? "—"}, Zone: {zoneName ?? "—"}, Status: {a.Status}, Assigned to: {assigneeName ?? "—"}";
    }

    public async Task UpdateAsync(Asset asset, string userId)
    {
        await EnsureCategoryAndZoneExistAsync(asset.CategoryId, asset.ZoneId);
        await EnsureAssigneeIsActiveAsync(asset.AssignedToUserId);
        var existing = await _db.Assets.FindAsync(asset.Id) ?? throw new InvalidOperationException("Asset not found.");
        var oldValue = await SnapshotAsync(existing);
        existing.AssetTag = asset.AssetTag; existing.Name = asset.Name; existing.NameAr = asset.NameAr; existing.CategoryId = asset.CategoryId;
        existing.ZoneId = asset.ZoneId; existing.Model = asset.Model; existing.SerialNumber = asset.SerialNumber;
        existing.Manufacturer = asset.Manufacturer; existing.Sku = asset.Sku; existing.Status = asset.Status; existing.AssignedToUserId = asset.AssignedToUserId;
        existing.PurchaseDate = asset.PurchaseDate; existing.WarrantyExpiry = asset.WarrantyExpiry; existing.Notes = asset.Notes;
        existing.UpdatedByUserId = userId; existing.UpdatedDate = DateTime.UtcNow;
        var newValue = await SnapshotAsync(existing);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Asset", existing.Id.ToString(), userId, oldValue: oldValue, newValue: newValue);
    }

    /// <summary>Everything that references an Asset, checked before the delete so the caller gets a
    /// readable "what is blocking this" message instead of a raw FK violation from SaveChanges.</summary>
    private async Task EnsureNoDependenciesAsync(int id)
    {
        var blockers = new List<string>();
        var workOrders = await _db.WorkOrders.CountAsync(w => w.AssetId == id);
        if (workOrders > 0) blockers.Add($"{workOrders} {_loc.T("work order(s)")}");
        var maintenanceOrders = await _db.MaintenanceOrders.CountAsync(m => m.AssetId == id);
        if (maintenanceOrders > 0) blockers.Add($"{maintenanceOrders} {_loc.T("maintenance order(s)")}");
        var inspections = await _db.InspectionRunAssets.CountAsync(i => i.AssetId == id);
        if (inspections > 0) blockers.Add($"{inspections} {_loc.T("inspection record(s)")}");
        var contracts = await _db.ContractAssets.CountAsync(c => c.AssetId == id);
        if (contracts > 0) blockers.Add($"{contracts} {_loc.T("contract(s)")}");
        var spareParts = await _db.SparePartAssets.CountAsync(sp => sp.AssetId == id);
        if (spareParts > 0) blockers.Add($"{spareParts} {_loc.T("spare part link(s)")}");
        var recurring = await _db.RecurringOrderAssets.CountAsync(r => r.AssetId == id);
        if (recurring > 0) blockers.Add($"{recurring} {_loc.T("recurring order schedule(s)")}");

        if (blockers.Count > 0)
            throw new InvalidOperationException(
                _loc.T("This asset can't be deleted because it is still referenced by:") + " " + string.Join(", ", blockers) + ". "
                + _loc.T("Set its status to Retired instead."));
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var asset = await _db.Assets.FindAsync(id) ?? throw new InvalidOperationException("Asset not found.");
        await EnsureNoDependenciesAsync(id);
        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Asset", id.ToString(), userId, oldValue: asset.AssetTag);
    }

    private async Task<List<Asset>> GetExportRowsAsync(string userId)
    {
        var query = await _scopeService.ApplyScopeAsync(
            _db.Assets.AsNoTracking().Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.LocationCategory),
            userId);
        return await query.OrderBy(a => a.AssetTag).ToListAsync();
    }

    private string ZoneLabel(Asset a)
    {
        if (a.Zone == null) return "";
        var zoneName = LocalizedName(a.Zone.Name, a.Zone.NameAr);
        var locCat = a.Zone.LocationCategory;
        var locCatName = locCat == null ? null : LocalizedName(locCat.Name, locCat.NameAr);
        return $"{zoneName} ({locCatName})";
    }

    private string? LocalizedCategoryName(Asset a) =>
        a.Category == null ? null : LocalizedName(a.Category.Name, a.Category.NameAr);

    private string LocalizedName(string name, string? nameAr) =>
        _loc.Lang == "ar" && !string.IsNullOrWhiteSpace(nameAr) ? nameAr : name;

    public async Task<byte[]> ExportToExcelAsync(string userId)
    {
        var assets = await GetExportRowsAsync(userId);
        var vendors = await _contractService.GetDerivedVendorsAsync(assets.Select(a => a.Id));
        ExcelHelper.EnsureLicense();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Assets");
        string[] headers = ["Tag", "Name", "Category", "Zone", "Vendor", "Model", "Serial Number", "Status"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var a in assets)
        {
            ws.Cells[row, 1].Value = a.AssetTag;
            ws.Cells[row, 2].Value = a.Name;
            ws.Cells[row, 3].Value = a.Category?.Name;
            ws.Cells[row, 4].Value = ZoneLabel(a);
            ws.Cells[row, 5].Value = vendors.GetValueOrDefault(a.Id)?.Name;
            ws.Cells[row, 6].Value = a.Model;
            ws.Cells[row, 7].Value = a.SerialNumber;
            ws.Cells[row, 8].Value = a.Status;
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }

    public async Task<byte[]> ExportToPdfAsync(string userId)
    {
        var assets = await GetExportRowsAsync(userId);
        var vendors = await _contractService.GetDerivedVendorsAsync(assets.Select(a => a.Id));
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            PdfReportHelper.ApplyPageBackground(pdf);
            var doc = new Document(pdf);
            doc.SetFont(PdfReportHelper.GetFont(_loc.IsRTL));
            PdfReportHelper.AddHeader(doc, _loc.T("Asset Register"), _loc.TDate(DateTime.UtcNow.ToString("dddd, dd MMMM yyyy")));

            var byStatus = assets.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.Count());
            PdfReportHelper.AddKpiRow(doc,
                (_loc.T("Total Assets"), assets.Count.ToString(), PdfReportHelper.Primary),
                (_loc.T("Working"), byStatus.GetValueOrDefault("Working").ToString(), PdfReportHelper.Success),
                (_loc.T("Defective"), byStatus.GetValueOrDefault("Defective").ToString(), PdfReportHelper.Danger),
                (_loc.T("Maintenance"), byStatus.GetValueOrDefault("Maintenance").ToString(), PdfReportHelper.Warning),
                (_loc.T("Retired"), byStatus.GetValueOrDefault("Retired").ToString(), PdfReportHelper.MutedText));

            var byCategory = assets.GroupBy(a => LocalizedCategoryName(a) ?? _loc.T("Uncategorized"))
                .OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count()));
            PdfReportHelper.AddBarChart(doc, _loc.T("Assets by Category"), byCategory, PdfReportHelper.Primary);

            var table = PdfReportHelper.StyledTable(
                new float[] { 1.4f, 1.8f, 1.3f, 1.6f, 1.3f, 1.1f, 1.3f, 1f },
                new[] { _loc.T("Tag"), _loc.T("Name"), _loc.T("Category"), _loc.T("Zone"), _loc.T("Vendor"), _loc.T("Model"), _loc.T("Serial Number"), _loc.T("Status") });
            var i = 0;
            foreach (var a in assets)
            {
                PdfReportHelper.AddRow(table, i++, 9,
                    a.AssetTag, a.Name, LocalizedCategoryName(a) ?? "", ZoneLabel(a),
                    vendors.GetValueOrDefault(a.Id)?.Name ?? "", a.Model ?? "", a.SerialNumber ?? "", _loc.T(a.Status));
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
