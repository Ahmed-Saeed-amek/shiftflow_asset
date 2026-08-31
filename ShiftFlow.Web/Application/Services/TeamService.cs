using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class TeamService : ITeamService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public TeamService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<Team>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.Teams.Include(t => t.Members).ThenInclude(m => m.User).AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<Team?> GetByIdAsync(int id) =>
        await _db.Teams.Include(t => t.Members).ThenInclude(m => m.User)
            .Include(t => t.CreatedByUser)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Team> CreateAsync(string name, string? nameAr, string? description, List<string> initialMemberUserIds, string userId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Team name is required.");

        var team = new Team
        {
            Name = name,
            NameAr = nameAr,
            Description = description,
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Members = initialMemberUserIds.Distinct()
                .Select(uid => new TeamMember { UserId = uid, AddedAt = DateTime.UtcNow })
                .ToList(),
        };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Team", team.Id.ToString(), userId, newValue: team.Name);
        return team;
    }

    public async Task UpdateAsync(int teamId, string name, string? nameAr, string? description, string userId)
    {
        var team = await _db.Teams.FindAsync(teamId) ?? throw new InvalidOperationException("Team not found.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Team name is required.");

        team.Name = name;
        team.NameAr = nameAr;
        team.Description = description;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Team", teamId.ToString(), userId, newValue: name);
    }

    public async Task SetActiveAsync(int teamId, bool isActive, string userId)
    {
        var team = await _db.Teams.FindAsync(teamId) ?? throw new InvalidOperationException("Team not found.");
        team.IsActive = isActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(isActive ? "Activate" : "Deactivate", "Team", teamId.ToString(), userId);
    }

    // Audit values are shown to admins as-is (e.g. on the per-user profile Audit Log tab) — log
    // the member's name, not the raw user-id GUID, so the entry is actually readable.
    private async Task<string?> NameOfAsync(string? uid) => uid == null ? null
        : await _db.Users.Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync();

    public async Task AddMemberAsync(int teamId, string userId, string actingUserId)
    {
        var exists = await _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (exists) return;
        _db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = userId, AddedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AddMember", "Team", teamId.ToString(), actingUserId, newValue: await NameOfAsync(userId));
    }

    public async Task RemoveMemberAsync(int teamId, string userId, string actingUserId)
    {
        var member = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (member == null) return;
        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("RemoveMember", "Team", teamId.ToString(), actingUserId, oldValue: await NameOfAsync(userId));
    }

    public Task<bool> IsMemberAsync(int teamId, string userId) =>
        _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId);

    /// <summary>Reconciles a team's membership to exactly the given user id list — diffs against
    /// the current members and adds/removes only what changed, so the audit trail reads the same
    /// as the old separate AddMember/RemoveMember actions this replaces on the Edit page.</summary>
    public async Task SetMembersAsync(int teamId, List<string> memberUserIds, string actingUserId)
    {
        var current = await _db.TeamMembers.Where(m => m.TeamId == teamId).ToListAsync();
        var currentIds = current.Select(m => m.UserId).ToHashSet();
        var wantedIds = memberUserIds.Distinct().ToHashSet();

        var toRemove = current.Where(m => !wantedIds.Contains(m.UserId)).ToList();
        var toAddIds = wantedIds.Where(id => !currentIds.Contains(id)).ToList();

        _db.TeamMembers.RemoveRange(toRemove);
        foreach (var id in toAddIds)
            _db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = id, AddedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        foreach (var m in toRemove)
            await _audit.LogAsync("RemoveMember", "Team", teamId.ToString(), actingUserId, oldValue: await NameOfAsync(m.UserId));
        foreach (var id in toAddIds)
            await _audit.LogAsync("AddMember", "Team", teamId.ToString(), actingUserId, newValue: await NameOfAsync(id));
    }
}
