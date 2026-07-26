using ShiftFlow.Domain.Entities;using ShiftFlow.Infrastructure.Data;
namespace ShiftFlow.Application.Services;
public class AuditService:IAuditService{
    private readonly ApplicationDbContext _db;
    public AuditService(ApplicationDbContext db){_db=db;}
    public async Task LogAsync(string action,string entityType,string? entityId,string userId,string? oldValue=null,string? newValue=null,string? details=null){
        _db.AuditLogs.Add(new AuditLog{Action=action,EntityType=entityType,EntityId=entityId,UserId=userId,OldValue=oldValue,NewValue=newValue,Details=details,CreatedDate=DateTime.UtcNow});
        await _db.SaveChangesAsync();}}