using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Application.Services;

public class ContractService : IContractService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILanguageService _loc;
    public ContractService(ApplicationDbContext db, IAuditService audit, ILanguageService loc) { _db = db; _audit = audit; _loc = loc; }

    // Cost is stored as decimal(12,2) - a value the client can't represent (e.g. a 27-digit
    // string pasted into the field) used to reach an unhandled DbUpdateException/ArgumentException
    // at SaveChangesAsync, leaking the raw EF/SQL error. Vendors similarly had no existence check,
    // so a stale/tampered VendorId hit an unhandled FK-constraint violation instead of a clean
    // message - confirmed live on both.
    private const decimal MaxCost = 9_999_999_999.99m;

    private async Task ValidateAsync(Contract contract, List<int> assetIds)
    {
        if (contract.Cost is { } cost && (cost < 0 || cost > MaxCost))
            throw new InvalidOperationException($"Cost must be between 0 and {MaxCost:N2}.");
        if (!await _db.Vendors.AnyAsync(v => v.Id == contract.VendorId))
            throw new InvalidOperationException("Selected vendor not found.");
        // A stale multi-select value or a raw/tampered POST with a non-existent asset id otherwise
        // hits the DB's Restrict FK constraint on ContractAssets.AssetId and raises an unhandled
        // DbUpdateException — same bug class the VendorId check above already guards against.
        if (assetIds.Count > 0 && await _db.Assets.CountAsync(a => assetIds.Contains(a.Id)) != assetIds.Distinct().Count())
            throw new InvalidOperationException("One or more selected assets were not found.");
        // Every other order-creation path (Inspection/Maintenance/WorkOrder) blocks new work
        // against a Retired asset — a Contract linking one is a dangling, misleading association
        // (it can still surface via GetDerivedVendorAsync/GetActiveServiceVendorsAsync) since no
        // order can ever actually be opened against that asset.
        var retiredTags = assetIds.Count > 0
            ? await _db.Assets.Where(a => assetIds.Contains(a.Id) && a.Status == "Retired").Select(a => a.AssetTag).ToListAsync()
            : [];
        if (retiredTags.Count > 0)
            throw new InvalidOperationException("These assets are retired and can't be linked to a contract: " + string.Join(", ", retiredTags));
    }

    /// <summary>Validates and builds the new Asset entities for the Contract form's inline
    /// creator — not yet added to the DbContext, so nothing is persisted until the caller adds
    /// them alongside the Contract and calls SaveChangesAsync once. Rows with a blank AssetTag
    /// (an added-then-untouched row) are silently skipped rather than rejected.</summary>
    private async Task<List<Asset>> BuildNewAssetsAsync(List<NewAssetInput> newAssets, string userId)
    {
        var rows = newAssets.Where(a => !string.IsNullOrWhiteSpace(a.AssetTag)).ToList();
        if (rows.Count == 0) return [];

        var tags = rows.Select(a => a.AssetTag!.Trim()).ToList();
        var dupe = tags.GroupBy(t => t, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dupe != null)
            throw new InvalidOperationException($"Duplicate new asset tag: {dupe.Key}.");
        var existingTags = await _db.Assets.Where(a => tags.Contains(a.AssetTag)).Select(a => a.AssetTag).ToListAsync();
        if (existingTags.Count > 0)
            throw new InvalidOperationException($"Asset tag already in use: {string.Join(", ", existingTags)}.");

        var categoryIds = rows.Select(a => a.CategoryId).Distinct().ToList();
        var zoneIds = rows.Select(a => a.ZoneId).Distinct().ToList();
        var validCategoryIds = (await _db.AssetCategories.Where(c => categoryIds.Contains(c.Id)).Select(c => c.Id).ToListAsync()).ToHashSet();
        var validZoneIds = (await _db.Zones.Where(z => zoneIds.Contains(z.Id)).Select(z => z.Id).ToListAsync()).ToHashSet();

        var assets = new List<Asset>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Name))
                throw new InvalidOperationException($"New asset \"{r.AssetTag}\" needs a name.");
            if (!validCategoryIds.Contains(r.CategoryId))
                throw new InvalidOperationException($"New asset \"{r.AssetTag}\": selected category not found.");
            if (!validZoneIds.Contains(r.ZoneId))
                throw new InvalidOperationException($"New asset \"{r.AssetTag}\": selected zone not found.");
            var status = string.IsNullOrWhiteSpace(r.Status) ? "Working" : r.Status;
            if (!Asset.Statuses.Contains(status))
                throw new InvalidOperationException($"New asset \"{r.AssetTag}\": invalid status.");
            assets.Add(new Asset
            {
                AssetTag = r.AssetTag!.Trim(), Name = r.Name!.Trim(), CategoryId = r.CategoryId, ZoneId = r.ZoneId,
                Status = status, Model = string.IsNullOrWhiteSpace(r.Model) ? null : r.Model.Trim(),
                SerialNumber = string.IsNullOrWhiteSpace(r.SerialNumber) ? null : r.SerialNumber.Trim(),
                CreatedByUserId = userId, CreatedDate = DateTime.UtcNow,
            });
        }
        return assets;
    }

    public async Task<Contract> CreateAsync(Contract contract, List<int> assetIds, List<NewAssetInput> newAssets, string userId)
    {
        await ValidateAsync(contract, assetIds);
        var newAssetEntities = await BuildNewAssetsAsync(newAssets, userId);
        contract.CreatedDate = DateTime.UtcNow;
        contract.AssetLinks = assetIds.Select(id => new ContractAsset { AssetId = id }).ToList();
        foreach (var asset in newAssetEntities)
            contract.AssetLinks.Add(new ContractAsset { Asset = asset });
        _db.Contracts.Add(contract);
        // Single SaveChangesAsync for the contract, its asset links, AND the brand-new assets —
        // if this throws (bad FK, DB constraint), nothing here is persisted; a new asset is never
        // created unless the contract it's being linked to is created too.
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Contract", contract.Id.ToString(), userId, newValue: contract.ContractNumber);
        foreach (var asset in newAssetEntities)
            await _audit.LogAsync("Create", "Asset", asset.Id.ToString(), userId, newValue: $"{asset.AssetTag} (via Contract {contract.ContractNumber})");
        return contract;
    }

    public async Task UpdateAsync(Contract contract, List<int> assetIds, List<int> originalAssetIds, List<NewAssetInput> newAssets, string userId)
    {
        var existing = await _db.Contracts.Include(c => c.AssetLinks).FirstOrDefaultAsync(c => c.Id == contract.Id)
            ?? throw new InvalidOperationException("Contract not found.");
        // Same lost-update race rounds 19-20 fixed for Group membership and RBAC permissions: this
        // diffs the posted asset list against whatever is live in ContractAssets right now, with no
        // check that the editor's page snapshot is still current — a concurrent edit's asset link
        // gets silently deleted by an unrelated save (confirmed live: Admin B's Notes-only edit
        // erased an asset Admin A had just added). originalAssetIds is the snapshot the edit form
        // was loaded with; reject the submit if it no longer matches what's actually linked.
        var currentAssetIds = existing.AssetLinks.Select(l => l.AssetId).ToHashSet();
        if (!currentAssetIds.SetEquals(originalAssetIds.Distinct()))
            throw new InvalidOperationException("This contract's linked assets were changed by someone else since you opened this page. Reload and try again.");
        // Only newly-added links are checked for retirement — a contract that already had an asset
        // linked before it was retired shouldn't have every unrelated future edit (e.g. changing
        // Cost) blocked until someone remembers to unlink it.
        var newlyAddedIds = assetIds.Where(id => !existing.AssetLinks.Any(l => l.AssetId == id)).ToList();
        await ValidateAsync(contract, newlyAddedIds);
        var newAssetEntities = await BuildNewAssetsAsync(newAssets, userId);
        existing.VendorId = contract.VendorId; existing.ContractType = contract.ContractType; existing.ContractNumber = contract.ContractNumber;
        existing.StartDate = contract.StartDate; existing.EndDate = contract.EndDate; existing.Cost = contract.Cost; existing.Notes = contract.Notes;
        existing.PmCadence = contract.PmCadence;

        var toRemove = existing.AssetLinks.Where(l => !assetIds.Contains(l.AssetId)).ToList();
        var toAdd = newlyAddedIds.Select(id => new ContractAsset { ContractId = existing.Id, AssetId = id });
        _db.ContractAssets.RemoveRange(toRemove);
        _db.ContractAssets.AddRange(toAdd);
        foreach (var asset in newAssetEntities)
            existing.AssetLinks.Add(new ContractAsset { Asset = asset });

        // Single SaveChangesAsync — a new asset is never created unless this contract update
        // itself succeeds, same guarantee as CreateAsync above.
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Contract", existing.Id.ToString(), userId, newValue: existing.ContractNumber);
        foreach (var asset in newAssetEntities)
            await _audit.LogAsync("Create", "Asset", asset.Id.ToString(), userId, newValue: $"{asset.AssetTag} (via Contract {existing.ContractNumber})");
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
            PdfReportHelper.ApplyPageBackground(pdf);
            var doc = new Document(pdf);
            doc.SetFont(PdfReportHelper.GetFont(_loc.IsRTL));
            PdfReportHelper.AddHeader(doc, _loc.T("Contracts"), _loc.TDate(DateTime.Today.ToString("dddd, dd MMMM yyyy")));

            var today = DateTime.UtcNow.Date;
            var expiringSoon = contracts.Count(c => c.EndDate != null && c.EndDate >= today && c.EndDate <= today.AddDays(30));
            var totalCost = contracts.Sum(c => c.Cost ?? 0);
            PdfReportHelper.AddKpiRow(doc,
                (_loc.T("Total Contracts"), contracts.Count.ToString(), PdfReportHelper.Primary),
                (_loc.T("Total Cost"), totalCost.ToString("0.00"), PdfReportHelper.Success),
                (_loc.T("Expiring in 30 Days"), expiringSoon.ToString(), PdfReportHelper.Warning),
                (_loc.T("Linked Assets"), contracts.Sum(c => c.AssetLinks.Count).ToString(), PdfReportHelper.Info));

            var byType = contracts.GroupBy(c => _loc.T(c.ContractType)).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count()));
            PdfReportHelper.AddBarChart(doc, _loc.T("Contracts by Type"), byType, PdfReportHelper.Primary);

            var table = PdfReportHelper.StyledTable(
                new float[] { 1.6f, 1.6f, 1.4f, 1.2f, 1.2f, 1f, 0.8f },
                new[] { _loc.T("Contract Number"), _loc.T("Vendor"), _loc.T("Contract Type"), _loc.T("Start Date"), _loc.T("End Date"), _loc.T("Cost"), _loc.T("Assets") });
            var i = 0;
            foreach (var c in contracts)
            {
                PdfReportHelper.AddRow(table, i++, 9,
                    c.ContractNumber ?? "", c.Vendor?.Name ?? "", _loc.T(c.ContractType),
                    c.StartDate.ToString("yyyy-MM-dd"), c.EndDate?.ToString("yyyy-MM-dd") ?? "",
                    c.Cost?.ToString("0.00") ?? "", c.AssetLinks.Count.ToString());
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
