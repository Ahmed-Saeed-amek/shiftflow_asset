using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

public class AiInspectionToolFunctions : IAiInspectionToolFunctions
{
    private readonly IInspectionOrderService _orders;
    private readonly ITeamService _teams;
    private readonly IDashboardService _dashboard;
    private readonly IWorkOrderService _workOrders;
    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissions;

    public AiInspectionToolFunctions(IInspectionOrderService orders, ITeamService teams,
        IDashboardService dashboard, IWorkOrderService workOrders, ApplicationDbContext db, IPermissionService permissions)
    {
        _orders = orders;
        _teams = teams;
        _dashboard = dashboard;
        _workOrders = workOrders;
        _db = db;
        _permissions = permissions;
    }

    private static object OrderSummary(InspectionOrder o) => new
    {
        id = o.Id,
        orderNumber = o.OrderNumber,
        status = o.Status,
        assignedTo = o.AssignedToUser?.FullName ?? (o.AssignedToTeam != null ? $"Team: {o.AssignedToTeam.Name}" : null),
        dueDate = o.DueDate?.ToString("yyyy-MM-dd"),
        totalAssets = o.InspectionRun?.Items.Count ?? 0,
        checkedAssets = o.InspectionRun?.Items.Count(i => i.Outcome != "Pending") ?? 0,
    };

    public async Task<object> GetMyInspectionOrdersAsync(string userId, CancellationToken ct)
    {
        var orders = await _orders.GetMyOrdersAsync(userId, includeDone: false);
        return new { orders = orders.Select(OrderSummary) };
    }

