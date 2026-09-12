using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class GroupService : IGroupService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public GroupService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<Group>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.Groups.AsNoTracking().Include(t => t.Members).ThenInclude(m => m.User).AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<Group?> GetByIdAsync(int id) =>
        await _db.Groups.AsNoTracking().Include(t => t.Members).ThenInclude(m => m.User)
            .Include(t => t.CreatedByUser)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Group> CreateAsync(string name, string? nameAr, string? description, List<string> initialMemberUserIds, string userId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Group name is required.");
        // Group names must be unique — assigning work by group name is ambiguous otherwise.
        if (await _db.Groups.AnyAsync(t => t.Name == name))
            throw new InvalidOperationException($"A group named '{name}' already exists.");
        // A stale or tampered member id otherwise hits GroupMembers' FK constraint as an
        // unhandled DbUpdateException.
        var distinctMemberIds = initialMemberUserIds.Distinct().ToList();
        if (distinctMemberIds.Count > 0 && await _db.Users.CountAsync(u => distinctMemberIds.Contains(u.Id)) != distinctMemberIds.Count)
            throw new InvalidOperationException("One or more selected members were not found.");

        var group = new Group
        {
            Name = name,
            NameAr = nameAr,
            Description = description,
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Members = initialMemberUserIds.Distinct()
                .Select(uid => new GroupMember { UserId = uid, AddedAt = DateTime.UtcNow })
                .ToList(),
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Group", group.Id.ToString(), userId, newValue: group.Name);
        return group;
    }

    public async Task UpdateAsync(int groupId, string name, string? nameAr, string? description, string userId)
    {
        var group = await _db.Groups.FindAsync(groupId) ?? throw new InvalidOperationException("Group not found.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Group name is required.");
        if (await _db.Groups.AnyAsync(t => t.Id != groupId && t.Name == name))
            throw new InvalidOperationException($"A group named '{name}' already exists.");

        group.Name = name;
        group.NameAr = nameAr;
        group.Description = description;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Group", groupId.ToString(), userId, newValue: name);
    }

    public async Task SetActiveAsync(int groupId, bool isActive, string userId)
    {
        var group = await _db.Groups.FindAsync(groupId) ?? throw new InvalidOperationException("Group not found.");
        group.IsActive = isActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(isActive ? "Activate" : "Deactivate", "Group", groupId.ToString(), userId);
    }

    // Audit values are shown to admins as-is (e.g. on the per-user profile Audit Log tab) — log
    // the member's name, not the raw user-id GUID, so the entry is actually readable.
    private async Task<string?> NameOfAsync(string? uid) => uid == null ? null
        : await _db.Users.Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync();

    // Called by the AI assistant tools with ids resolved from model output, so both are checked
    // here: an unknown id would otherwise trip a GroupMembers FK constraint as an unhandled 500
    // rather than the graceful in-chat "not found" every other AI tool returns.
    public async Task AddMemberAsync(int groupId, string userId, string actingUserId)
    {
        if (!await _db.Groups.AnyAsync(t => t.Id == groupId))
            throw new InvalidOperationException("Group not found.");
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            throw new InvalidOperationException("Selected employee not found.");
        var exists = await _db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId);
        if (exists) return;
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId, AddedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AddMember", "Group", groupId.ToString(), actingUserId, newValue: await NameOfAsync(userId));
    }

    public async Task RemoveMemberAsync(int groupId, string userId, string actingUserId)
    {
        if (!await _db.Groups.AnyAsync(t => t.Id == groupId))
            throw new InvalidOperationException("Group not found.");
        var member = await _db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);
        if (member == null) return;
        _db.GroupMembers.Remove(member);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("RemoveMember", "Group", groupId.ToString(), actingUserId, oldValue: await NameOfAsync(userId));
    }

    public Task<bool> IsMemberAsync(int groupId, string userId) =>
        _db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId);

    /// <summary>Reconciles a group's membership to exactly the given user id list — diffs against
    /// the current members and adds/removes only what changed, so the audit trail reads the same
    /// as the old separate AddMember/RemoveMember actions this replaces on the Edit page.
    /// originalMemberUserIds is the snapshot the edit form was loaded with (carried as hidden
    /// fields) — if it no longer matches what's actually in the DB, someone else changed this
    /// group's membership in between, and blindly diffing against the live set would silently
    /// discard their change. Rejecting the stale submit forces a reload instead.</summary>
    public async Task SetMembersAsync(int groupId, List<string> memberUserIds, List<string> originalMemberUserIds, string actingUserId)
    {
        var current = await _db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync();
        var currentIds = current.Select(m => m.UserId).ToHashSet();
        if (!currentIds.SetEquals(originalMemberUserIds.Distinct()))
            throw new InvalidOperationException("This group's membership was changed by someone else since you opened this page. Reload and try again.");
        var wantedIds = memberUserIds.Distinct().ToHashSet();

        var toRemove = current.Where(m => !wantedIds.Contains(m.UserId)).ToList();
        var toAddIds = wantedIds.Where(id => !currentIds.Contains(id)).ToList();

        _db.GroupMembers.RemoveRange(toRemove);
        foreach (var id in toAddIds)
            _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = id, AddedAt = DateTime.UtcNow });

        // One name lookup for every affected member, and one SaveChanges for the membership change
        // plus all its audit rows — this used to be a query and a save per added/removed member.
        var affectedIds = toRemove.Select(m => m.UserId).Concat(toAddIds).Distinct().ToList();
        var names = await _db.Users.Where(u => affectedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);
        foreach (var m in toRemove)
            AuditBatch.Add(_db, "RemoveMember", "Group", groupId.ToString(), actingUserId, oldValue: names.GetValueOrDefault(m.UserId));
        foreach (var id in toAddIds)
            AuditBatch.Add(_db, "AddMember", "Group", groupId.ToString(), actingUserId, newValue: names.GetValueOrDefault(id));
        await _db.SaveChangesAsync();
    }
}
