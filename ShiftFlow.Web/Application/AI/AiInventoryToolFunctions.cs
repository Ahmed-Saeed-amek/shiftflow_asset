using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

/// <summary>Spare-parts (inventory) tools. The catalog scope rule mirrors
/// SparePartsController.ScopedPartsAsync exactly: an unrestricted user sees every part; a scoped
/// user sees parts linked to at least one in-scope asset, plus unlinked generic consumables.</summary>
public class AiInventoryToolFunctions : AiToolsBase
{
    private readonly ISparePartService _parts;
    private readonly IPendingActionStore _pending;

    public AiInventoryToolFunctions(ApplicationDbContext db, IAssetScopeService scope,
        ISparePartService parts, IPendingActionStore pending) : base(db, scope)
    {
        _parts = parts;
        _pending = pending;
    }

    private async Task<IQueryable<SparePart>> ScopedPartsAsync(string userId)
    {
        var query = Db.SpareParts.AsNoTracking().AsQueryable();
        if (!await Scope.HasScopeAsync(userId)) return query;
        var scopedAssets = await ScopedAssetsAsync(userId);
        return query.Where(p => !p.AssetLinks.Any() || p.AssetLinks.Any(l => scopedAssets.Any(a => a.Id == l.AssetId)));
    }

    public async Task<object> SearchSparePartsAsync(string? query, bool? lowStockOnly, int? limit, string userId, CancellationToken ct)
    {
        var take = Clamp(limit, 10, 50);
        var q = await ScopedPartsAsync(userId);

        if (lowStockOnly == true) q = q.Where(p => p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(p => p.Name.Contains(term) || (p.Sku != null && p.Sku.Contains(term)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(p => p.Name).Take(take)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                sku = p.Sku,
                stockQuantity = p.StockQuantity,
                reorderThreshold = p.ReorderThreshold,
                unitCost = p.UnitCost,
                isActive = p.IsActive,
            })
            .ToListAsync(ct);

        return new { totalMatches = total, returned = rows.Count, parts = rows, ui = rows.Count > 0 ? (object?)PartsTable("Spare parts", rows.Select(r => (r.id, r.name, r.sku, r.stockQuantity, r.reorderThreshold))) : null };
    }

    public async Task<object> GetSparePartDetailAsync(int id, string userId, CancellationToken ct)
    {
        var part = await (await ScopedPartsAsync(userId)).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (part == null) return NotFound("Spare part");

        // Only the links the caller may see — the raw AssetLinks collection names every asset the
        // part fits, in or out of scope.
        var scopedAssets = await ScopedAssetsAsync(userId);
        var linkedAssets = await Db.SparePartAssets.AsNoTracking()
            .Where(l => l.SparePartId == id && scopedAssets.Any(a => a.Id == l.AssetId))
            .Select(l => new { id = l.AssetId, tag = l.Asset!.AssetTag, name = l.Asset.Name })
            .Take(25).ToListAsync(ct);

        var card = new AiCardsAttachment
        {
            Items =
            [
                new AiCard
                {
                    Title = part.Name,
                    Subtitle = part.Sku,
                    Badge = new AiBadge($"{part.StockQuantity} in stock", IsLow(part) ? "Defective" : "Working"),
                    Url = AiLinks.SparePart(part.Id),
                    Fields =
                    [
                        new("Unit cost", Money(part.UnitCost)),
                        new("Reorder threshold", part.ReorderThreshold?.ToString() ?? "—"),
                        new("Active", part.IsActive ? "Yes" : "No"),
                        new("Compatible assets (in your scope)", linkedAssets.Count.ToString()),
                    ],
                },
            ],
        };

        return new
        {
            id = part.Id,
            name = part.Name,
            sku = part.Sku,
            stockQuantity = part.StockQuantity,
            reorderThreshold = part.ReorderThreshold,
            unitCost = part.UnitCost,
            isActive = part.IsActive,
            isLowStock = IsLow(part),
            compatibleAssets = linkedAssets,
            ui = card,
        };
    }

    public async Task<object> GetCompatiblePartsAsync(int assetId, string userId, CancellationToken ct)
    {
        if (await FindScopedAssetAsync(assetId, userId, ct) == null) return NotFound("Asset");

        var parts = await _parts.GetCompatiblePartsAsync(assetId);
        var rows = parts.Select(p => (p.Id, p.Name, p.Sku, p.StockQuantity, p.ReorderThreshold)).ToList();
        return new
        {
            assetId,
            parts = parts.Select(p => new { id = p.Id, name = p.Name, sku = p.Sku, stockQuantity = p.StockQuantity, unitCost = p.UnitCost }),
            ui = rows.Count > 0 ? (object?)PartsTable("Compatible parts", rows) : null,
        };
    }

    public async Task<object> GetLowStockPartsAsync(string userId, CancellationToken ct)
    {
        var parts = await (await ScopedPartsAsync(userId))
            .Where(p => p.IsActive && p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold)
            .OrderBy(p => p.StockQuantity)
            .Take(50)
            .Select(p => new { id = p.Id, name = p.Name, sku = p.Sku, stockQuantity = p.StockQuantity, reorderThreshold = p.ReorderThreshold })
            .ToListAsync(ct);

        return new
        {
            count = parts.Count,
            parts,
            ui = parts.Count > 0
                ? (object?)PartsTable("Low stock", parts.Select(p => (p.id, p.name, p.sku, p.stockQuantity, p.reorderThreshold)))
                : null,
        };
    }

    /// <summary>Stock adjustment. Confirmation is required when the change is large — more than
    /// half the current stock — or when it zeroes the part out, since those are the adjustments a
    /// misheard number would do real damage with.</summary>
    public async Task<object> AdjustSparePartStockAsync(int id, int newQuantity, string? reason, string userId, CancellationToken ct)
    {
        if (newQuantity < 0) return Failed("Stock quantity can't be negative.");

        var part = await (await ScopedPartsAsync(userId)).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (part == null) return NotFound("Spare part");

        var delta = Math.Abs(newQuantity - part.StockQuantity);
        var needsConfirm = newQuantity == 0 || (part.StockQuantity > 0 && delta * 2 > part.StockQuantity);

        if (!needsConfirm)
        {
            await _parts.AdjustStockAsync(id, newQuantity, reason, userId);
            return Adjusted(part, newQuantity);
        }

        var summary = $"Set {part.Name} stock from {part.StockQuantity} to {newQuantity}{(string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}")}.";
        var token = _pending.Create(userId, summary, PermissionCatalog.SparePartManage, async (sp, innerCt) =>
        {
            var tools = sp.GetRequiredService<AiInventoryToolFunctions>();
            return await tools.AdjustSparePartStockConfirmedAsync(id, newQuantity, reason, userId, innerCt);
        });

