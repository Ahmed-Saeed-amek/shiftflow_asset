using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>Applies a user's UserAssetScope (Zone/LocationCategory/Category — each independently
/// optional, combined with AND when more than one is set) wherever assets need to be filtered or
/// checked for a given user. The single source of truth for scope enforcement — was previously
/// duplicated between AssetsController and AssetRepairGuidanceService.</summary>
public interface IAssetScopeService
{
    Task<IQueryable<Asset>> ApplyScopeAsync(IQueryable<Asset> query, string userId);
    Task<bool> IsInScopeAsync(Asset asset, string userId);
    Task<bool> HasScopeAsync(string userId);
}

public class AssetScopeService : IAssetScopeService
{
    private readonly ApplicationDbContext _db;
    public AssetScopeService(ApplicationDbContext db) { _db = db; }

    public async Task<IQueryable<Asset>> ApplyScopeAsync(IQueryable<Asset> query, string userId)
    {
        var scope = await _db.UserAssetScopes.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        if (scope == null) return query;
        if (scope.ZoneId.HasValue) query = query.Where(a => a.ZoneId == scope.ZoneId);
        if (scope.LocationCategoryId.HasValue) query = query.Where(a => a.Zone!.LocationCategoryId == scope.LocationCategoryId);
        if (scope.CategoryId.HasValue) query = query.Where(a => a.CategoryId == scope.CategoryId || a.Category!.ParentCategoryId == scope.CategoryId);
        return query;
    }

    /// <summary>asset must have Zone and Category loaded.</summary>
    public async Task<bool> IsInScopeAsync(Asset asset, string userId)
    {
        var scope = await _db.UserAssetScopes.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        if (scope == null) return true;
        if (scope.ZoneId.HasValue && asset.ZoneId != scope.ZoneId) return false;
        if (scope.LocationCategoryId.HasValue && asset.Zone?.LocationCategoryId != scope.LocationCategoryId) return false;
        if (scope.CategoryId.HasValue && asset.CategoryId != scope.CategoryId && asset.Category?.ParentCategoryId != scope.CategoryId) return false;
        return true;
    }

    public async Task<bool> HasScopeAsync(string userId) =>
        await _db.UserAssetScopes.AsNoTracking().AnyAsync(s => s.UserId == userId);
}
