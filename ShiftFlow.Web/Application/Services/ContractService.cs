using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class ContractService : IContractService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    public ContractService(ApplicationDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // Cost is stored as decimal(12,2) - a value the client can't represent (e.g. a 27-digit
    // string pasted into the field) used to reach an unhandled DbUpdateException/ArgumentException
    // at SaveChangesAsync, leaking the raw EF/SQL error. Vendors similarly had no existence check,
    // so a stale/tampered VendorId hit an unhandled FK-constraint violation instead of a clean
    // message - confirmed live on both.
    private const decimal MaxCost = 9_999_999_999.99m;

    private async Task ValidateAsync(Contract contract)
    {
        if (contract.Cost is { } cost && (cost < 0 || cost > MaxCost))
            throw new InvalidOperationException($"Cost must be between 0 and {MaxCost:N2}.");
        if (!await _db.Vendors.AnyAsync(v => v.Id == contract.VendorId))
            throw new InvalidOperationException("Selected vendor not found.");
    }

    public async Task<Contract> CreateAsync(Contract contract, List<int> assetIds, string userId)
    {
        await ValidateAsync(contract);
        contract.CreatedDate = DateTime.UtcNow;
        contract.AssetLinks = assetIds.Select(id => new ContractAsset { AssetId = id }).ToList();
        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Contract", contract.Id.ToString(), userId, newValue: contract.ContractNumber);
        return contract;
    }

    public async Task UpdateAsync(Contract contract, List<int> assetIds, string userId)
    {
        await ValidateAsync(contract);
        var existing = await _db.Contracts.Include(c => c.AssetLinks).FirstOrDefaultAsync(c => c.Id == contract.Id)
            ?? throw new InvalidOperationException("Contract not found.");
        existing.VendorId = contract.VendorId; existing.ContractType = contract.ContractType; existing.ContractNumber = contract.ContractNumber;
        existing.StartDate = contract.StartDate; existing.EndDate = contract.EndDate; existing.Cost = contract.Cost; existing.Notes = contract.Notes;
        existing.PmCadence = contract.PmCadence;

        var toRemove = existing.AssetLinks.Where(l => !assetIds.Contains(l.AssetId)).ToList();
        var toAdd = assetIds.Where(id => !existing.AssetLinks.Any(l => l.AssetId == id)).Select(id => new ContractAsset { ContractId = existing.Id, AssetId = id });
        _db.ContractAssets.RemoveRange(toRemove);
        _db.ContractAssets.AddRange(toAdd);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Contract", existing.Id.ToString(), userId, newValue: existing.ContractNumber);
    }

    public async Task<Vendor?> GetDerivedVendorAsync(int assetId)
    {
        var result = await GetDerivedVendorsAsync([assetId]);
        return result.GetValueOrDefault(assetId);
    }

    public async Task<Dictionary<int, Vendor?>> GetDerivedVendorsAsync(IEnumerable<int> assetIds)
    {
        var today = DateTime.UtcNow.Date;
        var contracts = await _db.ContractAssets
            .Where(ca => assetIds.Contains(ca.AssetId))
            .Select(ca => new { ca.AssetId, ca.Contract!.VendorId, ca.Contract.Vendor, ca.Contract.StartDate, ca.Contract.EndDate })
            .ToListAsync();

        var result = new Dictionary<int, Vendor?>();
        foreach (var group in contracts.GroupBy(c => c.AssetId))
        {
            var active = group.Where(c => c.EndDate == null || c.EndDate >= today).OrderByDescending(c => c.StartDate).FirstOrDefault();
            var pick = active ?? group.OrderByDescending(c => c.StartDate).First();
            result[group.Key] = pick.Vendor;
        }
        return result;
    }

    public async Task<List<ServiceVendorCandidate>> GetActiveServiceVendorsAsync(int assetId)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.ContractAssets
            .Where(ca => ca.AssetId == assetId
                && ca.Contract!.ContractType == "Service"
                && (ca.Contract.EndDate == null || ca.Contract.EndDate >= today))
            .Select(ca => new ServiceVendorCandidate
            {
                VendorId = ca.Contract!.VendorId,
                VendorName = ca.Contract.Vendor!.Name,
                ContractId = ca.ContractId,
                ContractNumber = ca.Contract.ContractNumber,
            })
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<PmScheduleRow>> GetPreventiveMaintenanceScheduleAsync(int contractId)
    {
        var contract = await _db.Contracts.Include(c => c.AssetLinks).ThenInclude(l => l.Asset)
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract is null || contract.ContractType != "Preventive Maintenance"
            || contract.PmCadence is null || contract.EndDate is null)
            return [];

        var dueDates = Contract.ComputeOccurrenceDueDates(contract.StartDate, contract.EndDate.Value, contract.PmCadence);

        var generated = await _db.WorkOrders
            .Where(w => w.SourceContractId == contractId)
            .Select(w => new { w.AssetId, w.ScheduledDate, w.Id, w.WorkOrderNumber })
            .ToListAsync();
        var lookup = generated.ToDictionary(w => (w.AssetId, w.ScheduledDate!.Value.Date), w => (w.Id, w.WorkOrderNumber));

        var rows = new List<PmScheduleRow>();
        foreach (var link in contract.AssetLinks)
        {
            foreach (var due in dueDates)
            {
                lookup.TryGetValue((link.AssetId, due), out var match);
                rows.Add(new PmScheduleRow
                {
                    AssetId = link.AssetId,
                    AssetLabel = $"{link.Asset!.AssetTag} — {link.Asset.Name}",
                    DueDate = due,
                    WorkOrderId = match.Id == 0 ? null : match.Id,
                    WorkOrderNumber = match.WorkOrderNumber,
                });
            }
        }
        return rows.OrderBy(r => r.DueDate).ThenBy(r => r.AssetLabel).ToList();
    }

    private async Task<List<Contract>> GetExportRowsAsync() =>
        await _db.Contracts.Include(c => c.Vendor).Include(c => c.AssetLinks)
            .OrderByDescending(c => c.StartDate).ToListAsync();

    public async Task<byte[]> ExportToExcelAsync()
    {
        var contracts = await GetExportRowsAsync();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Contracts");
        string[] headers = ["Contract Number", "Vendor", "Type", "Start Date", "End Date", "Cost", "Assets"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var c in contracts)
        {
            ws.Cells[row, 1].Value = c.ContractNumber;
            ws.Cells[row, 2].Value = c.Vendor?.Name;
            ws.Cells[row, 3].Value = c.ContractType;
            ws.Cells[row, 4].Value = c.StartDate.ToString("yyyy-MM-dd");
            ws.Cells[row, 5].Value = c.EndDate?.ToString("yyyy-MM-dd");
            ws.Cells[row, 6].Value = c.Cost;
            ws.Cells[row, 7].Value = c.AssetLinks.Count;
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }

    public async Task<byte[]> ExportToPdfAsync()
    {
        var contracts = await GetExportRowsAsync();
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            var doc = new Document(pdf);
            doc.Add(new Paragraph("Contracts").SetBold().SetFontSize(16));
            var table = new Table(7, true).UseAllAvailableWidth();
            foreach (var h in new[] { "Contract Number", "Vendor", "Type", "Start Date", "End Date", "Cost", "Assets" })
                table.AddHeaderCell(h);
            foreach (var c in contracts)
            {
                table.AddCell(c.ContractNumber ?? "");
                table.AddCell(c.Vendor?.Name ?? "");
                table.AddCell(c.ContractType);
                table.AddCell(c.StartDate.ToString("yyyy-MM-dd"));
                table.AddCell(c.EndDate?.ToString("yyyy-MM-dd") ?? "");
                table.AddCell(c.Cost?.ToString("0.00") ?? "");
                table.AddCell(c.AssetLinks.Count.ToString());
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
