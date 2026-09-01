using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class SparePartService : ISparePartService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    public SparePartService(ApplicationDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    public async Task<SparePart> CreateAsync(SparePart part, List<int> assetIds, string userId)
    {
        part.CreatedDate = DateTime.UtcNow;
        _db.SpareParts.Add(part);
        await _db.SaveChangesAsync(); // need part.Id before inserting links
        foreach (var assetId in assetIds.Distinct())
            _db.SparePartAssets.Add(new SparePartAsset { SparePartId = part.Id, AssetId = assetId });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "SparePart", part.Id.ToString(), userId, newValue: part.Name);
        return part;
    }

    public async Task UpdateAsync(SparePart part, List<int> assetIds, string userId)
    {
        var existing = await _db.SpareParts.Include(p => p.AssetLinks).FirstOrDefaultAsync(p => p.Id == part.Id)
            ?? throw new InvalidOperationException("Spare part not found.");
        existing.Name = part.Name; existing.NameAr = part.NameAr; existing.Sku = part.Sku;
        existing.UnitCost = part.UnitCost; existing.ReorderThreshold = part.ReorderThreshold; existing.IsActive = part.IsActive;
        // StockQuantity deliberately NOT touched here - stock changes only via AdjustStockAsync, so a
        // catalog-metadata edit can never accidentally alter the on-hand count.

        var toRemove = existing.AssetLinks.Where(l => !assetIds.Contains(l.AssetId)).ToList();
        var toAdd = assetIds.Where(id => !existing.AssetLinks.Any(l => l.AssetId == id))
            .Select(id => new SparePartAsset { SparePartId = existing.Id, AssetId = id });
        _db.SparePartAssets.RemoveRange(toRemove);
        _db.SparePartAssets.AddRange(toAdd);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "SparePart", existing.Id.ToString(), userId, newValue: existing.Name);
    }

    public async Task AdjustStockAsync(int sparePartId, int newQuantity, string? reason, string userId)
    {
        if (newQuantity < 0) throw new InvalidOperationException("Stock quantity cannot be negative.");
        var part = await _db.SpareParts.FindAsync(sparePartId) ?? throw new InvalidOperationException("Spare part not found.");
        var old = part.StockQuantity;
        part.StockQuantity = newQuantity;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AdjustStock", "SparePart", part.Id.ToString(), userId,
            oldValue: old.ToString(), newValue: newQuantity.ToString(), details: reason);
    }

    public async Task<List<SparePart>> GetCompatiblePartsAsync(int assetId) =>
        await _db.SparePartAssets.AsNoTracking()
            .Where(sa => sa.AssetId == assetId && sa.SparePart!.IsActive)
            .Select(sa => sa.SparePart!)
            .OrderBy(p => p.Name)
            .ToListAsync();

    // Same TOCTOU-safe ExecuteUpdateAsync-with-WHERE-guard pattern already used for WorkOrder stage
    // transitions - the WHERE clause is evaluated atomically by the database, so two concurrent fix
    // submissions consuming the last unit of the same part can't both succeed and drive stock negative.
    public async Task<bool> TryDecrementStockAsync(int sparePartId, int quantity)
    {
        if (quantity <= 0) return true;
        var rows = await _db.SpareParts
            .Where(p => p.Id == sparePartId && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity));
        return rows > 0;
    }
}
