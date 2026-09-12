using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

/// <summary>Asset-facing tools: search, detail, full history, a computed health summary, defect
/// reporting and status changes, plus the category/action-type lookups the model needs to resolve
/// names it was given into the ids the write tools require.</summary>
public class AiAssetToolFunctions : AiToolsBase
{
    private readonly IWorkOrderService _workOrders;
    private readonly IAssetService _assets;
    private readonly IPendingActionStore _pending;

    public AiAssetToolFunctions(ApplicationDbContext db, IAssetScopeService scope,
        IWorkOrderService workOrders, IAssetService assets, IPendingActionStore pending)
        : base(db, scope)
    {
        _workOrders = workOrders;
        _assets = assets;
        _pending = pending;
    }

    public async Task<object> SearchAssetsAsync(string? query, string? status, int? categoryId, int? zoneId, int? limit,
        string userId, CancellationToken ct)
    {
        var take = Clamp(limit, 10, 50);
        var q = (await ScopedAssetsAsync(userId)).Include(a => a.Zone).Include(a => a.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(a => a.AssetTag.Contains(term) || a.Name.Contains(term)
                || (a.SerialNumber != null && a.SerialNumber.Contains(term))
                || (a.Model != null && a.Model.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        if (categoryId.HasValue) q = q.Where(a => a.CategoryId == categoryId || (a.Category != null && a.Category.ParentCategoryId == categoryId));
        if (zoneId.HasValue) q = q.Where(a => a.ZoneId == zoneId);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(a => a.AssetTag).Take(take)
            .Select(a => new
            {
                id = a.Id,
                assetTag = a.AssetTag,
                name = a.Name,
                zone = a.Zone != null ? a.Zone.Name : null,
                category = a.Category != null ? a.Category.Name : null,
                status = a.Status,
            })
            .ToListAsync(ct);

        var table = new AiTableAttachment
        {
            Title = "Assets",
            Columns =
            [
                new("assetTag", "Tag"), new("name", "Asset"), new("zone", "Zone"), new("status", "Status"),
            ],
            Rows = rows.Select(r => new Dictionary<string, object?>
            {
                ["assetTag"] = r.assetTag,
                ["name"] = r.name,
                ["zone"] = r.zone,
                ["status"] = r.status,
                ["_url"] = AiLinks.Asset(r.id),
                ["_status"] = r.status,
            }).ToList(),
        };

        return new { totalMatches = total, returned = rows.Count, assets = rows, ui = rows.Count > 0 ? (object?)table : null };
    }

    public async Task<object> GetAssetDetailAsync(int? assetId, string? assetTag, string userId, CancellationToken ct)
    {
        var asset = await ResolveAssetAsync(assetId, assetTag, userId, ct);
        if (asset == null) return NotFound("Asset");

        var openWorkOrders = await Db.WorkOrders.CountAsync(w => w.AssetId == asset.Id && OpenWorkOrderStages.Contains(w.Stage), ct);
        var openMaintenance = await Db.MaintenanceOrders.CountAsync(m => m.AssetId == asset.Id && OpenMaintenanceStatuses.Contains(m.Status), ct);
        var contracts = await Db.ContractAssets.AsNoTracking().Where(c => c.AssetId == asset.Id)
            .Select(c => new
            {
                id = c.ContractId,
                type = c.Contract!.ContractType,
                number = c.Contract.ContractNumber,
                vendor = c.Contract.Vendor != null ? c.Contract.Vendor.Name : null,
                endDate = c.Contract.EndDate,
            })
            .ToListAsync(ct);

        var card = new AiCardsAttachment
        {
            Items =
            [
                new AiCard
                {
                    Title = $"{asset.AssetTag} — {asset.Name}",
                    Subtitle = asset.Category?.Name,
                    Badge = new AiBadge(asset.Status, asset.Status),
                    Url = AiLinks.Asset(asset.Id),
                    Fields =
                    [
                        new("Zone", asset.Zone?.Name ?? "—"),
                        new("Manufacturer / Model", $"{asset.Manufacturer ?? "—"} / {asset.Model ?? "—"}"),
                        new("Serial", asset.SerialNumber ?? "—"),
                        new("Warranty expiry", D(asset.WarrantyExpiry) ?? "—"),
                        new("Open work orders", openWorkOrders.ToString()),
                        new("Open maintenance orders", openMaintenance.ToString()),
                        new("Contracts", contracts.Count.ToString()),
                    ],
                },
            ],
        };

        return new
        {
            id = asset.Id,
            assetTag = asset.AssetTag,
            name = asset.Name,
            status = asset.Status,
            category = asset.Category?.Name,
            categoryId = asset.CategoryId,
            zone = asset.Zone?.Name,
            zoneId = asset.ZoneId,
            manufacturer = asset.Manufacturer,
            model = asset.Model,
            serialNumber = asset.SerialNumber,
            sku = asset.Sku,
            purchaseDate = D(asset.PurchaseDate),
            warrantyExpiry = D(asset.WarrantyExpiry),
            assignedTo = asset.AssignedToUser?.FullName,
            openWorkOrders,
            openMaintenanceOrders = openMaintenance,
            contracts = contracts.Select(c => new { c.id, c.type, c.number, c.vendor, endDate = D(c.endDate) }),
            ui = new object[]
            {
                card,
                new AiLinksAttachment { Items = [new AiLinkItem("Open asset", AiLinks.Asset(asset.Id), "box")] },
            },
        };
    }

    public async Task<object> GetAssetHistoryAsync(int assetId, string userId, CancellationToken ct)
    {
        var asset = await FindScopedAssetAsync(assetId, userId, ct);
        if (asset == null) return NotFound("Asset");

        var events = new List<(DateTime When, string Kind, string Reference, string Detail, string? Url, string? Status)>();

        var workOrders = await Db.WorkOrders.AsNoTracking().Where(w => w.AssetId == assetId)
            .Select(w => new { w.Id, w.WorkOrderNumber, w.Stage, w.CreatedDate, w.Priority, Action = w.ActionType!.Name, Cause = w.Cause!.Name })
            .ToListAsync(ct);
        foreach (var w in workOrders)
            events.Add((w.CreatedDate, "Work order", w.WorkOrderNumber,
                $"{w.Priority} priority{(w.Action != null ? $" — {w.Action}" : "")}{(w.Cause != null ? $" ({w.Cause})" : "")}",
                AiLinks.WorkOrder(w.Id), w.Stage));

        var maintenance = await Db.MaintenanceOrders.AsNoTracking().Where(m => m.AssetId == assetId)
            .Select(m => new { m.Id, m.OrderNumber, m.Status, m.CreatedDate, m.Description })
            .ToListAsync(ct);
        foreach (var m in maintenance)
            events.Add((m.CreatedDate, "Maintenance order", m.OrderNumber, m.Description ?? "—", AiLinks.MaintenanceOrder(m.Id), m.Status));

        var inspections = await Db.InspectionRunAssets.AsNoTracking().Where(i => i.AssetId == assetId && i.InspectedAt != null)
            .Select(i => new
            {
                i.Outcome,
                i.InspectedAt,
                OrderId = i.InspectionRun.InspectionOrderId,
                Number = i.InspectionRun.InspectionOrder.OrderNumber,
                By = i.InspectedByUser!.FullName,
            })
            .ToListAsync(ct);
        foreach (var i in inspections)
            events.Add((i.InspectedAt!.Value, "Inspection", i.Number,
                $"Reported {i.Outcome}{(i.By != null ? $" by {i.By}" : "")}", AiLinks.InspectionOrder(i.OrderId), i.Outcome));

        var workOrderParts = await Db.WorkOrderParts.AsNoTracking().Where(p => p.WorkOrder!.AssetId == assetId)
            .Select(p => new
            {
                p.Name,
                p.Quantity,
                p.UnitCostAtUsage,
                When = p.WorkOrder!.FixCompletionDate ?? p.WorkOrder.CreatedDate,
                Number = p.WorkOrder.WorkOrderNumber,
                Id = p.WorkOrderId,
            })
            .ToListAsync(ct);
        foreach (var p in workOrderParts)
            events.Add((p.When, "Part used", p.Number, $"{p.Quantity} × {p.Name} ({Money(p.UnitCostAtUsage)} each)", AiLinks.WorkOrder(p.Id), null));

        var maintenanceParts = await Db.MaintenanceOrderParts.AsNoTracking().Where(p => p.MaintenanceOrder!.AssetId == assetId)
            .Select(p => new
            {
                p.Name,
                p.Quantity,
                p.UnitCostAtUsage,
                When = p.MaintenanceOrder!.CompletedDate ?? p.MaintenanceOrder.CreatedDate,
                Number = p.MaintenanceOrder.OrderNumber,
                Id = p.MaintenanceOrderId,
            })
            .ToListAsync(ct);
        foreach (var p in maintenanceParts)
            events.Add((p.When, "Part used", p.Number, $"{p.Quantity} × {p.Name} ({Money(p.UnitCostAtUsage)} each)", AiLinks.MaintenanceOrder(p.Id), null));

        var contracts = await Db.ContractAssets.AsNoTracking().Where(c => c.AssetId == assetId)
            .Select(c => new
            {
                c.ContractId,
                c.Contract!.ContractType,
                c.Contract.ContractNumber,
                c.Contract.StartDate,
                c.Contract.EndDate,
                Vendor = c.Contract.Vendor!.Name,
            })
            .ToListAsync(ct);
        foreach (var c in contracts)
            events.Add((c.StartDate, "Contract", c.ContractNumber ?? $"#{c.ContractId}",
                $"{c.ContractType}{(c.Vendor != null ? $" — {c.Vendor}" : "")}{(c.EndDate != null ? $", ends {D(c.EndDate)}" : "")}",
                AiLinks.Contract(c.ContractId), null));

        var ordered = events.OrderByDescending(e => e.When).Take(40).ToList();

        var table = new AiTableAttachment
        {
            Title = $"History — {asset.AssetTag}",
            Columns = [new("date", "Date"), new("kind", "Event"), new("reference", "Reference"), new("detail", "Detail")],
            Rows = ordered.Select(e => new Dictionary<string, object?>
            {
                ["date"] = e.When.ToString("yyyy-MM-dd"),
                ["kind"] = e.Kind,
                ["reference"] = e.Reference,
                ["detail"] = e.Detail,
                ["_url"] = e.Url,
                ["_status"] = e.Status,
            }).ToList(),
        };

        return new
        {
            assetId,
            assetTag = asset.AssetTag,
            totalEvents = events.Count,
            timeline = ordered.Select(e => new { date = e.When.ToString("yyyy-MM-dd"), kind = e.Kind, reference = e.Reference, detail = e.Detail, status = e.Status }),
            ui = ordered.Count > 0 ? (object?)table : null,
        };
    }

    public async Task<object> GetAssetHealthSummaryAsync(int assetId, string userId, CancellationToken ct)
    {
        var asset = await FindScopedAssetAsync(assetId, userId, ct, withIncludes: true);
        if (asset == null) return NotFound("Asset");

        var byStage = await Db.WorkOrders.AsNoTracking().Where(w => w.AssetId == assetId)
            .GroupBy(w => w.Stage).Select(g => new { stage = g.Key, count = g.Count() }).ToListAsync(ct);

        var causes = await Db.WorkOrders.AsNoTracking().Where(w => w.AssetId == assetId && w.CauseId != null)
            .GroupBy(w => w.Cause!.Name).Select(g => new { cause = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).Take(5).ToListAsync(ct);

        var lastInspectedAt = await Db.InspectionRunAssets.AsNoTracking()
            .Where(i => i.AssetId == assetId && i.InspectedAt != null)
            .MaxAsync(i => (DateTime?)i.InspectedAt, ct);

        var maintenanceByStatus = await Db.MaintenanceOrders.AsNoTracking().Where(m => m.AssetId == assetId)
            .GroupBy(m => m.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);

        var workOrderSpend = await Db.WorkOrderParts.AsNoTracking().Where(p => p.WorkOrder!.AssetId == assetId)
            .SumAsync(p => (decimal?)(p.Quantity * (p.UnitCostAtUsage ?? 0m)), ct) ?? 0m;
        var maintenanceSpend = await Db.MaintenanceOrderParts.AsNoTracking().Where(p => p.MaintenanceOrder!.AssetId == assetId)
            .SumAsync(p => (decimal?)(p.Quantity * (p.UnitCostAtUsage ?? 0m)), ct) ?? 0m;

        var openWorkOrders = byStage.Where(s => OpenWorkOrderStages.Contains(s.stage)).Sum(s => s.count);

        return new
        {
            assetId,
            assetTag = asset.AssetTag,
            name = asset.Name,
            status = asset.Status,
            zone = asset.Zone?.Name,
            workOrdersByStage = byStage,
            openWorkOrders,
            maintenanceOrdersByStatus = maintenanceByStatus,
            repeatCauses = causes,
            lastInspectedOn = D(lastInspectedAt),
            daysSinceLastInspection = lastInspectedAt.HasValue ? (int)(DateTime.UtcNow.Date - lastInspectedAt.Value.Date).TotalDays : (int?)null,
            partsSpend = workOrderSpend + maintenanceSpend,
            note = "These are raw counts — write the narrative assessment yourself.",
        };
    }

    public async Task<object> ReportAssetDefectAsync(int assetId, int actionTypeId, int? causeId, string? notes,
        string userId, CancellationToken ct)
    {
        // Scope is re-checked inside WorkOrderService.ReportAsync too; checking here first turns an
        // out-of-scope id into a clean "not found" rather than a thrown service error.
        if (await FindScopedAssetAsync(assetId, userId, ct) == null) return NotFound("Asset");

        var wo = await _workOrders.ReportAsync(new WorkOrder
        {
            AssetId = assetId,
            ActionTypeId = actionTypeId,
            CauseId = causeId,
            Notes = notes,
        }, userId);

        return new
        {
            success = true,
            workOrderId = wo.Id,
            workOrderNumber = wo.WorkOrderNumber,
            stage = wo.Stage,
            ui = new AiLinksAttachment { Items = [new AiLinkItem(wo.WorkOrderNumber, AiLinks.WorkOrder(wo.Id), "clipboard")] },
        };
    }

    public async Task<object> UpdateAssetStatusAsync(int assetId, string status, string userId, CancellationToken ct)
    {
        if (!Asset.Statuses.Contains(status))
            return Failed($"Invalid status. Valid values: {string.Join(", ", Asset.Statuses)}.");

        var asset = await FindScopedAssetAsync(assetId, userId, ct);
        if (asset == null) return NotFound("Asset");
        if (asset.Status == status)
            return new { success = true, unchanged = true, message = $"{asset.AssetTag} is already {status}." };

        var summary = $"Change {asset.AssetTag} ({asset.Name}) from {asset.Status} to {status}.";
        var token = _pending.Create(userId, summary, PermissionCatalog.AssetManage, async (sp, innerCt) =>
        {
            var tools = sp.GetRequiredService<AiAssetToolFunctions>();
            return await tools.UpdateAssetStatusConfirmedAsync(assetId, status, userId, innerCt);
        });

        return new
        {
            pendingConfirmation = true,
            token,
            summary,
            ui = new AiConfirmAttachment
            {
                Token = token,
                Title = "Change asset status",
                Summary = summary,
                ActionLabel = $"Set to {status}",
                Danger = status == AssetStatuses.Retired,
            },
        };
    }

    /// <summary>The confirmed half of updateAssetStatus — resolved fresh from the confirming
    /// request's scope, so it re-reads the asset (and re-applies the caller's scope) rather than
    /// trusting anything captured up to 10 minutes earlier.</summary>
    public async Task<object> UpdateAssetStatusConfirmedAsync(int assetId, string status, string userId, CancellationToken ct)
    {
        var asset = await FindScopedAssetAsync(assetId, userId, ct);
        if (asset == null) return NotFound("Asset");

        asset.Status = status;
        await _assets.UpdateAsync(asset, userId);

        return new
        {
            success = true,
            assetId,
            assetTag = asset.AssetTag,
            status,
            ui = new AiLinksAttachment { Items = [new AiLinkItem(asset.AssetTag, AiLinks.Asset(asset.Id), "box")] },
        };
    }

    public async Task<object> ListAssetCategoriesAsync(CancellationToken ct)
    {
        var categories = await Db.AssetCategories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.Id, name = c.Name, parentCategoryId = c.ParentCategoryId })
            .ToListAsync(ct);
        return new { categories };
    }

    public async Task<object> ListActionTypesAsync(int? categoryId, CancellationToken ct)
    {
        var q = Db.AssetActionTypes.AsNoTracking().Where(a => a.IsActive);
        if (categoryId.HasValue)
        {
            // A subcategory-scoped asset also offers its parent category's action types — same
            // inheritance rule the Report Action page uses.
            var parentId = await Db.AssetCategories.Where(c => c.Id == categoryId).Select(c => c.ParentCategoryId).FirstOrDefaultAsync(ct);
            q = q.Where(a => a.CategoryId == categoryId || (parentId != null && a.CategoryId == parentId));
        }

        var actionTypes = await q.OrderBy(a => a.Name)
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                categoryId = a.CategoryId,
                causes = a.Causes.Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new { id = c.Id, name = c.Name }),
            })
            .Take(50)
            .ToListAsync(ct);

        return new { actionTypes };
    }

    private async Task<Asset?> ResolveAssetAsync(int? assetId, string? assetTag, string userId, CancellationToken ct)
    {
        if (assetId.HasValue) return await FindScopedAssetAsync(assetId.Value, userId, ct, withIncludes: true);
        if (string.IsNullOrWhiteSpace(assetTag)) return null;
        var tag = assetTag.Trim();
        var query = (await ScopedAssetsAsync(userId))
            .Include(a => a.Category).Include(a => a.Zone).Include(a => a.AssignedToUser);
        return await query.FirstOrDefaultAsync(a => a.AssetTag == tag, ct);
    }
}
