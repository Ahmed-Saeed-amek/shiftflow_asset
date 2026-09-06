using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>Generates work orders for Preventive Maintenance contracts on their recurring schedule, one
/// per (contract, asset, due date). Keeps zero in-memory state — every tick recomputes what's missing purely
/// from Contract/ContractAsset/WorkOrder table contents, so an app restart (e.g. an IIS app-pool recycle)
/// never loses track of anything: the very next tick re-derives and catches up on whatever was missed while
/// the process was down.</summary>
public class PreventiveMaintenanceSchedulerService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    // Round 26 capped ComputeOccurrenceDueDates at 2000 occurrences, but that only bounds the date
    // dimension — this loop nests assets *outside* dates, so the real per-tick blast radius is
    // occurrences x linked-assets. A PM contract near that cap linked to a few hundred assets (one
    // click via the asset picker's "add all in category") generates hundreds of thousands of
    // WorkOrder/AuditLog inserts synchronously in a single tick (confirmed live: 105 occurrences x
    // 3 assets = 315 WorkOrders from one tick). Capping WorkOrders created per contract per tick
    // spreads a large backlog across multiple 5-minute ticks instead — safe because this method is
    // idempotent and re-derives what's still missing every time, so nothing generated this tick is
    // lost, just deferred.
    private const int MaxOccurrencesGeneratedPerContractPerTick = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PreventiveMaintenanceSchedulerService> _logger;

    public PreventiveMaintenanceSchedulerService(IServiceScopeFactory scopeFactory, ILogger<PreventiveMaintenanceSchedulerService> logger)
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
                _logger.LogError(ex, "Preventive Maintenance scheduler tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrderService = scope.ServiceProvider.GetRequiredService<IWorkOrderService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var today = DateTime.UtcNow.Date;

        var contracts = await db.Contracts
            .Include(c => c.AssetLinks)
            .Where(c => c.ContractType == "Preventive Maintenance" && c.PmCadence != null && c.EndDate != null)
            .ToListAsync(ct);

        if (contracts.Count == 0) return;

        string? systemUserId = (await userManager.GetUsersInRoleAsync("Admin")).FirstOrDefault()?.Id;
        if (systemUserId is null)
        {
            _logger.LogWarning("Preventive Maintenance scheduler: no Admin-role user found, skipping this tick.");
            return;
        }

        foreach (var contract in contracts)
        {
            var dueSoFar = Contract.ComputeOccurrenceDueDates(contract.StartDate, contract.EndDate!.Value, contract.PmCadence!)
                .Where(d => d <= today).ToList();
            if (dueSoFar.Count == 0 || contract.AssetLinks.Count == 0) continue;

            var alreadyGenerated = await db.WorkOrders
                .Where(w => w.SourceContractId == contract.Id)
                .Select(w => new { w.AssetId, w.ScheduledDate })
                .ToListAsync(ct);
            var generatedSet = alreadyGenerated.Select(g => (g.AssetId, g.ScheduledDate!.Value.Date)).ToHashSet();

            var generatedThisTickForContract = 0;
            foreach (var link in contract.AssetLinks)
            {
                if (generatedThisTickForContract >= MaxOccurrencesGeneratedPerContractPerTick) break;
                foreach (var dueDate in dueSoFar)
                {
                    if (generatedThisTickForContract >= MaxOccurrencesGeneratedPerContractPerTick)
                    {
                        _logger.LogWarning("Preventive Maintenance: contract {ContractId} has more than {Max} occurrences still to generate — deferring the rest to the next tick.",
                            contract.Id, MaxOccurrencesGeneratedPerContractPerTick);
                        break;
                    }
                    if (generatedSet.Contains((link.AssetId, dueDate))) continue;
                    try
                    {
                        await workOrderService.CreatePreventiveMaintenanceOccurrenceAsync(
                            link.AssetId, contract.VendorId, contract.Id, dueDate, contract.ContractNumber, systemUserId);
                        generatedThisTickForContract++;
                    }
                    catch (DbUpdateException ex)
                    {
                        // Filtered unique index on (SourceContractId, AssetId, ScheduledDate) rejects a
                        // duplicate — the safety net for a multi-instance deployment racing on the same tick.
                        _logger.LogWarning(ex, "Preventive Maintenance: occurrence for contract {ContractId}, asset {AssetId}, due {DueDate} was not created (likely already generated).",
                            contract.Id, link.AssetId, dueDate);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // e.g. the asset was retired after being linked to this contract — log and
                        // move on rather than letting one bad asset link block every other link's
                        // occurrences this tick (and every tick thereafter).
                        _logger.LogWarning(ex, "Preventive Maintenance: occurrence for contract {ContractId}, asset {AssetId}, due {DueDate} failed: {Message}",
                            contract.Id, link.AssetId, dueDate, ex.Message);
                    }
                }
            }
        }
    }
}
