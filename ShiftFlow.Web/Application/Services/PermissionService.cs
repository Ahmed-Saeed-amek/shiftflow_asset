using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.Services;

public sealed class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public PermissionService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IMemoryCache cache)
    {
        _db = db;
        _userManager = userManager;
        _cache = cache;
    }

    // -------------------------------------------------------------------------
    // Core evaluation
    // -------------------------------------------------------------------------

    public async Task<bool> HasPermissionAsync(string userId, string permission)
    {
        var granted = await GetUserEffectivePermissionsAsync(userId);
        return granted.Contains(permission);
    }

    public async Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(string userId)
    {
        var cacheKey = CacheKey(userId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var result = await ComputeEffectivePermissionsAsync(userId);

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheDuration
        });

        return result;
    }

    private async Task<IReadOnlyList<string>> ComputeEffectivePermissionsAsync(string userId)
    {
        // 1. Load all user-level overrides
        var overrides = await _db.UserPermissions
            .Where(up => up.UserId == userId)
            .ToListAsync();

        var deniedSet = overrides.Where(up => !up.IsGranted).Select(up => up.PermissionName).ToHashSet();
        var allowedSet = overrides.Where(up => up.IsGranted).Select(up => up.PermissionName).ToHashSet();

        // 2. Load role-level permissions for all roles this user belongs to
        var user = await _userManager.FindByIdAsync(userId);
        var roleNames = user is null
            ? []
            : (IList<string>)await _userManager.GetRolesAsync(user);

        var roleIds = await _db.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        var roleGranted = await _db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionName)
            .Distinct()
            .ToListAsync();

        // 3. Evaluate: Deny > Allow > Role
        var effective = new HashSet<string>();

        foreach (var perm in allowedSet)
        {
            if (!deniedSet.Contains(perm))
                effective.Add(perm);
        }

        foreach (var perm in roleGranted)
        {
            if (!deniedSet.Contains(perm))
                effective.Add(perm);
        }

        // Super-permission: holding IsAdmin implicitly grants every permission,
        // including ones added to the catalog after this user was granted it.
        if (effective.Contains(PermissionCatalog.IsAdmin))
            return PermissionCatalog.All;

        return [.. effective];
    }

    public Task InvalidateCacheAsync(string userId)
    {
        _cache.Remove(CacheKey(userId));
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Admin: Permission catalog
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<Permission>> GetAllPermissionsAsync()
        => await _db.Permissions.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();

    // -------------------------------------------------------------------------
    // Admin: Role permissions
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(string roleId)
        => await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();

    public async Task AssignRolePermissionAsync(string roleId, string permissionName)
    {
        var exists = await _db.RolePermissions
            .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionName == permissionName);
        if (exists) return;

        _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionName = permissionName });
        await _db.SaveChangesAsync();

        // Invalidate cache for all users in this role
        await InvalidateCacheForRoleAsync(roleId);
    }

    public async Task RemoveRolePermissionAsync(string roleId, string permissionName)
    {
        var entry = await _db.RolePermissions
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionName == permissionName);
        if (entry is null) return;

        _db.RolePermissions.Remove(entry);
        await _db.SaveChangesAsync();

        await InvalidateCacheForRoleAsync(roleId);
    }

    // -------------------------------------------------------------------------
    // Admin: User permission overrides
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<UserPermission>> GetUserPermissionOverridesAsync(string userId)
        => await _db.UserPermissions.Where(up => up.UserId == userId).ToListAsync();

    public async Task SetUserPermissionOverrideAsync(string userId, string permissionName, bool isGranted)
    {
        var existing = await _db.UserPermissions
            .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionName == permissionName);

        if (existing is null)
        {
            _db.UserPermissions.Add(new UserPermission
            {
                UserId = userId,
                PermissionName = permissionName,
                IsGranted = isGranted
            });
        }
        else
        {
            existing.IsGranted = isGranted;
        }

        await _db.SaveChangesAsync();
        await InvalidateCacheAsync(userId);
    }

    public async Task RemoveUserPermissionOverrideAsync(string userId, string permissionName)
    {
        var entry = await _db.UserPermissions
            .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionName == permissionName);
        if (entry is null) return;

        _db.UserPermissions.Remove(entry);
        await _db.SaveChangesAsync();
        await InvalidateCacheAsync(userId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string CacheKey(string userId) => $"perms:{userId}";

    private async Task InvalidateCacheForRoleAsync(string roleId)
    {
        // Find all users in this role and evict their caches
        var usersInRole = await _db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync();

        foreach (var uid in usersInRole)
            _cache.Remove(CacheKey(uid));
    }
}
