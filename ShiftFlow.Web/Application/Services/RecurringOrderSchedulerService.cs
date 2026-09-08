using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>Generates, per (schedule, linked asset, due date), an InspectionOrder, MaintenanceOrder
/// (per OrderType.IsDirectFix), or — for a RequiresVendor order type — a vendor-routed WorkOrder, for
/// RecurringOrder schedules on their cadence. Same nested (asset x due date) shape and per-tick
/// generation cap as PreventiveMaintenanceSchedulerService. Keeps zero in-memory state: every tick
/// recomputes what's missing purely from RecurringOrder/InspectionOrder/MaintenanceOrder/WorkOrder
/// table contents, so an app restart never loses track of anything.</summary>
public class RecurringOrderSchedulerService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    // Same blast-radius protection PreventiveMaintenanceSchedulerService has (round 26/27): a schedule
    // whose StartDate is far in the past combined with a short cadence and many linked assets can have
    // hundreds or thousands of occurrences already "due" the very first tick after it's created or
    // reactivated. Each one is a real order + AuditLog insert, so cap how many one schedule generates
    // per tick (across all its linked assets combined, same as the PM scheduler's per-contract cap) and
    // let the idempotent re-derivation above catch up the rest over subsequent ticks instead of
    // generating everything synchronously in one pass.
    private const int MaxOccurrencesGeneratedPerSchedulePerTick = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringOrderSchedulerService> _logger;

    public RecurringOrderSchedulerService(IServiceScopeFactory scopeFactory, ILogger<RecurringOrderSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring Order scheduler tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inspectionOrders = scope.ServiceProvider.GetRequiredService<IInspectionOrderService>();
        var maintenanceOrders = scope.ServiceProvider.GetRequiredService<IMaintenanceOrderService>();
        var workOrders = scope.ServiceProvider.GetRequiredService<IWorkOrderService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var today = DateTime.UtcNow.Date;

        var schedules = await db.RecurringOrders
            .Include(r => r.OrderType)
            .Include(r => r.AssetLinks)
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        if (schedules.Count == 0) return;

        string? systemUserId = (await userManager.GetUsersInRoleAsync("Admin")).FirstOrDefault()?.Id;
        if (systemUserId is null)
        {
            _logger.LogWarning("Recurring Order scheduler: no Admin-role user found, skipping this tick.");
            return;
        }

        foreach (var schedule in schedules)
        {
            if (schedule.OrderType is not { IsActive: true }) continue;
            if (schedule.AssetLinks.Count == 0) continue;
            // Mirrors RecurringOrderService.ValidateAsync's routesToVendor rule: only a direct-fix
            // type that also RequiresVendor is actually vendor-routed. For a survey-style (Inspection/
            // Quick Check) type, RequiresVendor governs a different downstream flow (a reported
            // Defective outcome later spawning its own Work Order) — not this schedule's own table.
            var routesToVendor = schedule.OrderType.IsDirectFix && schedule.OrderType.RequiresVendor;
            // A vendor-routed schedule with no VendorId shouldn't exist (RecurringOrderService
            // requires one), but defend against a stale/invalid row rather than generating a vendor
            // work order with no vendor.
            if (routesToVendor && schedule.VendorId == null)
            {
                _logger.LogWarning("Recurring Order: schedule {ScheduleId} requires a vendor but has none set — skipping.", schedule.Id);
                continue;
            }
            var effectiveEnd = schedule.EndDate ?? today;
            var dueSoFar = RecurrenceCalculator.ComputeOccurrenceDueDates(schedule.StartDate, effectiveEnd, schedule.Cadence)
                .Where(d => d <= today).ToList();
            if (dueSoFar.Count == 0) continue;

            // (AssetId, due date) pairs already generated for this schedule, across whichever table
            // its order type routes to — same per-asset dedup shape ContractService's PM schedule view
            // and PreventiveMaintenanceSchedulerService already use.
            var generatedSet = routesToVendor
                ? (await db.WorkOrders.Where(w => w.SourceRecurringOrderId == schedule.Id)
                    .Select(w => new { w.AssetId, w.ScheduledDate }).ToListAsync(ct))
                    .Select(g => (g.AssetId, g.ScheduledDate!.Value.Date)).ToHashSet()
                : schedule.OrderType.IsDirectFix
                    ? (await db.MaintenanceOrders.Where(m => m.SourceRecurringOrderId == schedule.Id)
                        .Select(m => new { m.AssetId, m.ScheduledDate }).ToListAsync(ct))
                        .Select(g => (g.AssetId, g.ScheduledDate!.Value.Date)).ToHashSet()
                    // Each InspectionOrder this scheduler generates covers exactly one asset (one
                    // InspectionRun.Items row) — mirrors the WorkOrder/MaintenanceOrder "one row per
                    // asset per occurrence" shape rather than batching every linked asset into one order.
                    : (await db.InspectionOrders.Where(i => i.SourceRecurringOrderId == schedule.Id)
                        .Select(i => new { AssetId = i.InspectionRun!.Items.Select(it => it.AssetId).FirstOrDefault(), i.ScheduledDate })
                        .ToListAsync(ct))
                        .Select(g => (g.AssetId, g.ScheduledDate!.Value.Date)).ToHashSet();

            var creatorUserId = schedule.CreatedByUserId is { Length: > 0 } ? schedule.CreatedByUserId : systemUserId;
            var generatedThisTick = 0;
            foreach (var link in schedule.AssetLinks)
            {
                if (generatedThisTick >= MaxOccurrencesGeneratedPerSchedulePerTick) break;
                foreach (var dueDate in dueSoFar)
                {
                    if (generatedThisTick >= MaxOccurrencesGeneratedPerSchedulePerTick)
                    {
                        _logger.LogWarning("Recurring Order: schedule {ScheduleId} has more than {Max} occurrences still to generate — deferring the rest to the next tick.",
                            schedule.Id, MaxOccurrencesGeneratedPerSchedulePerTick);
                        break;
                    }
                    if (generatedSet.Contains((link.AssetId, dueDate))) continue;
                    try
                    {
                        if (routesToVendor)
                        {
                            await workOrders.CreateRecurringVendorOccurrenceAsync(link.AssetId, schedule.VendorId!.Value,
                                schedule.AssignedToUserId, schedule.Id, dueDate, creatorUserId);
                        }
                        else if (schedule.OrderType.IsDirectFix)
                        {
                            await maintenanceOrders.CreateAsync(link.AssetId, schedule.AssignedToUserId, schedule.AssignedToGroupId,
                                null, dueDate, creatorUserId, schedule.OrderTypeId, schedule.Id, dueDate);
                        }
                        else
                        {
                            await inspectionOrders.CreateAsync(schedule.OrderTypeId, null, schedule.AssignedToUserId, schedule.AssignedToGroupId,
                                [link.AssetId], dueDate, creatorUserId, schedule.Id, dueDate);
                        }
                        generatedThisTick++;
                    }
                    catch (DbUpdateException ex)
                    {
                        // Filtered unique index on (SourceRecurringOrderId, [AssetId,] ScheduledDate)
                        // rejects a duplicate — the safety net for a multi-instance deployment racing
                        // on the same tick.
                        _logger.LogWarning(ex, "Recurring Order: occurrence for schedule {ScheduleId}, asset {AssetId}, due {DueDate} was not created (likely already generated).",
                            schedule.Id, link.AssetId, dueDate);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // e.g. the asset was retired after being linked to this schedule — log and move
                        // on rather than letting one bad asset link block every other link's occurrences
                        // this tick (and every tick thereafter).
                        _logger.LogWarning(ex, "Recurring Order: occurrence for schedule {ScheduleId}, asset {AssetId}, due {DueDate} failed: {Message}",
                            schedule.Id, link.AssetId, dueDate, ex.Message);
                    }
                }
            }
        }
    }
}
