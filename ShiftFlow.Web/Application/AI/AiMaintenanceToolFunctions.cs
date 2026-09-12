using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

/// <summary>Maintenance-order tools (the in-house fix pipeline: Open → PendingApproval → Done, no
/// vendor). Reads are scoped to the caller's assets; writes go through IMaintenanceOrderService,
/// which owns the asset-status side effects, stock movements and audit rows.</summary>
public class AiMaintenanceToolFunctions : AiToolsBase
{
    private readonly IMaintenanceOrderService _orders;
    private readonly IPendingActionStore _pending;

    public AiMaintenanceToolFunctions(ApplicationDbContext db, IAssetScopeService scope,
        IMaintenanceOrderService orders, IPendingActionStore pending) : base(db, scope)
    {
        _orders = orders;
        _pending = pending;
    }

    private async Task<IQueryable<MaintenanceOrder>> ScopedOrdersAsync(string userId)
    {
        var q = Db.MaintenanceOrders.AsNoTracking().AsQueryable();
        var scopedIds = await ScopedAssetIdsAsync(userId);
        return scopedIds == null ? q : q.Where(m => scopedIds.Contains(m.AssetId));
    }

    public async Task<object> SearchMaintenanceOrdersAsync(string? status, bool? assignedToMe, string? query, int? limit,
        string userId, CancellationToken ct)
    {
        var take = Clamp(limit, 10, 50);
        var q = (await ScopedOrdersAsync(userId))
            .Include(m => m.Asset).Include(m => m.AssignedToUser).Include(m => m.AssignedToGroup).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(m => m.Status == status);
        if (assignedToMe == true)
        {
            var myGroupIds = await Db.GroupMembers.Where(g => g.UserId == userId).Select(g => g.GroupId).ToListAsync(ct);
            q = q.Where(m => m.AssignedToUserId == userId || (m.AssignedToGroupId != null && myGroupIds.Contains(m.AssignedToGroupId.Value)));
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(m => m.OrderNumber.Contains(term) || (m.Asset != null && m.Asset.AssetTag.Contains(term)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(m => m.CreatedDate).Take(take)
            .Select(m => new
            {
                id = m.Id,
                number = m.OrderNumber,
                asset = m.Asset != null ? m.Asset.AssetTag : null,
                status = m.Status,
                assignedTo = m.AssignedToUser != null ? m.AssignedToUser.FullName
                    : (m.AssignedToGroup != null ? "Group: " + m.AssignedToGroup.Name : null),
                dueDate = m.DueDate,
            })
            .ToListAsync(ct);

        var table = new AiTableAttachment
        {
            Title = "Maintenance orders",
            Columns =
            [
                new("number", "Number"), new("asset", "Asset"), new("assignedTo", "Assigned to"),
                new("dueDate", "Due"), new("status", "Status"),
            ],
            Rows = rows.Select(r => new Dictionary<string, object?>
            {
                ["number"] = r.number,
                ["asset"] = r.asset,
                ["assignedTo"] = r.assignedTo ?? "—",
                ["dueDate"] = D(r.dueDate) ?? "—",
                ["status"] = r.status,
                ["_url"] = AiLinks.MaintenanceOrder(r.id),
                ["_status"] = r.status,
            }).ToList(),
        };

        return new
        {
            totalMatches = total,
            returned = rows.Count,
            orders = rows.Select(r => new { r.id, r.number, r.asset, r.status, r.assignedTo, dueDate = D(r.dueDate) }),
            ui = rows.Count > 0 ? (object?)table : null,
        };
    }

    public async Task<object> GetMaintenanceOrderDetailAsync(int id, string userId, CancellationToken ct)
    {
        var order = await (await ScopedOrdersAsync(userId))
            .Include(m => m.Asset).Include(m => m.AssignedToUser).Include(m => m.AssignedToGroup)
            .Include(m => m.OrderType).Include(m => m.Parts)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (order == null) return NotFound("Maintenance order");

        var card = new AiCardsAttachment
        {
            Items =
            [
                new AiCard
                {
                    Title = order.OrderNumber,
                    Subtitle = order.Asset != null ? $"{order.Asset.AssetTag} — {order.Asset.Name}" : null,
                    Badge = new AiBadge(order.Status, order.Status),
                    Url = AiLinks.MaintenanceOrder(order.Id),
                    Fields =
                    [
                        new("Assigned to", order.AssignedToUser?.FullName ?? (order.AssignedToGroup != null ? $"Group: {order.AssignedToGroup.Name}" : "—")),
                        new("Type", order.OrderType?.Name ?? "—"),
                        new("Due", D(order.DueDate) ?? "—"),
                        new("Completed", D(order.CompletedDate) ?? "—"),
                        new("Cost", Money(order.Cost)),
                    ],
                },
            ],
        };

        return new
        {
            id = order.Id,
            number = order.OrderNumber,
            status = order.Status,
            assetId = order.AssetId,
            asset = order.Asset != null ? $"{order.Asset.AssetTag} — {order.Asset.Name}" : null,
            assignedToUserId = order.AssignedToUserId,
            assignedToGroupId = order.AssignedToGroupId,
            assignedTo = order.AssignedToUser?.FullName ?? order.AssignedToGroup?.Name,
            description = order.Description,
            orderType = order.OrderType?.Name,
            dueDate = D(order.DueDate),
            completedDate = D(order.CompletedDate),
            cost = order.Cost,
            fixDescription = order.FixDescription,
            parts = order.Parts.Select(p => new { p.Name, p.Quantity, unitCost = p.UnitCostAtUsage }),
            ui = card,
        };
    }

    public async Task<object> CreateMaintenanceOrderAsync(int assetId, string? assignedToUserId, int? assignedToGroupId,
        string? description, DateTime? dueDate, int? orderTypeId, string userId, CancellationToken ct)
    {
        if (await FindScopedAssetAsync(assetId, userId, ct) == null) return NotFound("Asset");

        var order = await _orders.CreateAsync(assetId, assignedToUserId, assignedToGroupId, description, dueDate, userId, orderTypeId);
        return new
        {
            success = true,
            id = order.Id,
            number = order.OrderNumber,
            status = order.Status,
            ui = new AiLinksAttachment { Items = [new AiLinkItem(order.OrderNumber, AiLinks.MaintenanceOrder(order.Id), "wrench")] },
        };
    }

    public async Task<object> CompleteMaintenanceOrderAsync(int id, DateTime? completedDate,
        List<(int SparePartId, int Quantity)>? parts, string userId, CancellationToken ct)
    {
        var order = await ResolveAsync(id, userId, ct);
        if (order == null) return NotFound("Maintenance order");

        var updated = await _orders.CompleteAsync(id, completedDate, parts ?? [], userId);
        return new
        {
            success = true,
            id = updated.Id,
            number = updated.OrderNumber,
            status = updated.Status,
            cost = updated.Cost,
            ui = new AiLinksAttachment { Items = [new AiLinkItem(updated.OrderNumber, AiLinks.MaintenanceOrder(updated.Id), "wrench")] },
        };
    }

    public async Task<object> ApproveMaintenanceOrderAsync(int id, string userId, CancellationToken ct)
    {
        var order = await ResolveAsync(id, userId, ct);
        if (order == null) return NotFound("Maintenance order");
        await _orders.ApproveAsync(id, userId);
        return Ok(order, "approved");
    }

    public async Task<object> ReturnMaintenanceOrderToOpenAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var order = await ResolveAsync(id, userId, ct);
        if (order == null) return NotFound("Maintenance order");

        var summary = $"Send {order.OrderNumber} back to Open — {reason}. This clears the reported cost and returns the used parts to stock.";
        return Pending(userId, summary, "Return maintenance order to Open", "Return to Open", PermissionCatalog.MaintenanceOrderManage, danger: true,
            async (sp, innerCt) =>
            {
                var tools = sp.GetRequiredService<AiMaintenanceToolFunctions>();
                return await tools.ReturnMaintenanceOrderToOpenConfirmedAsync(id, reason, userId, innerCt);
            });
    }

    public async Task<object> ReturnMaintenanceOrderToOpenConfirmedAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var order = await ResolveAsync(id, userId, ct);
        if (order == null) return NotFound("Maintenance order");
        await _orders.RejectApprovalAsync(id, reason, userId);
        return Ok(order, "returned to Open");
    }

    public async Task<object> CancelMaintenanceOrderAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var order = await ResolveAsync(id, userId, ct);
        if (order == null) return NotFound("Maintenance order");

        var summary = $"Cancel {order.OrderNumber} ({order.Asset?.AssetTag}) — {reason}. Cancellation is terminal.";
        return Pending(userId, summary, "Cancel maintenance order", "Cancel order", PermissionCatalog.MaintenanceOrderManage, danger: true,
            async (sp, innerCt) =>
            {
                var tools = sp.GetRequiredService<AiMaintenanceToolFunctions>();
                return await tools.CancelMaintenanceOrderConfirmedAsync(id, reason, userId, innerCt);
            });
    }

    public async Task<object> CancelMaintenanceOrderConfirmedAsync(int id, string reason, string userId, CancellationToken ct)
    {
        var order = await ResolveAsync(id, userId, ct);
        if (order == null) return NotFound("Maintenance order");
        await _orders.CancelAsync(id, reason, userId);
        return Ok(order, "cancelled");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<MaintenanceOrder?> ResolveAsync(int id, string userId, CancellationToken ct) =>
        await (await ScopedOrdersAsync(userId)).Include(m => m.Asset).FirstOrDefaultAsync(m => m.Id == id, ct);

    private static object Ok(MaintenanceOrder order, string what) => new
    {
        success = true,
        id = order.Id,
        number = order.OrderNumber,
        result = what,
        ui = new AiLinksAttachment { Items = [new AiLinkItem(order.OrderNumber, AiLinks.MaintenanceOrder(order.Id), "wrench")] },
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
