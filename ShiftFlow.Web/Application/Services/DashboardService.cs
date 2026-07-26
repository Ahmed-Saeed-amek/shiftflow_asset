using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public DashboardService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<DashboardKpis> GetKpisAsync(string? userId = null, string? userRole = null)
    {
        var cacheKey = $"dashboard_kpis:{userRole ?? "all"}";
        if (_cache.TryGetValue(cacheKey, out DashboardKpis? cached) && cached != null)
            return cached;

        var today = DateTime.Today;
        // Sequential — a scoped DbContext cannot run these counts concurrently.
        var totalEngineers = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive);
        var openInspectionOrders = await _db.InspectionOrders.AsNoTracking().CountAsync(o => o.Status != "Done");
        var inspectionOrdersOverdue = await _db.InspectionOrders.AsNoTracking()
            .CountAsync(o => o.Status != "Done" && o.DueDate != null && o.DueDate < today);
        var activeTeams = await _db.Teams.AsNoTracking().CountAsync(t => t.IsActive);
        var totalAssets = await _db.Assets.AsNoTracking().CountAsync();
        var defectiveAssets = await _db.Assets.AsNoTracking().CountAsync(a => a.Status == "Defective");
        var openWorkOrders = await _db.WorkOrders.AsNoTracking().CountAsync(w => w.Stage != "Closed");
        var criticalOpenWorkOrders = await _db.WorkOrders.AsNoTracking().CountAsync(w => w.Stage != "Closed" && w.Priority == "Critical");

        var kpis = new DashboardKpis
        {
            TotalEngineers          = totalEngineers,
            OpenInspectionOrders    = openInspectionOrders,
            InspectionOrdersOverdue = inspectionOrdersOverdue,
            ActiveTeams             = activeTeams,
            TotalAssets             = totalAssets,
            DefectiveAssets         = defectiveAssets,
            OpenWorkOrders          = openWorkOrders,
            CriticalOpenWorkOrders  = criticalOpenWorkOrders,
        };

        _cache.Set(cacheKey, kpis, TimeSpan.FromMinutes(2));
        return kpis;
    }
}