        return new
        {
            pendingConfirmation = true,
            token,
            summary,
            ui = new AiConfirmAttachment
            {
                Token = token,
                Title = "Adjust stock",
                Summary = summary,
                ActionLabel = "Adjust stock",
                Danger = newQuantity == 0,
            },
        };
    }

    public async Task<object> AdjustSparePartStockConfirmedAsync(int id, int newQuantity, string? reason, string userId, CancellationToken ct)
    {
        var part = await (await ScopedPartsAsync(userId)).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (part == null) return NotFound("Spare part");
        await _parts.AdjustStockAsync(id, newQuantity, reason, userId);
        return Adjusted(part, newQuantity);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static bool IsLow(SparePart p) => p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold;

    private static object Adjusted(SparePart part, int newQuantity) => new
    {
        success = true,
        id = part.Id,
        name = part.Name,
        previousQuantity = part.StockQuantity,
        newQuantity,
        ui = new AiLinksAttachment { Items = [new AiLinkItem(part.Name, AiLinks.SparePart(part.Id), "package")] },
    };

    private static AiTableAttachment PartsTable(string title, IEnumerable<(int Id, string Name, string? Sku, int Stock, int? Threshold)> rows) => new()
    {
        Title = title,
        Columns = [new("name", "Part"), new("sku", "SKU"), new("stock", "In stock"), new("threshold", "Reorder at")],
        Rows = rows.Select(r => new Dictionary<string, object?>
        {
            ["name"] = r.Name,
            ["sku"] = r.Sku ?? "—",
            ["stock"] = r.Stock,
            ["threshold"] = r.Threshold?.ToString() ?? "—",
            ["_url"] = AiLinks.SparePart(r.Id),
            ["_status"] = r.Threshold != null && r.Stock <= r.Threshold ? "Defective" : "Working",
        }).ToList(),
    };
}
