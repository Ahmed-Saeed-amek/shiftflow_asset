using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.RbacManage)]
public class RbacController : Controller
{
    private readonly IPermissionService _permissions;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public RbacController(
        IPermissionService permissions,
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IAuditService audit)
    {
        _permissions = permissions;
        _roleManager = roleManager;
        _userManager = userManager;
        _db = db;
        _audit = audit;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    // -------------------------------------------------------------------------
    // Role CRUD
    // -------------------------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(string roleName, string? roleNameAr)
    {
        roleName = roleName?.Trim() ?? string.Empty;
        roleNameAr = string.IsNullOrWhiteSpace(roleNameAr) ? null : roleNameAr.Trim();
        if (string.IsNullOrWhiteSpace(roleName))
        {
            TempData["Error"] = "Role name cannot be empty.";
            return RedirectToAction(nameof(Index));
        }

        if (await _roleManager.RoleExistsAsync(roleName))
        {
            TempData["Error"] = $"Role '{roleName}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        var role = new ApplicationRole(roleName) { NameAr = roleNameAr };
        var result = await _roleManager.CreateAsync(role);
        if (result.Succeeded)
        {
            await _audit.LogAsync("Create", "Role", role.Id, CurrentUserId, newValue: roleName);
            TempData["Success"] = $"Role '{roleName}' created.";
        }
        else
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(string roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            TempData["Error"] = "Role not found.";
            return RedirectToAction(nameof(Index));
        }

        // Block deletion if any users are still assigned to this role
        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Count > 0)
        {
            TempData["Error"] = $"Cannot delete '{role.Name}' — {usersInRole.Count} user(s) are still assigned to it. Remove the role from all users first.";
            return RedirectToAction(nameof(Index));
        }

        // Remove all role-permission grants before deleting
        var rolePerms = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();
        _db.RolePermissions.RemoveRange(rolePerms);
        await _db.SaveChangesAsync();

        var result = await _roleManager.DeleteAsync(role);
        if (result.Succeeded)
        {
            await _audit.LogAsync("Delete", "Role", roleId, CurrentUserId, oldValue: role.Name);
            TempData["Success"] = $"Role '{role.Name}' deleted.";
        }
        else
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));

