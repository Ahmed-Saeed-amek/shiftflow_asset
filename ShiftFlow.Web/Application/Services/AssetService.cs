using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
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

    // The Edit/Create form's assignee dropdown already excludes deactivated users, but that's
    // UI-only — a direct POST with a stale/tampered id otherwise saves an Asset "assigned" to
    // someone who no longer has any access, unlike MaintenanceOrderService/WorkOrderService, which
    // both explicitly validate the target user exists (and here, is still active) before saving.
    private async Task EnsureAssigneeIsActiveAsync(string? assignedToUserId)
    {
        if (assignedToUserId is null) return;
        if (!await _db.Users.AnyAsync(u => u.Id == assignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
    }

    // CategoryId/ZoneId are populated from dropdowns the same way AssignedToUserId is, and were
    // just as unchecked: a direct POST with a stale/tampered id sailed past this service straight
    // into SaveChangesAsync, where it hit the FK constraint and surfaced as an unhandled 500 with a
    // raw SqlException (confirmed live) instead of the friendly validation error every other
    // tampered-FK path in this controller already gets.
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

    // Every editable field is saved, but the audit entry used to hard-code Status alone as
    // old/new — an edit that reassigned the asset to a different zone, category, or employee
    // without also touching Status produced a byte-identical old/new pair, silently dropping the
    // one piece of context (who/where it moved from and to) the audit trail exists to capture
    // (confirmed live: a Zone-only change logged "Maintenance" -> "Maintenance").
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
        // AssetTag used to be missing from this list entirely — the Edit form (shared _Form.cshtml
        // with Create) lets the user type a new tag and it passes validation, but nothing here ever
        // wrote it back, so a changed tag was silently discarded even though the save reported
        // success (confirmed live: POST with a new AssetTag returned success but the DB row was
        // unchanged). The controller now duplicate-checks AssetTag on Edit the same way Create does.
        existing.AssetTag = asset.AssetTag; existing.Name = asset.Name; existing.NameAr = asset.NameAr; existing.CategoryId = asset.CategoryId;
        existing.ZoneId = asset.ZoneId; existing.Model = asset.Model; existing.SerialNumber = asset.SerialNumber;
        existing.Manufacturer = asset.Manufacturer; existing.Sku = asset.Sku; existing.Status = asset.Status; existing.AssignedToUserId = asset.AssignedToUserId;
        existing.PurchaseDate = asset.PurchaseDate; existing.WarrantyExpiry = asset.WarrantyExpiry; existing.Notes = asset.Notes;
        existing.UpdatedByUserId = userId; existing.UpdatedDate = DateTime.UtcNow;
        var newValue = await SnapshotAsync(existing);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Asset", existing.Id.ToString(), userId, oldValue: oldValue, newValue: newValue);
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var asset = await _db.Assets.FindAsync(id) ?? throw new InvalidOperationException("Asset not found.");
        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Asset", id.ToString(), userId, oldValue: asset.AssetTag);
    }

    private async Task<List<Asset>> GetExportRowsAsync(string userId)
    {
        var query = await _scopeService.ApplyScopeAsync(
            _db.Assets.Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.LocationCategory),
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
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
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
            PdfReportHelper.AddHeader(doc, _loc.T("Asset Register"), _loc.TDate(DateTime.Today.ToString("dddd, dd MMMM yyyy")));

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
