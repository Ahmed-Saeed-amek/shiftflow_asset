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

    // Same blast-radius cap as PreventiveMaintenanceSchedulerService: a schedule starting far in the
    // past with a short cadence and many assets has thousands of occurrences due on its first tick.
    // Generation is idempotent, so the remainder is simply picked up by later ticks.
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
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
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
            // Nothing at all can be generated in this state, so it is an error, not a warning.
            _logger.LogError("Recurring Order scheduler: no Admin-role user found — no occurrences can be generated this tick.");
            return;
        }

        // A generation failure used to leave only a log line nobody reads. Every failure below is
        // also written to the audit log against the schedule, so it is visible on the Audit Logs
        // screen (filter EntityType = RecurringOrder) without shell access to the server.
        async Task ReportFailureAsync(int scheduleId, string message)
        {
            _logger.LogError("Recurring Order: schedule {ScheduleId} failed to generate an occurrence: {Message}", scheduleId, message);
            try
            {
                await audit.LogAsync("GenerationFailed", "RecurringOrder", scheduleId.ToString(), systemUserId, details: message);
            }
            catch (Exception auditEx)
            {
                // Never let audit logging itself break the tick.
                _logger.LogError(auditEx, "Recurring Order: could not write the failure audit row for schedule {ScheduleId}.", scheduleId);
            }
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
                await ReportFailureAsync(schedule.Id, "This schedule's order type requires a vendor but the schedule has none set.");
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

            // Occurrences are generated on behalf of the schedule, not of whoever happened to
            // create it: if that user has since been deactivated or had their asset scope narrowed,
            // the downstream services would reject every asset on the schedule. Fall back to the
            // system (Admin) account in that case so an administrative change to one employee never
            // silently stops a schedule. See NOTES-data.md for the system-context create signature
            // this should use once InspectionOrderService/MaintenanceOrderService expose one.
            var creator = schedule.CreatedByUserId is { Length: > 0 } ? schedule.CreatedByUserId : null;
            var creatorIsUsable = creator != null && await db.Users.AnyAsync(u => u.Id == creator && u.IsActive, ct);
            if (creator != null && !creatorIsUsable)
                await ReportFailureAsync(schedule.Id, $"The employee who created this schedule ({creator}) is inactive — generating as the system account instead.");
            var creatorUserId = creatorIsUsable ? creator! : systemUserId;

            // An assignee that has since been deactivated makes every occurrence unassignable.
            if (schedule.AssignedToUserId is { Length: > 0 } assignee
                && !await db.Users.AnyAsync(u => u.Id == assignee && u.IsActive, ct))
            {
                await ReportFailureAsync(schedule.Id, "This schedule's assigned employee is no longer active — reassign the schedule to resume generating orders.");
                continue;
            }
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
                        // on the same tick. Expected, so this one stays a warning.
                        _logger.LogWarning(ex, "Recurring Order: occurrence for schedule {ScheduleId}, asset {AssetId}, due {DueDate} was not created (likely already generated).",
                            schedule.Id, link.AssetId, dueDate);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // e.g. the asset was retired after being linked to this schedule — surfaced,
                        // then skipped, so one bad link can't block the rest of the schedule forever.
                        await ReportFailureAsync(schedule.Id,
                            $"Asset {link.AssetId}, due {dueDate:yyyy-MM-dd}: {ex.Message}");
                    }
                }
            }
        }
    }
}
