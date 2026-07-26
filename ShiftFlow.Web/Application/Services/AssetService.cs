using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class AssetService : IAssetService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IContractService _contractService;
    public AssetService(ApplicationDbContext db, IAuditService audit, IContractService contractService) { _db = db; _audit = audit; _contractService = contractService; }

    public async Task<Asset> CreateAsync(Asset asset, string userId)
    {
        asset.CreatedByUserId = userId;
        asset.CreatedDate = DateTime.UtcNow;
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Asset", asset.Id.ToString(), userId, newValue: asset.AssetTag);
        return asset;
    }

    public async Task UpdateAsync(Asset asset, string userId)
    {
        var existing = await _db.Assets.FindAsync(asset.Id) ?? throw new InvalidOperationException("Asset not found.");
        var oldStatus = existing.Status;
        existing.Name = asset.Name; existing.NameAr = asset.NameAr; existing.CategoryId = asset.CategoryId;
        existing.ZoneId = asset.ZoneId; existing.Model = asset.Model; existing.SerialNumber = asset.SerialNumber;
        existing.Manufacturer = asset.Manufacturer; existing.Sku = asset.Sku; existing.Status = asset.Status; existing.AssignedToUserId = asset.AssignedToUserId;
        existing.PurchaseDate = asset.PurchaseDate; existing.WarrantyExpiry = asset.WarrantyExpiry; existing.Notes = asset.Notes;
        existing.UpdatedByUserId = userId; existing.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Asset", existing.Id.ToString(), userId, oldValue: oldStatus, newValue: existing.Status);
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var asset = await _db.Assets.FindAsync(id) ?? throw new InvalidOperationException("Asset not found.");
        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Asset", id.ToString(), userId, oldValue: asset.AssetTag);
    }

    private async Task<List<Asset>> GetExportRowsAsync() =>
        await _db.Assets.Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.Block).ThenInclude(bl => bl!.Area).ThenInclude(a => a!.Governorate)
            .OrderBy(a => a.AssetTag).ToListAsync();

    private static string ZoneLabel(Asset a) =>
        a.Zone == null ? "" : $"{a.Zone.Name} ({a.Zone.Block?.Area?.Name}, {a.Zone.Block?.Area?.Governorate?.Name})";

    public async Task<byte[]> ExportToExcelAsync()
    {
        var assets = await GetExportRowsAsync();
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

    public async Task<byte[]> ExportToPdfAsync()
    {
        var assets = await GetExportRowsAsync();
        var vendors = await _contractService.GetDerivedVendorsAsync(assets.Select(a => a.Id));
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            var doc = new Document(pdf);
            doc.Add(new Paragraph("Asset Register").SetBold().SetFontSize(16));
            var table = new Table(8, true).UseAllAvailableWidth();
            foreach (var h in new[] { "Tag", "Name", "Category", "Zone", "Vendor", "Model", "Serial Number", "Status" })
                table.AddHeaderCell(h);
            foreach (var a in assets)
            {
                table.AddCell(a.AssetTag);
                table.AddCell(a.Name);
                table.AddCell(a.Category?.Name ?? "");
                table.AddCell(ZoneLabel(a));
                table.AddCell(vendors.GetValueOrDefault(a.Id)?.Name ?? "");
                table.AddCell(a.Model ?? "");
                table.AddCell(a.SerialNumber ?? "");
                table.AddCell(a.Status);
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
