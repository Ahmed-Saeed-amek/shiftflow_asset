using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    public AuditService(ApplicationDbContext db) { _db = db; }

    public async Task LogAsync(string action, string entityType, string? entityId, string userId,
        string? oldValue = null, string? newValue = null, string? details = null)
    {
        AuditBatch.Add(_db, action, entityType, entityId, userId, oldValue, newValue, details);
        await _db.SaveChangesAsync();
    }
}

/// <summary>Queues audit rows on the caller's own DbContext without saving, so a service writing
/// several rows at once (group membership diffs, contract asset diffs) commits them in one
/// SaveChangesAsync instead of one round trip per row.</summary>
public static class AuditBatch
{
    public static void Add(ApplicationDbContext db, string action, string entityType, string? entityId, string userId,
        string? oldValue = null, string? newValue = null, string? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Action = action, EntityType = entityType, EntityId = entityId, UserId = userId,
            OldValue = oldValue, NewValue = newValue, Details = details, CreatedDate = DateTime.UtcNow,
        });
}
