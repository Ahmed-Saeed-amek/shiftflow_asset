using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Services;

/// <summary>
/// The signed-in user's effective permissions, resolved once per request.
/// _Layout used to run ~21 separate IAuthorizationService.AuthorizeAsync calls (each of which
/// opens its own DI scope) plus a UserManager.GetUserAsync, and feature views then repeated the
/// same checks — this collapses all of it into one IPermissionService lookup (itself memory-cached
/// per user for 5 minutes) with the exact same Deny &gt; Allow &gt; Role semantics.
/// </summary>
public sealed class NavPermissionSet
{
    public static readonly NavPermissionSet Anonymous = new(new HashSet<string>(StringComparer.Ordinal), null, null);

    private readonly HashSet<string> _granted;

    public NavPermissionSet(HashSet<string> granted, string? userId, string? displayName)
    {
        _granted = granted;
        UserId = userId;
        DisplayName = displayName;
    }

    /// <summary>Identity user id, or null when not signed in.</summary>
    public string? UserId { get; }

    /// <summary>FullName when set, otherwise the login name — what the sidebar greets the user by.</summary>
    public string? DisplayName { get; }

    /// <summary>True when the user effectively holds <paramref name="permission"/> (any PermissionCatalog constant).</summary>
    public bool Has(string permission) => _granted.Contains(permission);

    /// <summary>True when the user holds at least one of the given permissions.</summary>
    public bool HasAny(params string[] permissions) => permissions.Any(_granted.Contains);

    public bool CanUseAi => Has(PermissionCatalog.AiAssistantUse);
    public bool CanManageInspectionOrders => Has(PermissionCatalog.InspectionOrderManage);
    public bool CanViewInspectionOrders => Has(PermissionCatalog.InspectionOrderView);
    public bool CanManageMaintenanceOrders => Has(PermissionCatalog.MaintenanceOrderManage);
    public bool CanViewMaintenanceOrders => Has(PermissionCatalog.MaintenanceOrderView);
    public bool CanViewGroups => Has(PermissionCatalog.GroupView);
    public bool CanViewMyWork => Has(PermissionCatalog.MyWorkView);
    public bool CanViewAssets => Has(PermissionCatalog.AssetView);
    public bool CanViewVendors => Has(PermissionCatalog.VendorView);
    public bool CanViewWorkOrders => Has(PermissionCatalog.WorkOrderView);
    public bool CanManageWorkOrders => Has(PermissionCatalog.WorkOrderManage);
    public bool CanViewContracts => Has(PermissionCatalog.ContractView);
    public bool CanViewSpareParts => Has(PermissionCatalog.SparePartView);
    public bool CanReportAssetAction => Has(PermissionCatalog.AssetReportAction);
    public bool CanManageAssetScope => Has(PermissionCatalog.AssetScopeManage);
    public bool CanManageOrderTypes => Has(PermissionCatalog.OrderTypeManage);
    public bool CanManageAssetCategories => Has(PermissionCatalog.AssetCategoryManage);
    public bool CanViewUsers => Has(PermissionCatalog.UserView);
    public bool CanViewAuditLogs => Has(PermissionCatalog.AuditLogView);
    public bool CanManageRbac => Has(PermissionCatalog.RbacManage);
}

/// <summary>Request-scoped accessor for <see cref="NavPermissionSet"/>. Inject into any view.</summary>
public interface INavPermissions
{
    Task<NavPermissionSet> GetAsync();
}

public sealed class NavPermissionsProvider : INavPermissions
{
    private readonly IHttpContextAccessor _accessor;

    public NavPermissionsProvider(IHttpContextAccessor accessor) => _accessor = accessor;

    public Task<NavPermissionSet> GetAsync() =>
        _accessor.HttpContext?.GetNavPermissionsAsync() ?? Task.FromResult(NavPermissionSet.Anonymous);
}

public static class NavPermissionsHttpContextExtensions
{
    private const string ItemsKey = "__ShiftFlow_NavPermissions";

    /// <summary>
    /// The permission set for this request, computed at most once and cached on HttpContext.Items.
    /// Usable straight from a Razor view (<c>await Context.GetNavPermissionsAsync()</c>) with no
    /// DI registration of its own — it resolves IPermissionService/UserManager off RequestServices.
    /// </summary>
    public static Task<NavPermissionSet> GetNavPermissionsAsync(this HttpContext http)
    {
        if (http.Items.TryGetValue(ItemsKey, out var cached) && cached is Task<NavPermissionSet> task)
            return task;

        var fresh = LoadAsync(http);
        http.Items[ItemsKey] = fresh;
        return fresh;
    }

    private static async Task<NavPermissionSet> LoadAsync(HttpContext http)
    {
        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true) return NavPermissionSet.Anonymous;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return NavPermissionSet.Anonymous;

        var permissions = http.RequestServices.GetRequiredService<IPermissionService>();
        var userManager = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();

        var granted = (await permissions.GetUserEffectivePermissionsAsync(userId)).ToHashSet(StringComparer.Ordinal);
        var displayName = (await userManager.FindByIdAsync(userId))?.FullName ?? user.Identity?.Name;
        return new NavPermissionSet(granted, userId, displayName);
    }
}

public static class NavPermissionsServiceCollectionExtensions
{
    /// <summary>
    /// Optional: registers <see cref="INavPermissions"/> for views/controllers that prefer
    /// constructor/@inject over <c>Context.GetNavPermissionsAsync()</c>. Not required — the
    /// HttpContext extension above works without any registration.
    /// </summary>
    public static IServiceCollection AddNavPermissions(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<INavPermissions, NavPermissionsProvider>();
        return services;
    }
}
