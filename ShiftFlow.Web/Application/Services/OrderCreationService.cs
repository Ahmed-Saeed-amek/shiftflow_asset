using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>What the unified Orders/Create form asked for, already bound. Which of the three order
/// kinds it becomes is decided here from the OrderType, never from the client.</summary>
public sealed class OrderCreationRequest
{
    public int OrderTypeId { get; init; }
    public DateTime? DueDate { get; init; }
    /// <summary>"User" or "Group" — only consulted when the type's AssignmentMode is "Either".</summary>
    public string? AssigneeType { get; init; }
    public string? AssignedToUserId { get; init; }
    public int? AssignedToGroupId { get; init; }
    /// <summary>Used when the type's AllowsMultipleAssets is false.</summary>
    public int AssetId { get; init; }
    /// <summary>Used when the type's AllowsMultipleAssets is true.</summary>
    public List<int>? AssetIds { get; init; }
}

public enum OrderCreationKind { Inspection, Maintenance, WorkOrder }

/// <summary>Where the caller should land, plus what to tell the user.</summary>
public sealed record OrderCreationResult(OrderCreationKind Kind, int FirstOrderId, string FirstOrderNumber, int Count);

public interface IOrderCreationService
{
    /// <summary>Resolves the order type, assignee and assets, then creates the order(s) — one
    /// transaction for the whole batch, so an invalid asset part-way through a multi-asset request
    /// fails all-or-nothing. Throws InvalidOperationException with a user-facing message.</summary>
    Task<OrderCreationResult> CreateAsync(OrderCreationRequest request, string userId);
    /// <summary>The active order type, or null — lets a caller decide permissions before creating.</summary>
    Task<OrderType?> GetActiveOrderTypeAsync(int orderTypeId);
}

public class OrderCreationService : IOrderCreationService
{
    private readonly ApplicationDbContext _db;
    private readonly IInspectionOrderService _inspectionOrders;
    private readonly IMaintenanceOrderService _maintenanceOrders;
    private readonly IWorkOrderService _workOrders;

    public OrderCreationService(ApplicationDbContext db, IInspectionOrderService inspectionOrders,
        IMaintenanceOrderService maintenanceOrders, IWorkOrderService workOrders)
    {
        _db = db; _inspectionOrders = inspectionOrders; _maintenanceOrders = maintenanceOrders; _workOrders = workOrders;
    }

