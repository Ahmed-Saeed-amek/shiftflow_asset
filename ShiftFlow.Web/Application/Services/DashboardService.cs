using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAssetScopeService _scope;

    public DashboardService(ApplicationDbContext db, IMemoryCache cache, IAssetScopeService scope)
    {
        _db = db;
        _cache = cache;
        _scope = scope;
    }

    public async Task<DashboardKpis> GetKpisAsync(string? userId = null, string? userRole = null)
    {
        // UserAssetScope-restricted users otherwise saw org-wide counts that didn't match what they
        // could actually drill into from the same dashboard (Details/Index already enforce scope —
        // rounds 11-13). Cache key must be per-user once scoped, not per-role, since scope is a
        // per-user setting, not a role-wide one.
        var hasScope = userId != null && await _scope.HasScopeAsync(userId);
        var cacheKey = hasScope ? $"dashboard_kpis:user:{userId}" : $"dashboard_kpis:{userRole ?? "all"}";
        if (_cache.TryGetValue(cacheKey, out DashboardKpis? cached) && cached != null)
            return cached;

        var today = DateTime.UtcNow.Date;
        List<int>? scopedAssetIds = hasScope
            ? await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), userId!)).Select(a => a.Id).ToListAsync()
            : null;

        // Sequential — a scoped DbContext cannot run these counts concurrently.
        var totalEngineers = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive);

        var inspectionQuery = _db.InspectionOrders.AsNoTracking().Where(o => o.Status != "Done" && o.Status != "Cancelled");
        if (scopedAssetIds != null) inspectionQuery = inspectionQuery.Where(o => o.InspectionRun!.Items.All(i => scopedAssetIds.Contains(i.AssetId)));
        var openInspectionOrders = await inspectionQuery.CountAsync();
        var inspectionOrdersOverdue = await inspectionQuery.CountAsync(o => o.DueDate != null && o.DueDate < today);

        var activeGroups = await _db.Groups.AsNoTracking().CountAsync(t => t.IsActive);

        var assetQuery = _db.Assets.AsNoTracking().AsQueryable();
        if (scopedAssetIds != null) assetQuery = assetQuery.Where(a => scopedAssetIds.Contains(a.Id));
        var totalAssets = await assetQuery.CountAsync();
        var defectiveAssets = await assetQuery.CountAsync(a => a.Status == "Defective");

        var workOrderQuery = _db.WorkOrders.AsNoTracking().Where(w => w.Stage != "Closed");
        if (scopedAssetIds != null) workOrderQuery = workOrderQuery.Where(w => scopedAssetIds.Contains(w.AssetId));
        var openWorkOrders = await workOrderQuery.CountAsync();
        var criticalOpenWorkOrders = await workOrderQuery.CountAsync(w => w.Priority == "Critical");

        var lowStockPartsCount = await _db.SpareParts.AsNoTracking()
            .CountAsync(p => p.IsActive && p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold);

        var kpis = new DashboardKpis
        {
            TotalEngineers          = totalEngineers,
            OpenInspectionOrders    = openInspectionOrders,
            InspectionOrdersOverdue = inspectionOrdersOverdue,
            ActiveGroups             = activeGroups,
            TotalAssets             = totalAssets,
            DefectiveAssets         = defectiveAssets,
            OpenWorkOrders          = openWorkOrders,
            CriticalOpenWorkOrders  = criticalOpenWorkOrders,
            LowStockPartsCount      = lowStockPartsCount,
        };

        _cache.Set(cacheKey, kpis, TimeSpan.FromMinutes(2));
        return kpis;
    }
}