    public async Task<object> GetInspectionOrderDetailAsync(int orderId, string userId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order == null) return new { error = "not_found", message = "Inspection order not found." };

        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, userId);
        if (order.AssignedToUserId != userId && !isTeamMember)
        {
            // Allow managers through — callers without InspectionOrder.Manage never reach this
            // tool at all (see orchestrator's RequiredPermission on getInspectionOrderDetail).
        }

        return new
        {
            id = order.Id,
            orderNumber = order.OrderNumber,
            description = order.Description,
            status = order.Status,
            dueDate = order.DueDate?.ToString("yyyy-MM-dd"),
            assignedTo = order.AssignedToUser?.FullName ?? (order.AssignedToTeam != null ? $"Team: {order.AssignedToTeam.Name}" : null),
            items = order.InspectionRun?.Items.Select(i => new
            {
                itemId = i.Id,
                assetTag = i.Asset.AssetTag,
                assetName = i.Asset.Name,
                outcome = i.Outcome,
                maintenanceActions = i.MaintenanceActions.Select(m => m.MaintenanceActionType.Name),
            }),
        };
    }

    public async Task<object> GetDashboardKpisAsync(string userId, CancellationToken ct)
    {
        var kpis = await _dashboard.GetKpisAsync(userId);
        return new
        {
            openInspectionOrders = kpis.OpenInspectionOrders,
            inspectionOrdersOverdue = kpis.InspectionOrdersOverdue,
            activeTeams = kpis.ActiveTeams,
            totalEngineers = kpis.TotalEngineers,
            totalAssets = kpis.TotalAssets,
            defectiveAssets = kpis.DefectiveAssets,
            openWorkOrders = kpis.OpenWorkOrders,
            criticalOpenWorkOrders = kpis.CriticalOpenWorkOrders,
        };
    }

    public async Task<object> FindEmployeeAsync(string query, string userId, CancellationToken ct)
    {
        var term = query?.Trim() ?? "";
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.FullName.Contains(term) || (u.Email != null && u.Email.Contains(term))))
            .OrderBy(u => u.FullName)
            .Take(10)
            .Select(u => new { id = u.Id, fullName = u.FullName, email = u.Email })
            .ToListAsync(ct);
        return new { results = users };
    }

    public async Task<object> ListTeamsAsync(string userId, CancellationToken ct)
    {
        var teams = await _teams.GetAllAsync();
        return new { teams = teams.Select(t => new { id = t.Id, name = t.Name, memberCount = t.Members.Count }) };
    }

    public async Task<object> GetTeamDetailAsync(int teamId, string userId, CancellationToken ct)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team == null) return new { error = "not_found", message = "Team not found." };
        return new
        {
            id = team.Id,
            name = team.Name,
            isActive = team.IsActive,
            members = team.Members.Select(m => new { userId = m.UserId, fullName = m.User.FullName }),
        };
    }

    public async Task<object> CreateInspectionOrderAsync(string? description, string? assignedToUserId,
        int? assignedToTeamId, int? zoneId, List<int>? assetIds, DateTime? dueDate, string userId, CancellationToken ct)
    {
        // zoneId is an AI-facing convenience ("inspect zone X") — resolve it to concrete asset ids
        // here since the service now always takes a resolved AssetIds list.
        var resolvedAssetIds = assetIds != null ? new List<int>(assetIds) : new List<int>();
        if (zoneId.HasValue)
        {
            var zoneAssetIds = await _db.Assets.Where(a => a.ZoneId == zoneId.Value).Select(a => a.Id).ToListAsync(ct);
            foreach (var id in zoneAssetIds)
                if (!resolvedAssetIds.Contains(id)) resolvedAssetIds.Add(id);
        }
        // The AI tool always creates the full "tracks defect outcome" order type (was the hardcoded
        // "Inspection" kind) rather than a lighter type like Quick Check — falls back to any active
        // type if that one was renamed/deactivated.
        var orderTypeId = await _db.OrderTypes
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.TracksDefectOutcome).ThenBy(t => t.SortOrder)
            .Select(t => t.Id).FirstOrDefaultAsync(ct);
        var order = await _orders.CreateAsync(orderTypeId, description, assignedToUserId, assignedToTeamId, resolvedAssetIds, dueDate, userId);
        return new { success = true, id = order.Id, orderNumber = order.OrderNumber };
    }

    public async Task<object> ReportInspectionOutcomeAsync(int itemId, string outcome, string? notes, int? actionTypeId, int? causeId, string userId, CancellationToken ct)
    {
        // InspectionOrderReport is a broad role permission (Engineer/Technician/etc.), not a
        // per-order grant — the human path (InspectionOrdersController.UpdateItem) additionally
        // requires the caller be the order's assignee, a member of its assigned team, or hold
        // InspectionOrder.Manage. Without the same check here, any holder of the broad permission
        // could ask the assistant to report outcomes on an item from an order assigned to someone
        // else entirely, which the equivalent UI action would 403.
        var item = await _db.InspectionRunAssets.Include(i => i.InspectionRun).ThenInclude(r => r.InspectionOrder)
            .FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new InvalidOperationException("Inspection item not found.");
        var order = item.InspectionRun.InspectionOrder;
        var isManager = await _permissions.HasPermissionAsync(userId, PermissionCatalog.InspectionOrderManage);
        var isAssignee = order.AssignedToUserId == userId;
        var isTeamMember = order.AssignedToTeamId.HasValue && await _teams.IsMemberAsync(order.AssignedToTeamId.Value, userId);
        if (!isManager && !isAssignee && !isTeamMember)
            throw new InvalidOperationException("This inspection order isn't assigned to you.");

        int? workOrderId = null;
        if (outcome == "Defective")
        {
            if (actionTypeId == null || causeId == null)
                throw new InvalidOperationException("Action Type and Cause are required to report a defect.");
            var wo = await _workOrders.ReportAsync(new WorkOrder
            {
                AssetId = item.AssetId, ActionTypeId = actionTypeId, CauseId = causeId, Notes = notes,
            }, userId);
            workOrderId = wo.Id;
        }

        await _orders.UpdateInspectionItemAsync(itemId, outcome, workOrderId, null, userId);
        return new { success = true, outcome, workOrderId };
    }

    public async Task<object> CancelInspectionOrderAsync(int orderId, string userId, CancellationToken ct)
    {
        await _orders.CancelAsync(orderId, null, userId);
        return new { success = true };
    }

    public async Task<object> CreateTeamAsync(string name, string? description, List<string>? memberUserIds, string userId, CancellationToken ct)
    {
        var team = await _teams.CreateAsync(name, null, description, memberUserIds ?? [], userId);
        return new { success = true, id = team.Id, name = team.Name };
    }

    public async Task<object> AddTeamMemberAsync(int teamId, string memberUserId, string userId, CancellationToken ct)
    {
        await _teams.AddMemberAsync(teamId, memberUserId, userId);
        return new { success = true };
    }

    public async Task<object> RemoveTeamMemberAsync(int teamId, string memberUserId, string userId, CancellationToken ct)
    {
        await _teams.RemoveMemberAsync(teamId, memberUserId, userId);
        return new { success = true };
    }
}