    public Task<OrderType?> GetActiveOrderTypeAsync(int orderTypeId) =>
        _db.OrderTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == orderTypeId && t.IsActive);

    public async Task<OrderCreationResult> CreateAsync(OrderCreationRequest request, string userId)
    {
        var orderType = await GetActiveOrderTypeAsync(request.OrderTypeId)
            ?? throw new InvalidOperationException("Invalid order type.");

        // Nothing rejected a backdated Due Date on a brand-new order — a manager fighting the native
        // date input's segment-jumping (day/month/year focus landing on the wrong segment) could
        // create an order that shows as "Overdue" the instant it's saved, with no warning at all
        // (found in a cold UX review, confirmed: no such check exists anywhere in the create path).
        if (request.DueDate.HasValue && request.DueDate.Value.Date < DateTime.UtcNow.Date)
            throw new InvalidOperationException("Due Date can't be in the past.");

        // Asset cardinality and assignment mode come from the OrderType, not from whichever inputs
        // the client happened to enable.
        var assetIds = orderType.AllowsMultipleAssets
            ? (request.AssetIds ?? []).Distinct().ToList()
            : (request.AssetId > 0 ? [request.AssetId] : new List<int>());

        var (assignedToUserId, assignedToGroupId) = ResolveAssignee(orderType, request);

        if (!orderType.IsDirectFix)
        {
            var order = await _inspectionOrders.CreateAsync(orderType.Id, null,
                assignedToUserId, assignedToGroupId, assetIds, request.DueDate, userId);
            return new OrderCreationResult(OrderCreationKind.Inspection, order.Id, order.OrderNumber, 1);
        }

        if (assetIds.Count == 0) throw new InvalidOperationException("Select at least one asset.");
        await EnsureNoRetiredAssetsAsync(assetIds);

        return orderType.RequiresVendor
            ? await CreateWorkOrdersAsync(orderType, assetIds, assignedToUserId, userId)
            : await CreateMaintenanceOrdersAsync(orderType, assetIds, assignedToUserId, assignedToGroupId, request.DueDate, userId);
    }

    private static (string? UserId, int? GroupId) ResolveAssignee(OrderType orderType, OrderCreationRequest request)
    {
        var userId = orderType.AssignmentMode == AssignmentModes.GroupOnly ? null : request.AssignedToUserId;
        var groupId = orderType.AssignmentMode == AssignmentModes.EmployeeOnly ? null : request.AssignedToGroupId;
        if (orderType.AssignmentMode == AssignmentModes.Either)
        {
            if (request.AssigneeType == "User") groupId = null; else userId = null;
        }
        return (userId, groupId);
    }

    /// <summary>Names the offending assets — with a multi-asset picker showing only labels, "one or
    /// more" leaves no way to tell which selection to drop.</summary>
    private async Task EnsureNoRetiredAssetsAsync(List<int> assetIds)
    {
        var retiredTags = await _db.Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id) && a.Status == AssetStatuses.Retired)
            .Select(a => a.AssetTag).ToListAsync();
        if (retiredTags.Count > 0)
            throw new InvalidOperationException("These assets are retired and can't have new orders opened against them: " + string.Join(", ", retiredTags));
    }

    private async Task<OrderCreationResult> CreateWorkOrdersAsync(OrderType orderType, List<int> assetIds, string? assignedToUserId, string userId)
    {
        // WorkOrder has no group-assignment concept — a GroupOnly/Either type resolving to a group
        // would otherwise silently create an unassigned work order with the picked group discarded.
        if (string.IsNullOrWhiteSpace(assignedToUserId))
            throw new InvalidOperationException("This order type requires a vendor, which needs an individual employee assignee — group assignment isn't supported for vendor-routed work orders yet.");

        // One transaction across the whole batch: an invalid asset later in the list must not leave
        // the orders already created for earlier assets committed.
        WorkOrder? first = null;
        await using var tx = await _db.Database.BeginTransactionAsync();
        foreach (var assetId in assetIds)
        {
            var wo = await _workOrders.CreateAsync(new WorkOrder
            {
                AssetId = assetId,
                AssignedToUserId = assignedToUserId,
                OrderTypeId = orderType.Id,
                Description = null,
                RequiresVendorResponse = true,
            }, userId);
            first ??= wo;
        }
        await tx.CommitAsync();
        return new OrderCreationResult(OrderCreationKind.WorkOrder, first!.Id, first.WorkOrderNumber, assetIds.Count);
    }

    private async Task<OrderCreationResult> CreateMaintenanceOrdersAsync(OrderType orderType, List<int> assetIds,
        string? assignedToUserId, int? assignedToGroupId, DateTime? dueDate, string userId)
    {
        MaintenanceOrder? first = null;
        await using var tx = await _db.Database.BeginTransactionAsync();
        foreach (var assetId in assetIds)
        {
            var order = await _maintenanceOrders.CreateAsync(assetId, assignedToUserId, assignedToGroupId,
                null, dueDate, userId, orderType.Id);
            first ??= order;
        }
        await tx.CommitAsync();
        return new OrderCreationResult(OrderCreationKind.Maintenance, first!.Id, first.OrderNumber, assetIds.Count);
    }
}

/// <summary>DI registration for the services added alongside the order-creation extraction.
/// Program.cs needs one line: <c>builder.Services.AddOrderServices();</c> — see NOTES-orders.md.</summary>
public static class OrderServiceRegistration
{
    public static IServiceCollection AddOrderServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderCreationService, OrderCreationService>();
        return services;
    }
}
