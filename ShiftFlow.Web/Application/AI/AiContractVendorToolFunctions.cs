using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.AI;

/// <summary>Contract and vendor lookups. These mirror ContractsController/VendorsController, which
/// are gated by Contract.View / Vendor.View and are deliberately not asset-scoped — a contract is a
/// commercial record, not an asset view. The one place asset scope does apply is the assetId filter
/// below, which resolves through the caller's scoped asset set like everything else.</summary>
public class AiContractVendorToolFunctions : AiToolsBase
{
    public AiContractVendorToolFunctions(ApplicationDbContext db, IAssetScopeService scope) : base(db, scope) { }

    public async Task<object> SearchContractsAsync(int? vendorId, string? type, int? expiringWithinDays, int? assetId,
        int? limit, string userId, CancellationToken ct)
    {
        var take = Clamp(limit, 10, 50);
        var q = Db.Contracts.AsNoTracking().Include(c => c.Vendor).AsQueryable();

        if (vendorId is > 0) q = q.Where(c => c.VendorId == vendorId);
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(c => c.ContractType == type);
        if (expiringWithinDays is > 0)
        {
            var today = DateTime.UtcNow.Date;
            var until = today.AddDays(expiringWithinDays.Value);
            q = q.Where(c => c.EndDate != null && c.EndDate >= today && c.EndDate <= until);
        }
        if (assetId.HasValue)
        {
            if (await FindScopedAssetAsync(assetId.Value, userId, ct) == null) return NotFound("Asset");
            q = q.Where(c => c.AssetLinks.Any(l => l.AssetId == assetId));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(c => c.EndDate == null).ThenBy(c => c.EndDate).Take(take)
            .Select(c => new
            {
                id = c.Id,
                number = c.ContractNumber,
                type = c.ContractType,
                vendor = c.Vendor != null ? c.Vendor.Name : null,
                startDate = c.StartDate,
                endDate = c.EndDate,
                assetCount = c.AssetLinks.Count,
                cost = c.Cost,
            })
            .ToListAsync(ct);

        var today2 = DateTime.UtcNow.Date;
        var table = new AiTableAttachment
        {
            Title = "Contracts",
            Columns = [new("number", "Contract"), new("type", "Type"), new("vendor", "Vendor"), new("endDate", "Ends"), new("assets", "Assets")],
            Rows = rows.Select(r => new Dictionary<string, object?>
            {
                ["number"] = r.number ?? $"#{r.id}",
                ["type"] = r.type,
                ["vendor"] = r.vendor ?? "—",
                ["endDate"] = D(r.endDate) ?? "Open-ended",
                ["assets"] = r.assetCount,
                ["_url"] = AiLinks.Contract(r.id),
                ["_status"] = r.endDate != null && r.endDate < today2 ? "Expired" : "Active",
            }).ToList(),
        };

        return new
        {
            totalMatches = total,
            returned = rows.Count,
            contracts = rows.Select(r => new { r.id, r.number, r.type, r.vendor, startDate = D(r.startDate), endDate = D(r.endDate), r.assetCount, r.cost }),
            ui = rows.Count > 0 ? (object?)table : null,
        };
    }

    public async Task<object> GetContractDetailAsync(int id, CancellationToken ct)
    {
        var contract = await Db.Contracts.AsNoTracking().Include(c => c.Vendor)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null) return NotFound("Contract");

        var assets = await Db.ContractAssets.AsNoTracking().Where(l => l.ContractId == id)
            .Select(l => new { id = l.AssetId, tag = l.Asset!.AssetTag, name = l.Asset.Name })
            .Take(50).ToListAsync(ct);

        var card = new AiCardsAttachment
        {
            Items =
            [
                new AiCard
                {
                    Title = contract.ContractNumber ?? $"Contract #{contract.Id}",
                    Subtitle = contract.Vendor?.Name,
                    Badge = new AiBadge(contract.ContractType, contract.EndDate != null && contract.EndDate < DateTime.UtcNow.Date ? "Expired" : "Active"),
                    Url = AiLinks.Contract(contract.Id),
                    Fields =
                    [
                        new("Start", D(contract.StartDate) ?? "—"),
                        new("End", D(contract.EndDate) ?? "Open-ended"),
                        new("Cost", Money(contract.Cost)),
                        new("PM cadence", contract.PmCadence ?? "—"),
                        new("Assets covered", assets.Count.ToString()),
                    ],
                },
            ],
        };

        return new
        {
            id = contract.Id,
            number = contract.ContractNumber,
            type = contract.ContractType,
            vendorId = contract.VendorId,
            vendor = contract.Vendor?.Name,
            startDate = D(contract.StartDate),
            endDate = D(contract.EndDate),
            cost = contract.Cost,
            pmCadence = contract.PmCadence,
            notes = contract.Notes,
            assets,
            ui = card,
        };
    }

