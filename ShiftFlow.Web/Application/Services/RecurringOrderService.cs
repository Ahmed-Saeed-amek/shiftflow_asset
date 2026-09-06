using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>Mirrors ContractService's shape almost exactly (asset-link diffing, the same lost-update
/// guard via originalAssetIds), generalized to any active Order Type rather than just vendor PM
/// contracts — see RecurringOrder's own doc comment for how the two features now divide the work.</summary>
public class RecurringOrderService : IRecurringOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAssetScopeService _scope;
    public RecurringOrderService(ApplicationDbContext db, IAssetScopeService scope) { _db = db; _scope = scope; }

    private async Task ValidateAsync(RecurringOrder schedule, List<int> assetIds, string actingUserId)
    {
        var orderType = await _db.OrderTypes.FindAsync(schedule.OrderTypeId)
            ?? throw new InvalidOperationException("Selected order type not found.");
        if (!orderType.IsActive)
            throw new InvalidOperationException("Selected order type is not active.");
        if (!RecurringOrder.Cadences.Contains(schedule.Cadence))
            throw new InvalidOperationException("Invalid cadence.");

        var hasUser = !string.IsNullOrEmpty(schedule.AssignedToUserId);
        var hasTeam = schedule.AssignedToTeamId.HasValue;
        if (orderType.RequiresVendor)
        {
            // Mirrors OrdersController.Create's manual RequiresVendor branch exactly: needs an
            // individual employee overseeing the vendor's work (no team-assignment concept for a
            // vendor-routed work order) plus the vendor itself.
            if (!hasUser || hasTeam)
                throw new InvalidOperationException("This order type requires a vendor, which needs an individual employee assignee — team assignment isn't supported for vendor-routed work orders yet.");
            if (schedule.VendorId == null || !await _db.Vendors.AnyAsync(v => v.Id == schedule.VendorId && v.Status == "Active"))
                throw new InvalidOperationException("Selected vendor not found or inactive.");
        }
        else
        {
            if (hasUser == hasTeam)
                throw new InvalidOperationException("Exactly one assignee (employee or team) is required.");
            schedule.VendorId = null;
        }
        // Same as RecurringOrdersController's old check: can't just silently drop whichever side the
        // OrderType's AssignmentMode disallows — reject the mismatch instead so a TeamOnly type never
        // ends up with an individual employee baked into every future occurrence. RequiresVendor
        // overrides AssignmentMode's team preference entirely (handled above), so TeamOnly doesn't
        // apply once a type is also vendor-required.
        if (orderType.AssignmentMode == "EmployeeOnly" && hasTeam)
            throw new InvalidOperationException($"'{orderType.Name}' can only be assigned to an employee, not a team.");
        if (orderType.AssignmentMode == "TeamOnly" && hasUser && !orderType.RequiresVendor)
            throw new InvalidOperationException($"'{orderType.Name}' can only be assigned to a team, not an employee.");

        // Same bug class as ContractService's VendorId/AssetId checks: a stale multi-select or
        // tampered POST with a non-existent employee/team id otherwise hits the DB's FK constraint
        // on RecurringOrders.AssignedToUserId/AssignedToTeamId and raises an unhandled
        // DbUpdateException instead of a clean message.
        if (hasUser && !await _db.Users.AnyAsync(u => u.Id == schedule.AssignedToUserId && u.IsActive))
            throw new InvalidOperationException("Selected employee not found or is inactive.");
        if (hasTeam && !await _db.Teams.AnyAsync(t => t.Id == schedule.AssignedToTeamId))
            throw new InvalidOperationException("Selected team not found.");

        if (assetIds.Count == 0)
            throw new InvalidOperationException("Select at least one asset.");
        // Mirrors OrdersController.Create's cardinality resolution: an AllowsMultipleAssets-false type
        // (e.g. Maintenance, tied to spare-part usage per asset) is limited to exactly one asset. The
        // controller already resolves this server-side from the OrderType's own flag rather than
        // trusting whichever picker the client posted — this is the defense-in-depth backstop against
        // a raw/tampered POST bypassing that resolution.
        if (!orderType.AllowsMultipleAssets && assetIds.Count > 1)
            throw new InvalidOperationException("This order type only supports a single asset — select just one.");
        if (await _db.Assets.CountAsync(a => assetIds.Contains(a.Id)) != assetIds.Distinct().Count())
            throw new InvalidOperationException("One or more selected assets were not found.");
        if (await _db.Assets.AnyAsync(a => assetIds.Contains(a.Id) && a.Status == "Retired"))
            throw new InvalidOperationException("One or more selected assets are retired and can't be scheduled for new orders.");
        // Same UserAssetScope enforcement round 11 added to manual Order creation — without it, a
        // scoped OrderType.Manage holder could schedule a recurring order against an asset they can't
        // even view via AssetsController, and the scheduler would then keep auto-generating real
        // orders against it indefinitely.
        var scopedIds = await (await _scope.ApplyScopeAsync(_db.Assets.AsQueryable(), actingUserId)).Select(a => a.Id).ToListAsync();
        if (assetIds.Except(scopedIds).Any())
            throw new InvalidOperationException("One or more selected assets were not found.");
    }

    public async Task<RecurringOrder> CreateAsync(RecurringOrder schedule, List<int> assetIds, string userId)
    {
        await ValidateAsync(schedule, assetIds, userId);
        schedule.CreatedByUserId = userId;
        schedule.CreatedDate = DateTime.UtcNow;
        schedule.AssetLinks = assetIds.Select(id => new RecurringOrderAsset { AssetId = id }).ToList();
        _db.RecurringOrders.Add(schedule);
        await _db.SaveChangesAsync();
        return schedule;
    }

    public async Task UpdateAsync(RecurringOrder schedule, List<int> assetIds, List<int> originalAssetIds, string userId)
    {
        var existing = await _db.RecurringOrders.Include(r => r.AssetLinks).FirstOrDefaultAsync(r => r.Id == schedule.Id)
            ?? throw new InvalidOperationException("Recurring order schedule not found.");
        // Same lost-update race ContractService.UpdateAsync guards against: diffs the posted asset
        // list against whatever is live right now, with no check that the editor's page snapshot is
        // still current — reject the submit if the schedule's linked assets no longer match what the
        // edit form was loaded with, instead of a concurrent edit silently discarding someone else's change.
        var currentAssetIds = existing.AssetLinks.Select(l => l.AssetId).ToHashSet();
        if (!currentAssetIds.SetEquals(originalAssetIds.Distinct()))
            throw new InvalidOperationException("This schedule's linked assets were changed by someone else since you opened this page. Reload and try again.");

        await ValidateAsync(schedule, assetIds, userId);

        existing.OrderTypeId = schedule.OrderTypeId;
        existing.AssignedToUserId = schedule.AssignedToUserId;
        existing.AssignedToTeamId = schedule.AssignedToTeamId;
        existing.VendorId = schedule.VendorId;
        existing.Cadence = schedule.Cadence;
        existing.StartDate = schedule.StartDate;
        existing.EndDate = schedule.EndDate;
        existing.IsActive = schedule.IsActive;

        var toRemove = existing.AssetLinks.Where(l => !assetIds.Contains(l.AssetId)).ToList();
        var toAdd = assetIds.Where(id => !currentAssetIds.Contains(id)).Select(id => new RecurringOrderAsset { RecurringOrderId = existing.Id, AssetId = id });
        _db.RecurringOrderAssets.RemoveRange(toRemove);
        _db.RecurringOrderAssets.AddRange(toAdd);

        await _db.SaveChangesAsync();
    }
}
