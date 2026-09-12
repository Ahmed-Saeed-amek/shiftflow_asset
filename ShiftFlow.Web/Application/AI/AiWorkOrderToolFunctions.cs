using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

/// <summary>Work-order tools. Reads are filtered to the caller's asset scope exactly like
/// WorkOrdersController.Index; every write delegates to IWorkOrderService, which re-checks scope,
/// stage legality and writes the audit row itself.</summary>
public class AiWorkOrderToolFunctions : AiToolsBase
{
    private readonly IWorkOrderService _workOrders;
    private readonly IContractService _contracts;
    private readonly IPendingActionStore _pending;

    public AiWorkOrderToolFunctions(ApplicationDbContext db, IAssetScopeService scope,
        IWorkOrderService workOrders, IContractService contracts, IPendingActionStore pending)
        : base(db, scope)
    {
        _workOrders = workOrders;
        _contracts = contracts;
        _pending = pending;
    }

    private async Task<IQueryable<WorkOrder>> ScopedWorkOrdersAsync(string userId)
    {
        var q = Db.WorkOrders.AsNoTracking().AsQueryable();
        var scopedIds = await ScopedAssetIdsAsync(userId);
        return scopedIds == null ? q : q.Where(w => scopedIds.Contains(w.AssetId));
    }

    public async Task<object> SearchWorkOrdersAsync(string? stage, string? priority, int? vendorId, int? assetId,
        bool? assignedToMe, string? query, int? limit, string userId, CancellationToken ct)
    {
        var take = Clamp(limit, 10, 50);
        var q = (await ScopedWorkOrdersAsync(userId))
            .Include(w => w.Asset).Include(w => w.Vendor).Include(w => w.AssignedToUser).AsQueryable();

        if (!string.IsNullOrWhiteSpace(stage)) q = q.Where(w => w.Stage == stage);
        if (!string.IsNullOrWhiteSpace(priority)) q = q.Where(w => w.Priority == priority);
        if (vendorId.HasValue) q = q.Where(w => w.VendorId == vendorId);
        if (assetId.HasValue) q = q.Where(w => w.AssetId == assetId);
        if (assignedToMe == true) q = q.Where(w => w.AssignedToUserId == userId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(w => w.WorkOrderNumber.Contains(term)
                || (w.Asset != null && (w.Asset.AssetTag.Contains(term) || w.Asset.Name.Contains(term))));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(w => w.CreatedDate).Take(take)
            .Select(w => new
            {
                id = w.Id,
                number = w.WorkOrderNumber,
                asset = w.Asset != null ? w.Asset.AssetTag : null,
                stage = w.Stage,
                priority = w.Priority,
                vendor = w.Vendor != null ? w.Vendor.Name : null,
                assignedTo = w.AssignedToUser != null ? w.AssignedToUser.FullName : null,
                created = w.CreatedDate,
            })
            .ToListAsync(ct);

        var table = new AiTableAttachment
        {
            Title = "Work orders",
            Columns =
            [
                new("number", "Number"), new("asset", "Asset"), new("priority", "Priority"),
                new("vendor", "Vendor"), new("stage", "Stage"),
            ],
            Rows = rows.Select(r => new Dictionary<string, object?>
            {
                ["number"] = r.number,
                ["asset"] = r.asset,
                ["priority"] = r.priority,
                ["vendor"] = r.vendor ?? "—",
                ["stage"] = r.stage,
                ["_url"] = AiLinks.WorkOrder(r.id),
                ["_status"] = r.stage,
            }).ToList(),
        };

        return new
        {
            totalMatches = total,
            returned = rows.Count,
            workOrders = rows.Select(r => new { r.id, r.number, r.asset, r.stage, r.priority, r.vendor, r.assignedTo, created = D(r.created) }),
            ui = rows.Count > 0 ? (object?)table : null,
        };
    }

    public async Task<object> GetWorkOrderDetailAsync(string idOrNumber, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(idOrNumber, userId, ct);
        if (wo == null) return NotFound("Work order");

        var parts = await Db.WorkOrderParts.AsNoTracking().Where(p => p.WorkOrderId == wo.Id)
            .Select(p => new { p.Name, p.Quantity, p.UnitCostAtUsage }).ToListAsync(ct);

        var card = new AiCardsAttachment
        {
            Items =
            [
                new AiCard
                {
                    Title = wo.WorkOrderNumber,
                    Subtitle = wo.Asset != null ? $"{wo.Asset.AssetTag} — {wo.Asset.Name}" : null,
                    Badge = new AiBadge(wo.Stage, wo.Stage),
                    Url = AiLinks.WorkOrder(wo.Id),
                    Fields =
                    [
                        new("Priority", wo.Priority),
                        new("Vendor", wo.Vendor?.Name ?? "—"),
                        new("Assigned employee", wo.AssignedToUser?.FullName ?? "—"),
                        new("Reported action", wo.ActionType?.Name ?? "—"),
                        new("Cause", wo.Cause?.Name ?? "—"),
                        new("Created", D(wo.CreatedDate) ?? "—"),
                        new("Fix cost", Money(wo.FixCost)),
                    ],
                },
            ],
        };

        return new
        {
            id = wo.Id,
            number = wo.WorkOrderNumber,
            stage = wo.Stage,
            priority = wo.Priority,
            assetId = wo.AssetId,
            asset = wo.Asset != null ? $"{wo.Asset.AssetTag} — {wo.Asset.Name}" : null,
            vendorId = wo.VendorId,
            vendor = wo.Vendor?.Name,
            assignedToUserId = wo.AssignedToUserId,
            assignedTo = wo.AssignedToUser?.FullName,
            actionType = wo.ActionType?.Name,
            cause = wo.Cause?.Name,
            blockReason = wo.BlockReason?.Name,
            blockDetail = wo.BlockDetail,
            description = wo.Description,
            notes = wo.Notes,
            requiresVendorResponse = wo.RequiresVendorResponse,
            createdDate = D(wo.CreatedDate),
            closedDate = D(wo.ClosedDate),
            fixDescription = wo.FixDescription,
            fixCost = wo.FixCost,
            partsUsed = parts,
            ui = card,
        };
    }

    public async Task<object> AcceptWorkOrderAsync(int id, int? vendorId, string? priority, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");

        await _workOrders.AcceptAsync(id, vendorId, priority ?? wo.Priority, userId);
        return Ok(wo, "accepted");
    }

    public async Task<object> RejectWorkOrderAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");

        var summary = $"Reject {wo.WorkOrderNumber} ({wo.Asset?.AssetTag}) — {reason}. Rejected reports are terminal and stay out of the pipeline.";
        return Pending(userId, summary, "Reject work order", "Reject", PermissionCatalog.WorkOrderManage, danger: true,
            async (sp, innerCt) =>
            {
                var tools = sp.GetRequiredService<AiWorkOrderToolFunctions>();
                return await tools.RejectWorkOrderConfirmedAsync(id, reason, userId, innerCt);
            });
    }

    public async Task<object> RejectWorkOrderConfirmedAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");
        await _workOrders.RejectAsync(id, reason, userId);
        return Ok(wo, "rejected");
    }

    public async Task<object> SendWorkOrderToVendorAsync(int id, int vendorId, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");
        await _workOrders.SendToVendorAsync(id, vendorId, userId);
        return Ok(wo, "sent to vendor");
    }

    public async Task<object> AssignWorkOrderEmployeeAsync(int id, string? employeeUserId, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");
        await _workOrders.AssignEmployeeAsync(id, string.IsNullOrWhiteSpace(employeeUserId) ? null : employeeUserId, userId);
        return Ok(wo, employeeUserId == null ? "employee cleared" : "employee assigned");
    }

    public async Task<object> SetWorkOrderPriorityAsync(int id, string priority, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");
        await _workOrders.UpdatePriorityAsync(id, priority, userId);
        return Ok(wo, $"priority set to {priority}");
    }

    public async Task<object> ConfirmWorkOrderFixAsync(int id, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");
        await _workOrders.ConfirmFixAsync(id, userId);
        return Ok(wo, "closed");
    }

    public async Task<object> ForceCloseWorkOrderAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");

        var summary = $"Force-close {wo.WorkOrderNumber} ({wo.Asset?.AssetTag}) from stage \"{wo.Stage}\" without waiting for the vendor or employee — {reason}.";
        return Pending(userId, summary, "Force-close work order", "Force close", PermissionCatalog.WorkOrderManage, danger: true,
            async (sp, innerCt) =>
            {
                var tools = sp.GetRequiredService<AiWorkOrderToolFunctions>();
                return await tools.ForceCloseWorkOrderConfirmedAsync(id, reason, userId, innerCt);
            });
    }

    public async Task<object> ForceCloseWorkOrderConfirmedAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var wo = await ResolveAsync(id.ToString(), userId, ct);
        if (wo == null) return NotFound("Work order");
        await _workOrders.ForceCloseAsync(id, reason, userId);
        return Ok(wo, "force-closed");
    }

    public async Task<object> ListVendorsForAssetAsync(int assetId, string userId, CancellationToken ct)
    {
        if (await FindScopedAssetAsync(assetId, userId, ct) == null) return NotFound("Asset");

        var candidates = await _contracts.GetActiveServiceVendorsAsync(assetId);
        return new
        {
            assetId,
            vendors = candidates.Select(c => new { vendorId = c.VendorId, name = c.VendorName, contractId = c.ContractId, contractNumber = c.ContractNumber }),
            note = candidates.Count == 0 ? "No active Service contract covers this asset, so it has no eligible vendor." : null,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Resolves a numeric id or a work order number ("WO-2026-0004"), always through the
    /// caller's asset scope — a model-supplied id never reaches DbSet.Find unfiltered.</summary>
    private async Task<WorkOrder?> ResolveAsync(string idOrNumber, string userId, CancellationToken ct)
    {
        var q = (await ScopedWorkOrdersAsync(userId))
            .Include(w => w.Asset).Include(w => w.Vendor).Include(w => w.AssignedToUser)
            .Include(w => w.ActionType).Include(w => w.Cause).Include(w => w.BlockReason);

        var term = (idOrNumber ?? "").Trim();
        if (int.TryParse(term, out var id)) return await q.FirstOrDefaultAsync(w => w.Id == id, ct);
        return await q.FirstOrDefaultAsync(w => w.WorkOrderNumber == term, ct);
    }

    private static object Ok(WorkOrder wo, string what) => new
    {
        success = true,
        workOrderId = wo.Id,
        workOrderNumber = wo.WorkOrderNumber,
        result = what,
        ui = new AiLinksAttachment { Items = [new AiLinkItem(wo.WorkOrderNumber, AiLinks.WorkOrder(wo.Id), "clipboard")] },
    };

    private object Pending(string userId, string summary, string title, string actionLabel, string permission, bool danger,
        Func<IServiceProvider, CancellationToken, Task<object>> execute)
    {
        var token = _pending.Create(userId, summary, permission, execute);
        return new
        {
            pendingConfirmation = true,
            token,
            summary,
            ui = new AiConfirmAttachment { Token = token, Title = title, Summary = summary, ActionLabel = actionLabel, Danger = danger },
        };
    }
}