    public async Task<object> ListVendorsAsync(string? query, bool? activeOnly, int? limit, CancellationToken ct)
    {
        var take = Clamp(limit, 20, 50);
        var q = Db.Vendors.AsNoTracking().AsQueryable();
        if (activeOnly != false) q = q.Where(v => v.Status == "Active");
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(v => v.Name.Contains(term) || (v.Specialization != null && v.Specialization.Contains(term)));
        }

        var vendors = await q.OrderBy(v => v.Name).Take(take)
            .Select(v => new { id = v.Id, name = v.Name, status = v.Status, specialization = v.Specialization, phone = v.Phone, email = v.Email })
            .ToListAsync(ct);

        return new { count = vendors.Count, vendors };
    }

    public async Task<object> GetVendorDetailAsync(int id, CancellationToken ct)
    {
        var vendor = await Db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vendor == null) return NotFound("Vendor");

        var openWorkOrders = await Db.WorkOrders.AsNoTracking()
            .Where(w => w.VendorId == id && OpenWorkOrderStages.Contains(w.Stage))
            .OrderByDescending(w => w.CreatedDate).Take(10)
            .Select(w => new { id = w.Id, number = w.WorkOrderNumber, stage = w.Stage, asset = w.Asset!.AssetTag, priority = w.Priority })
            .ToListAsync(ct);
        var openWorkOrderCount = await Db.WorkOrders.CountAsync(w => w.VendorId == id && OpenWorkOrderStages.Contains(w.Stage), ct);

        var contracts = await Db.Contracts.AsNoTracking().Where(c => c.VendorId == id)
            .OrderByDescending(c => c.StartDate).Take(10)
            .Select(c => new { id = c.Id, number = c.ContractNumber, type = c.ContractType, endDate = c.EndDate })
            .ToListAsync(ct);

        var card = new AiCardsAttachment
        {
            Items =
            [
                new AiCard
                {
                    Title = vendor.Name,
                    Subtitle = vendor.Specialization,
                    Badge = new AiBadge(vendor.Status, vendor.Status),
                    Url = AiLinks.Vendor(vendor.Id),
                    Fields =
                    [
                        new("Contact", vendor.ContactName ?? "—"),
                        new("Phone", vendor.Phone ?? "—"),
                        new("Email", vendor.Email ?? "—"),
                        new("Open work orders", openWorkOrderCount.ToString()),
                        new("Contracts", contracts.Count.ToString()),
                    ],
                },
            ],
        };

        var table = new AiTableAttachment
        {
            Title = "Open work orders",
            Columns = [new("number", "Number"), new("asset", "Asset"), new("priority", "Priority"), new("stage", "Stage")],
            Rows = openWorkOrders.Select(w => new Dictionary<string, object?>
            {
                ["number"] = w.number,
                ["asset"] = w.asset,
                ["priority"] = w.priority,
                ["stage"] = w.stage,
                ["_url"] = AiLinks.WorkOrder(w.id),
                ["_status"] = w.stage,
            }).ToList(),
        };

        return new
        {
            id = vendor.Id,
            name = vendor.Name,
            status = vendor.Status,
            specialization = vendor.Specialization,
            contactName = vendor.ContactName,
            phone = vendor.Phone,
            email = vendor.Email,
            openWorkOrderCount,
            openWorkOrders,
            contracts = contracts.Select(c => new { c.id, c.number, c.type, endDate = D(c.endDate) }),
            ui = openWorkOrders.Count > 0 ? new object[] { card, table } : new object[] { card },
        };
    }
}