        return RedirectToAction(nameof(Index));
    }

    // -------------------------------------------------------------------------
    // Bulk role assignment
    // -------------------------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssignRoles(
        List<string> userIds,
        List<string> addRoles,
        List<string> removeRoles)
    {
        userIds    ??= [];
        addRoles   ??= [];
        removeRoles ??= [];

        if (userIds.Count == 0)
        {
            TempData["Error"] = "No users selected.";
            return RedirectToAction(nameof(Index));
        }

        int changed = 0;
        var errors  = new List<string>();

        foreach (var userId in userIds)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) continue;

            foreach (var role in addRoles)
            {
                if (!await _userManager.IsInRoleAsync(user, role))
                {
                    var r = await _userManager.AddToRoleAsync(user, role);
                    if (r.Succeeded)
                    {
                        changed++;
                        await _audit.LogAsync("AddRole", "User", userId, CurrentUserId, newValue: role);
                    }
                    else errors.AddRange(r.Errors.Select(e => e.Description));
                }
            }

            foreach (var role in removeRoles)
            {
                if (await _userManager.IsInRoleAsync(user, role))
                {
                    var r = await _userManager.RemoveFromRoleAsync(user, role);
                    if (r.Succeeded)
                    {
                        changed++;
                        await _audit.LogAsync("RemoveRole", "User", userId, CurrentUserId, oldValue: role);
                    }
                    else errors.AddRange(r.Errors.Select(e => e.Description));
                }
            }

            // Invalidate permission cache for this user
            await _permissions.InvalidateCacheAsync(userId);
        }

        if (errors.Count > 0)
            TempData["Error"] = string.Join("; ", errors.Distinct());
        else
            TempData["Success"] = $"Roles updated for {userIds.Count} user(s). {changed} change(s) applied.";

        return RedirectToAction(nameof(Index));
    }

    // -------------------------------------------------------------------------
    // Index — lists all roles and all users side-by-side
    // -------------------------------------------------------------------------

    public async Task<IActionResult> Index()
    {
        var roles = await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync();
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync();

        // Single query instead of N per-user GetRolesAsync calls
        var rolesByUser = await _db.UserRoles
            .AsNoTracking()
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(g => g.Key, g => (IList<string>)g.Select(x => x.Name!).ToList());

        foreach (var u in users)
            rolesByUser.TryAdd(u.Id, []);

        ViewBag.RolesByUser = rolesByUser;
        ViewBag.Roles = roles;
        return View(users);
    }

    // -------------------------------------------------------------------------
    // Role permissions — checkbox grid
    // -------------------------------------------------------------------------

    public async Task<IActionResult> RolePermissions(string roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role is null) return NotFound();

        var allPerms = await _permissions.GetAllPermissionsAsync();
        var rolePerms = (await _permissions.GetRolePermissionsAsync(roleId))
            .Select(rp => rp.PermissionName)
            .ToHashSet();

        ViewBag.Role = role;
        ViewBag.RolePerms = rolePerms;
        return View(allPerms);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRolePermissions(string roleId, List<string> granted)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role is null) return NotFound();

        var allPerms = PermissionCatalog.All;
        var currentlyGranted = (await _permissions.GetRolePermissionsAsync(roleId))
            .Select(rp => rp.PermissionName)
            .ToHashSet();

        granted ??= [];

        // Add newly checked
        var added = granted.Where(p => !currentlyGranted.Contains(p)).ToList();
        foreach (var perm in added)
            await _permissions.AssignRolePermissionAsync(roleId, perm);

        // Remove unchecked
        var removed = currentlyGranted.Where(p => !granted.Contains(p)).ToList();
        foreach (var perm in removed)
            await _permissions.RemoveRolePermissionAsync(roleId, perm);

        if (added.Count > 0 || removed.Count > 0)
            await _audit.LogAsync("SavePermissions", "Role", roleId, CurrentUserId,
                oldValue: removed.Count > 0 ? $"-{string.Join(",", removed)}" : null,
                newValue: added.Count > 0 ? $"+{string.Join(",", added)}" : null);

        TempData["Success"] = $"Permissions saved for role '{role.Name}'.";
        return RedirectToAction(nameof(RolePermissions), new { roleId });
    }

    // -------------------------------------------------------------------------
    // User permissions — shows inherited (from roles) + overrides
    // -------------------------------------------------------------------------

    public async Task<IActionResult> UserPermissions(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var allPerms = await _permissions.GetAllPermissionsAsync();
        var userRoles = await _userManager.GetRolesAsync(user);

        // Collect role-granted permissions per role for display
        var roleGrants = new Dictionary<string, HashSet<string>>();
        var roleNameArByName = new Dictionary<string, string?>();
        foreach (var roleName in userRoles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            var perms = (await _permissions.GetRolePermissionsAsync(role.Id))
                .Select(rp => rp.PermissionName)
                .ToHashSet();
            roleGrants[roleName] = perms;
            roleNameArByName[roleName] = role.NameAr;
        }

        var overrides = await _permissions.GetUserPermissionOverridesAsync(userId);
        var allowOverrides = overrides.Where(o => o.IsGranted).Select(o => o.PermissionName).ToHashSet();
        var denyOverrides = overrides.Where(o => !o.IsGranted).Select(o => o.PermissionName).ToHashSet();

        ViewBag.User = user;
        ViewBag.UserRoles = userRoles;
        ViewBag.RoleGrants = roleGrants;
        ViewBag.RoleNameArByName = roleNameArByName;
        ViewBag.AllowOverrides = allowOverrides;
        ViewBag.DenyOverrides = denyOverrides;
        return View(allPerms);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUserPermissions(string userId, List<string> allowList, List<string> denyList)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        allowList ??= [];
        denyList ??= [];

        var allPerms = PermissionCatalog.All;
        var existingOverrides = await _permissions.GetUserPermissionOverridesAsync(userId);
        var changes = new List<string>();

        foreach (var perm in allPerms)
        {
            bool wantAllow = allowList.Contains(perm);
            bool wantDeny = denyList.Contains(perm);
            var existing = existingOverrides.FirstOrDefault(o => o.PermissionName == perm);

            if (wantDeny)
            {
                await _permissions.SetUserPermissionOverrideAsync(userId, perm, isGranted: false);
                changes.Add($"{perm}=Deny");
            }
            else if (wantAllow)
            {
                await _permissions.SetUserPermissionOverrideAsync(userId, perm, isGranted: true);
                changes.Add($"{perm}=Allow");
            }
            else if (existing is not null)
            {
                await _permissions.RemoveUserPermissionOverrideAsync(userId, perm);
                changes.Add($"{perm}=Removed");
            }
        }

        if (changes.Count > 0)
            await _audit.LogAsync("SavePermissionOverrides", "User", userId, CurrentUserId, newValue: string.Join(",", changes));

        TempData["Success"] = $"Permission overrides saved for '{user.FullName}'.";
        return RedirectToAction(nameof(UserPermissions), new { userId });
    }
}
