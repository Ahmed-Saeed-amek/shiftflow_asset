namespace ShiftFlow.Application.AI;

public interface IAiInspectionToolFunctions
{
    // ── Reads ─────────────────────────────────────────────────────────────────
    Task<object> GetMyInspectionOrdersAsync(string userId, CancellationToken ct);
    Task<object> GetInspectionOrderDetailAsync(int orderId, string userId, CancellationToken ct);
    Task<object> GetDashboardKpisAsync(string userId, CancellationToken ct);
    Task<object> FindEmployeeAsync(string query, string userId, CancellationToken ct);
    Task<object> ListTeamsAsync(string userId, CancellationToken ct);
    Task<object> GetTeamDetailAsync(int teamId, string userId, CancellationToken ct);

    // ── Writes ────────────────────────────────────────────────────────────────
    Task<object> CreateInspectionOrderAsync(string? description, string? assignedToUserId,
        int? assignedToTeamId, int? zoneId, List<int>? assetIds, DateTime? dueDate, string userId, CancellationToken ct);
    Task<object> ReportInspectionOutcomeAsync(int itemId, string outcome, string? notes, int? actionTypeId, int? causeId, string userId, CancellationToken ct);
    Task<object> CancelInspectionOrderAsync(int orderId, string userId, CancellationToken ct);
    Task<object> CreateTeamAsync(string name, string? description, List<string>? memberUserIds, string userId, CancellationToken ct);
    Task<object> AddTeamMemberAsync(int teamId, string memberUserId, string userId, CancellationToken ct);
    Task<object> RemoveTeamMemberAsync(int teamId, string memberUserId, string userId, CancellationToken ct);
}
