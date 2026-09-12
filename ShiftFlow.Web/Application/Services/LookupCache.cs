using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>The handful of small, rarely-changing reference tables every Create/Edit form's
/// PopulateLookupsAsync re-queried on each GET and each failed POST. Entities are read
/// AsNoTracking, so the cached graphs are detached and safe to share across requests (no lazy
/// loading is configured). The CRUD screens that own these tables call Invalidate on save.</summary>
public interface ILookupCache
{
    /// <summary>Top-level asset categories with their subcategories loaded.</summary>
    Task<List<AssetCategory>> TopLevelCategoriesAsync();
    Task<List<Zone>> ZonesAsync();
    Task<List<LocationCategory>> LocationCategoriesAsync();
    Task<List<Vendor>> ActiveVendorsAsync();
    Task<List<Group>> ActiveGroupsAsync();
    Task<List<OrderType>> ActiveOrderTypesAsync();

    void InvalidateCategories();
    void InvalidateZones();
    void InvalidateVendors();
    void InvalidateGroups();
    void InvalidateOrderTypes();
}

public sealed class LookupCache : ILookupCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const string KeyCategories = "lookup:categories";
    private const string KeyZones = "lookup:zones";
    private const string KeyLocationCategories = "lookup:locationCategories";
    private const string KeyVendors = "lookup:vendors";
    private const string KeyGroups = "lookup:groups";
    private const string KeyOrderTypes = "lookup:orderTypes";

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    public LookupCache(ApplicationDbContext db, IMemoryCache cache) { _db = db; _cache = cache; }

    private async Task<List<T>> GetAsync<T>(string key, Func<Task<List<T>>> load)
    {
        if (_cache.TryGetValue(key, out List<T>? cached) && cached != null) return cached;
        var loaded = await load();
        _cache.Set(key, loaded, Ttl);
        return loaded;
    }

    public Task<List<AssetCategory>> TopLevelCategoriesAsync() => GetAsync(KeyCategories, () =>
        _db.AssetCategories.AsNoTracking().Include(c => c.Subcategories)
            .Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync());

    public Task<List<Zone>> ZonesAsync() => GetAsync(KeyZones, () =>
        _db.Zones.AsNoTracking().Include(z => z.LocationCategory)
            .OrderBy(z => z.LocationCategory!.Name).ThenBy(z => z.Name).ToListAsync());

    public Task<List<LocationCategory>> LocationCategoriesAsync() => GetAsync(KeyLocationCategories, () =>
        _db.LocationCategories.AsNoTracking().OrderBy(c => c.Id).ToListAsync());

    public Task<List<Vendor>> ActiveVendorsAsync() => GetAsync(KeyVendors, () =>
        _db.Vendors.AsNoTracking().Where(v => v.Status == "Active").OrderBy(v => v.Name).ToListAsync());

    public Task<List<Group>> ActiveGroupsAsync() => GetAsync(KeyGroups, () =>
        _db.Groups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.Name).ToListAsync());

    public Task<List<OrderType>> ActiveOrderTypesAsync() => GetAsync(KeyOrderTypes, () =>
        _db.OrderTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync());

    public void InvalidateCategories() => _cache.Remove(KeyCategories);
    public void InvalidateZones() { _cache.Remove(KeyZones); _cache.Remove(KeyLocationCategories); }
    public void InvalidateVendors() => _cache.Remove(KeyVendors);
    public void InvalidateGroups() => _cache.Remove(KeyGroups);
    public void InvalidateOrderTypes() => _cache.Remove(KeyOrderTypes);
}

public static class LookupCacheServiceCollectionExtensions
{
    /// <summary>Register with <c>builder.Services.AddLookupCache();</c> in Program.cs.</summary>
    public static IServiceCollection AddLookupCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<ILookupCache, LookupCache>();
        return services;
    }
}
