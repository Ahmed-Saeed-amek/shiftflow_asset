using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.AI;

/// <summary>Zone tools — the assistant's equivalent of the Zone Overview page. Every asset figure
/// comes from the caller's scoped asset set, so a zone-scoped user sees only their own zone's
/// numbers (and an empty overview for a zone they have no assets in).</summary>
public class AiZoneToolFunctions : AiToolsBase
{
    public AiZoneToolFunctions(ApplicationDbContext db, IAssetScopeService scope) : base(db, scope) { }

    public async Task<object> ListZonesAsync(int? locationCategoryId, string? query, CancellationToken ct)
    {
        var q = Db.Zones.AsNoTracking().Include(z => z.LocationCategory).AsQueryable();
        if (locationCategoryId.HasValue) q = q.Where(z => z.LocationCategoryId == locationCategoryId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(z => z.Name.Contains(term) || (z.Address != null && z.Address.Contains(term)));
        }

        var zones = await q.OrderBy(z => z.Name).Take(100)
            .Select(z => new { id = z.Id, name = z.Name, locationCategory = z.LocationCategory != null ? z.LocationCategory.Name : null })
            .ToListAsync(ct);

        return new { count = zones.Count, zones };
    }

    public async Task<object> GetZoneOverviewAsync(int zoneId, string userId, CancellationToken ct)
    {
        var zone = await Db.Zones.AsNoTracking().Include(z => z.LocationCategory).FirstOrDefaultAsync(z => z.Id == zoneId, ct);
        if (zone == null) return NotFound("Zone");

        var scopedAssets = (await ScopedAssetsAsync(userId)).Where(a => a.ZoneId == zoneId);

        var byStatus = await scopedAssets.GroupBy(a => a.Status)
            .Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);

        var defectiveNoOpenOrder = await scopedAssets
            .Where(a => a.Status == AssetStatuses.Defective
                && !Db.WorkOrders.Any(w => w.AssetId == a.Id && OpenWorkOrderStages.Contains(w.Stage))
                && !Db.MaintenanceOrders.Any(m => m.AssetId == a.Id && OpenMaintenanceStatuses.Contains(m.Status)))
            .OrderBy(a => a.AssetTag).Take(10)
            .Select(a => new { id = a.Id, tag = a.AssetTag, name = a.Name })
            .ToListAsync(ct);

        var zoneAssetIds = await scopedAssets.Select(a => a.Id).ToListAsync();

        var latestWorkOrders = await Db.WorkOrders.AsNoTracking()
            .Where(w => zoneAssetIds.Contains(w.AssetId))
            .OrderByDescending(w => w.CreatedDate).Take(5)
            .Select(w => new { id = w.Id, number = w.WorkOrderNumber, stage = w.Stage, asset = w.Asset!.AssetTag, created = w.CreatedDate })
            .ToListAsync(ct);

        var latestMaintenanceOrders = await Db.MaintenanceOrders.AsNoTracking()
            .Where(m => zoneAssetIds.Contains(m.AssetId))
            .OrderByDescending(m => m.CreatedDate).Take(5)
            .Select(m => new { id = m.Id, number = m.OrderNumber, status = m.Status, asset = m.Asset!.AssetTag, created = m.CreatedDate })
            .ToListAsync(ct);

        var statusTable = new AiTableAttachment
        {
            Title = $"{zone.Name} — assets by status",
            Columns = [new("status", "Status"), new("count", "Assets")],
            Rows = byStatus.Select(s => new Dictionary<string, object?>
            {
                ["status"] = s.status,
                ["count"] = s.count,
                ["_status"] = s.status,
            }).ToList(),
        };

        var links = new AiLinksAttachment
        {
            Items = [new AiLinkItem($"Zone overview — {zone.Name}", AiLinks.Zone(zone.Id), "map-pin")],
        };

        return new
        {
            zoneId = zone.Id,
            zone = zone.Name,
            locationCategory = zone.LocationCategory?.Name,
            totalAssets = zoneAssetIds.Count,
            assetsByStatus = byStatus,
            defectiveAssetsWithNoOpenOrder = defectiveNoOpenOrder,
            latestWorkOrders = latestWorkOrders.Select(w => new { w.id, w.number, w.stage, w.asset, created = D(w.created) }),
            latestMaintenanceOrders = latestMaintenanceOrders.Select(m => new { m.id, m.number, m.status, m.asset, created = D(m.created) }),
            ui = new object[] { statusTable, links },
        };
    }
}
