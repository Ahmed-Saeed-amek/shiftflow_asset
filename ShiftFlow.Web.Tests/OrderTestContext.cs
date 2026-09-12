using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Web.Tests;

/// <summary>ApplicationDbContext plus the OrderNumberSequence mapping. The production mapping lives
/// in ApplicationDbContext (owned by another change — see NOTES-orders.md); until it lands, tests
/// map the entity here so EnsureCreated builds the table.</summary>
public class TestDbContext : ApplicationDbContext
{
    public TestDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<OrderNumberSequence>(e =>
        {
            e.ToTable("OrderNumberSequences");
            e.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.Prefix, x.Year }).IsUnique();
        });
    }
}

/// <summary>Stub permission service — tests decide who is a manager without an Identity stack.</summary>
public sealed class StubPermissionService : IPermissionService
{
    private readonly HashSet<string> _managers;
    public StubPermissionService(params string[] managerUserIds) => _managers = [.. managerUserIds];

    public Task<bool> HasPermissionAsync(string userId, string permission) => Task.FromResult(_managers.Contains(userId));
    public Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(string userId) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task InvalidateCacheAsync(string userId) => Task.CompletedTask;
    public Task<IReadOnlyList<Permission>> GetAllPermissionsAsync() => Task.FromResult<IReadOnlyList<Permission>>([]);
    public Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(string roleId) => Task.FromResult<IReadOnlyList<RolePermission>>([]);
    public Task AssignRolePermissionAsync(string roleId, string permissionName) => Task.CompletedTask;
    public Task RemoveRolePermissionAsync(string roleId, string permissionName) => Task.CompletedTask;
    public Task<IReadOnlyList<UserPermission>> GetUserPermissionOverridesAsync(string userId) => Task.FromResult<IReadOnlyList<UserPermission>>([]);
    public Task SetUserPermissionOverrideAsync(string userId, string permissionName, bool isGranted) => Task.CompletedTask;
    public Task RemoveUserPermissionOverrideAsync(string userId, string permissionName) => Task.CompletedTask;
}

/// <summary>Minimal ILanguageService — the order services only use it for PDF export labels.</summary>
public sealed class StubLanguageService : ILanguageService
{
    public string Lang => "en";
    public bool IsRTL => false;
    public string T(string key) => key;
    public string T(string key, params object[] args) => string.Format(key, args);
    public string TDate(string formattedDate) => formattedDate;
}

/// <summary>Shared SQLite-in-memory harness: one database per test, addressed by connection string
/// so several DbContexts can point at it independently (that is how the concurrency tests stage a
/// stale read), kept alive by a single open connection.</summary>
public static class OrderTestDb
{
    public static (string ConnectionString, SqliteConnection KeepAlive) CreateSharedMemoryDb()
    {
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        return (connectionString, keepAlive);
    }

    public static TestDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options);

    public static WorkOrderService CreateWorkOrderService(ApplicationDbContext db, params string[] managerUserIds)
    {
        var audit = new AuditService(db);
        return new WorkOrderService(db, audit, new SparePartService(db, audit), new AssetScopeService(db),
            new StubLanguageService(), new StubPermissionService(managerUserIds));
    }

    public static InspectionOrderService CreateInspectionOrderService(ApplicationDbContext db, params string[] managerUserIds)
    {
        var audit = new AuditService(db);
        return new InspectionOrderService(db, audit, new GroupService(db, audit), new AssetScopeService(db),
            CreateWorkOrderService(db, managerUserIds));
    }

    public static MaintenanceOrderService CreateMaintenanceOrderService(ApplicationDbContext db)
    {
        var audit = new AuditService(db);
        return new MaintenanceOrderService(db, audit, new SparePartService(db, audit), new GroupService(db, audit), new AssetScopeService(db));
    }

    /// <summary>Creates the schema and the shared reference rows every order test needs.</summary>
    public static async Task<SeedIds> SeedAsync(string connectionString)
    {
        using var db = CreateContext(connectionString);
        await db.Database.EnsureCreatedAsync();

        var category = new AssetCategory { Name = "Test Category" };
        var locationCategory = new LocationCategory { Name = "Test Location Category" };
        db.AssetCategories.Add(category);
        db.LocationCategories.Add(locationCategory);
        await db.SaveChangesAsync();

        var zone = new Zone { Name = "Test Zone", LocationCategoryId = locationCategory.Id };
        db.Zones.Add(zone);
        await db.SaveChangesAsync();

        db.Users.Add(new ApplicationUser { Id = "test-user-1", UserName = "tester@test.local", FullName = "Test User", IsActive = true });
        db.Users.Add(new ApplicationUser { Id = "test-manager", UserName = "manager@test.local", FullName = "Test Manager", IsActive = true });
        db.Vendors.Add(new Vendor { Id = 1, Name = "Test Vendor", Status = "Active" });

        var asset = new Asset
        {
            AssetTag = $"TEST-{Guid.NewGuid():N}"[..12], Name = "Test Asset",
            CategoryId = category.Id, ZoneId = zone.Id, CreatedByUserId = "test-user-1",
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        return new SeedIds(asset.Id, "test-user-1", "test-manager");
    }

    public record SeedIds(int AssetId, string UserId, string ManagerId);
}
