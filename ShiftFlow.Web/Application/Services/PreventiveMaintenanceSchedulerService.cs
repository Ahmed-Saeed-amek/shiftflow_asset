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

    // Per-tick blast radius is occurrences x linked-assets, so a long-running contract with many
    // linked assets could insert hundreds of thousands of rows in one tick. Capping spreads the
    // backlog across ticks — safe because this method re-derives what's still missing every time.
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
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
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
            _logger.LogError("Preventive Maintenance scheduler: no Admin-role user found — no occurrences can be generated this tick.");
            return;
        }

        // Failures are surfaced on the Audit Logs screen (EntityType = Contract) as well as the log,
        // so a schedule that has quietly stopped producing work orders is discoverable in the app.
        async Task ReportFailureAsync(int contractId, string message)
        {
            _logger.LogError("Preventive Maintenance: contract {ContractId} failed to generate an occurrence: {Message}", contractId, message);
            try
            {
                await audit.LogAsync("GenerationFailed", "Contract", contractId.ToString(), systemUserId, details: message);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Preventive Maintenance: could not write the failure audit row for contract {ContractId}.", contractId);
            }
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
                        // e.g. the asset was retired after being linked to this contract — surfaced,
                        // then skipped, so one bad link can't block the rest of the contract forever.
                        await ReportFailureAsync(contract.Id,
                            $"Asset {link.AssetId}, due {dueDate:yyyy-MM-dd}: {ex.Message}");
                    }
                }
            }
        }
    }
}
