using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>Generates Inspection/Maintenance orders for RecurringOrder schedules on their cadence, one
/// per (recurring order, due date) — same shape as PreventiveMaintenanceSchedulerService. Keeps zero
/// in-memory state: every tick recomputes what's missing purely from RecurringOrder/InspectionOrder/
/// MaintenanceOrder table contents, so an app restart never loses track of anything.</summary>
public class RecurringOrderSchedulerService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

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
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var today = DateTime.UtcNow.Date;

        var schedules = await db.RecurringOrders
            .Include(r => r.OrderType)
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
            // RequiresVendor types route through IWorkOrderService (see OrdersController.Create),
            // which this generator doesn't support — RecurringOrdersController blocks creating new
            // schedules for one, but skip defensively in case an old/invalid row still exists rather
            // than silently mis-generating a plain MaintenanceOrder/InspectionOrder for it.
            if (schedule.OrderType.RequiresVendor)
            {
                _logger.LogWarning("Recurring Order: schedule {ScheduleId} is for a RequiresVendor order type, which this generator doesn't support — skipping.", schedule.Id);
                continue;
            }
            var effectiveEnd = schedule.EndDate ?? today;
            var dueSoFar = RecurrenceCalculator.ComputeOccurrenceDueDates(schedule.StartDate, effectiveEnd, schedule.Cadence)
                .Where(d => d <= today).ToList();
            if (dueSoFar.Count == 0) continue;

            var generatedDates = schedule.OrderType.IsDirectFix
                ? (await db.MaintenanceOrders.Where(m => m.SourceRecurringOrderId == schedule.Id)
                    .Select(m => m.ScheduledDate!.Value.Date).ToListAsync(ct)).ToHashSet()
                : (await db.InspectionOrders.Where(i => i.SourceRecurringOrderId == schedule.Id)
                    .Select(i => i.ScheduledDate!.Value.Date).ToListAsync(ct)).ToHashSet();

            foreach (var dueDate in dueSoFar)
            {
                if (generatedDates.Contains(dueDate)) continue;
                try
                {
                    var creatorUserId = schedule.CreatedByUserId is { Length: > 0 } ? schedule.CreatedByUserId : systemUserId;
                    if (schedule.OrderType.IsDirectFix)
                    {
                        await maintenanceOrders.CreateAsync(schedule.AssetId, schedule.AssignedToUserId, schedule.AssignedToTeamId,
                            null, dueDate, creatorUserId, schedule.OrderTypeId, schedule.Id, dueDate);
                    }
                    else
                    {
                        await inspectionOrders.CreateAsync(schedule.OrderTypeId, null, schedule.AssignedToUserId, schedule.AssignedToTeamId,
                            [schedule.AssetId], dueDate, creatorUserId, schedule.Id, dueDate);
                    }
                }
                catch (DbUpdateException ex)
                {
                    // Filtered unique index on (SourceRecurringOrderId, ScheduledDate) rejects a
                    // duplicate — the safety net for a multi-instance deployment racing on the same tick.
                    _logger.LogWarning(ex, "Recurring Order: occurrence for schedule {ScheduleId}, due {DueDate} was not created (likely already generated).",
                        schedule.Id, dueDate);
                }
                catch (InvalidOperationException ex)
                {
                    // e.g. the target asset was retired after the schedule was created — log and move
                    // on rather than letting one bad schedule block every other schedule's occurrences
                    // this tick (and every tick thereafter, since the same failure would recur).
                    _logger.LogWarning(ex, "Recurring Order: occurrence for schedule {ScheduleId}, due {DueDate} failed: {Message}",
                        schedule.Id, dueDate, ex.Message);
                }
            }
        }
    }
}
