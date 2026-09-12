using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.AI;

/// <summary>Shared plumbing for the per-module AI tool classes: the caller's asset scope (the same
/// UserAssetScope every controller applies), the small result shapes the model expects, and a few
/// formatting helpers. Every read in a tool class goes through <see cref="ScopedAssetsAsync"/> or
/// <see cref="ScopedAssetIdsAsync"/> so the assistant can never become a scope-bypass side channel;
/// every write goes through the matching application service so business rules and audits stay in
/// one place.</summary>
public abstract class AiToolsBase
{
    protected readonly ApplicationDbContext Db;
    protected readonly IAssetScopeService Scope;

    protected AiToolsBase(ApplicationDbContext db, IAssetScopeService scope)
    {
        Db = db;
        Scope = scope;
    }

    protected Task<IQueryable<Asset>> ScopedAssetsAsync(string userId) => Scope.GetScopedAssetsAsync(userId);

    /// <summary>The caller's in-scope asset ids, or null when they are unrestricted (in which case
    /// no id filter should be applied at all — materializing every id would be pointless work).</summary>
    protected async Task<List<int>?> ScopedAssetIdsAsync(string userId)
    {
        if (!await Scope.HasScopeAsync(userId)) return null;
        return await (await ScopedAssetsAsync(userId)).Select(a => a.Id).ToListAsync();
    }

    /// <summary>Loads an asset only if it is inside the caller's scope. Never hand a model-supplied
    /// id to Find/FirstOrDefault directly — this is the only lookup path the tools use.</summary>
    protected async Task<Asset?> FindScopedAssetAsync(int assetId, string userId, CancellationToken ct,
        bool withIncludes = false)
    {
        var query = await ScopedAssetsAsync(userId);
        if (withIncludes)
            query = query.Include(a => a.Category).Include(a => a.Zone).ThenInclude(z => z!.LocationCategory)
                         .Include(a => a.AssignedToUser);
        return await query.FirstOrDefaultAsync(a => a.Id == assetId, ct);
    }

    protected static object NotFound(string what) => new { error = "not_found", message = $"{what} not found, or it is outside the assets you have access to." };
    protected static object Failed(string message) => new { error = "action_failed", message };

    protected static int Clamp(int? limit, int fallback = 10, int max = 50) =>
        limit is null or <= 0 ? fallback : Math.Min(limit.Value, max);

    protected static string? D(DateTime? value) => value?.ToString("yyyy-MM-dd");
    protected static string Money(decimal? value) => value.HasValue ? value.Value.ToString("0.##") : "—";

    /// <summary>Work-order stages that still mean "open work exists on this asset".</summary>
    protected static readonly string[] OpenWorkOrderStages = WorkOrderStages.Open;
    protected static readonly string[] OpenMaintenanceStatuses = [OrderStatuses.Open, OrderStatuses.PendingApproval];
    protected static readonly string[] OpenInspectionStatuses = [OrderStatuses.Open, OrderStatuses.InProgress, OrderStatuses.PendingApproval];
}
