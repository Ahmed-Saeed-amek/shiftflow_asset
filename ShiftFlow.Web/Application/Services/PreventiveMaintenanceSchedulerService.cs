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

            foreach (var link in contract.AssetLinks)
            {
                foreach (var dueDate in dueSoFar)
                {
                    if (generatedSet.Contains((link.AssetId, dueDate))) continue;
                    try
                    {
                        await workOrderService.CreatePreventiveMaintenanceOccurrenceAsync(
                            link.AssetId, contract.VendorId, contract.Id, dueDate, contract.ContractNumber, systemUserId);
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
