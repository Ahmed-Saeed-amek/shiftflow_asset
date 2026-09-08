using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>Applies a user's effective asset scope (Zone/LocationCategory/Category — each
/// independently optional, combined with AND when more than one is set) wherever assets need to be
/// filtered or checked for a given user. The single source of truth for scope enforcement — was
/// previously duplicated between AssetsController and AssetRepairGuidanceService.
/// A user's own UserAssetScope, if they have one, always wins. With no individual scope, the scope
/// of the single Group they belong to (if any, and if it has one) applies instead — belonging to
/// zero or more-than-one scoped group falls back to unrestricted, same as having no scope at all,
/// rather than guessing which group's restriction should win.</summary>
public interface IAssetScopeService
{
    Task<IQueryable<Asset>> ApplyScopeAsync(IQueryable<Asset> query, string userId);
    Task<bool> IsInScopeAsync(Asset asset, string userId);
    Task<bool> HasScopeAsync(string userId);
}

/// <summary>Common shape a UserAssetScope or GroupAssetScope row is read into for effective-scope
/// resolution — the two entities aren't otherwise related.</summary>
internal readonly record struct EffectiveScope(int? ZoneId, int? LocationCategoryId, int? CategoryId);

public class AssetScopeService : IAssetScopeService
{
    private readonly ApplicationDbContext _db;
    public AssetScopeService(ApplicationDbContext db) { _db = db; }

    private async Task<EffectiveScope?> GetEffectiveScopeAsync(string userId)
    {
        var own = await _db.UserAssetScopes.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        if (own != null) return new EffectiveScope(own.ZoneId, own.LocationCategoryId, own.CategoryId);

        var groupIds = await _db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        if (groupIds.Count == 0) return null;
        var groupScopes = await _db.GroupAssetScopes.AsNoTracking().Where(s => groupIds.Contains(s.GroupId)).ToListAsync();
        // Belonging to more than one scoped group has no unambiguous "most restrictive" or "most
        // permissive" answer without a business rule nobody's asked for — leave unrestricted rather
        // than guess. A single scoped group applies cleanly.
        return groupScopes.Count == 1 ? new EffectiveScope(groupScopes[0].ZoneId, groupScopes[0].LocationCategoryId, groupScopes[0].CategoryId) : null;
    }

    public async Task<IQueryable<Asset>> ApplyScopeAsync(IQueryable<Asset> query, string userId)
    {
        var scope = await GetEffectiveScopeAsync(userId);
        if (scope == null) return query;
        if (scope.Value.ZoneId.HasValue) query = query.Where(a => a.ZoneId == scope.Value.ZoneId);
        if (scope.Value.LocationCategoryId.HasValue) query = query.Where(a => a.Zone!.LocationCategoryId == scope.Value.LocationCategoryId);
        if (scope.Value.CategoryId.HasValue) query = query.Where(a => a.CategoryId == scope.Value.CategoryId || a.Category!.ParentCategoryId == scope.Value.CategoryId);
        return query;
    }

    /// <summary>asset must have Zone and Category loaded.</summary>
    public async Task<bool> IsInScopeAsync(Asset asset, string userId)
    {
        var scope = await GetEffectiveScopeAsync(userId);
        if (scope == null) return true;
        if (scope.Value.ZoneId.HasValue && asset.ZoneId != scope.Value.ZoneId) return false;
        if (scope.Value.LocationCategoryId.HasValue && asset.Zone?.LocationCategoryId != scope.Value.LocationCategoryId) return false;
        if (scope.Value.CategoryId.HasValue && asset.CategoryId != scope.Value.CategoryId && asset.Category?.ParentCategoryId != scope.Value.CategoryId) return false;
        return true;
    }

    public async Task<bool> HasScopeAsync(string userId) => await GetEffectiveScopeAsync(userId) != null;
}
