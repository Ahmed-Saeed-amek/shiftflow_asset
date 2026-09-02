using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using Xunit;

namespace ShiftFlow.Web.Tests;

/// <summary>
/// Regression coverage for the TOCTOU race in WorkOrder stage-transition methods: two concurrent
/// callers must not both succeed in appending a stage-history row for the same transition.
///
/// Racing two Task.WhenAll calls against a fast local SQLite database is NOT a reliable way to
/// reproduce this - both calls typically run start-to-finish without ever actually interleaving
/// at the vulnerable window, so a test built that way passes against BOTH the guarded and the
/// unguarded (buggy) implementation and proves nothing. Instead, each test below deterministically
/// stages the real root cause: EF Core's DbSet.FindAsync returns an already-tracked entity straight
/// out of the DbContext's change tracker without re-querying the database. So "request A" loads and
/// tracks the WorkOrder first; "request B" (a fully separate DbContext/service instance) then
/// completes its own transition and commits; "request A" then resumes using its now-stale tracked
/// entity - exactly the window a real concurrent HTTP request pair would race through. A correctly
/// guarded method must reject request A's stale-based attempt via a direct database-state check
/// (ExecuteUpdateAsync's WHERE clause), not an in-memory property check on the stale entity.
/// </summary>
public class WorkOrderConcurrencyTests
{
    /// <summary>A shared-cache SQLite in-memory database, addressed by connection string so each
    /// ApplicationDbContext gets its own independent connection - all pointing at the same
    /// underlying database as long as the keep-alive connection stays open.</summary>
    private static (string ConnectionString, SqliteConnection KeepAlive) CreateSharedMemoryDb()
    {
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        return (connectionString, keepAlive);
    }

    private static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options);

    private static WorkOrderService CreateService(ApplicationDbContext db)
    {
        var audit = new AuditService(db);
        return new WorkOrderService(db, audit, new SparePartService(db, audit));
    }

    private static async Task<int> SeedWorkOrderAsync(string connectionString, string stage)
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

        var user = new ApplicationUser { Id = "test-user-1", UserName = "tester@test.local", FullName = "Test User" };
        db.Users.Add(user);
        db.Vendors.Add(new Vendor { Id = 1, Name = "Test Vendor", Status = "Active" });

        var asset = new Asset
        {
            AssetTag = $"TEST-{Guid.NewGuid():N}"[..12], Name = "Test Asset",
            CategoryId = category.Id, ZoneId = zone.Id, CreatedByUserId = user.Id,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var workOrder = new WorkOrder
        {
            WorkOrderNumber = $"WO-TEST-{Guid.NewGuid():N}"[..16],
            AssetId = asset.Id, Stage = stage, CreatedByUserId = user.Id,
        };
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        return workOrder.Id;
    }

    [Fact]
    public async Task ForceCloseAsync_StaleReadAfterConcurrentClose_RejectsInsteadOfDuplicatingStageHistory()
    {
        var (connectionString, keepAlive) = CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "Sent to Vendor");

        // "Request A" loads (and tracks) the work order while it's still "Sent to Vendor".
        using var dbA = CreateContext(connectionString);
        await dbA.WorkOrders.FindAsync(workOrderId);

        // "Request B" - a fully independent DbContext/service - force-closes it first and commits.
        using (var dbB = CreateContext(connectionString))
            await CreateService(dbB).ForceCloseAsync(workOrderId, "request B", "test-user-1");

        // "Request A" resumes: its own internal FindAsync(workOrderId) call will return the
        // already-tracked, now-stale entity (still showing "Sent to Vendor") instead of re-querying
        // the database - the real TOCTOU window. Must be rejected, not silently succeed.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(dbA).ForceCloseAsync(workOrderId, "request A", "test-user-1"));
        Assert.Equal("Already closed.", ex.Message);

        using var verifyDb = CreateContext(connectionString);
        Assert.Equal(1, await verifyDb.WorkOrderStageEvents.CountAsync(e => e.WorkOrderId == workOrderId && e.Stage == "Closed"));
        Assert.Equal("Closed", await verifyDb.WorkOrders.Where(w => w.Id == workOrderId).Select(w => w.Stage).FirstAsync());
    }

    [Fact]
    public async Task SendToVendorAsync_StaleReadAfterConcurrentSend_RejectsInsteadOfDuplicatingStageHistory()
    {
        var (connectionString, keepAlive) = CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "New");

        using var dbA = CreateContext(connectionString);
        await dbA.WorkOrders.FindAsync(workOrderId);

        using (var dbB = CreateContext(connectionString))
            await CreateService(dbB).SendToVendorAsync(workOrderId, vendorId: 1, "test-user-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(dbA).SendToVendorAsync(workOrderId, vendorId: 1, "test-user-1"));
        Assert.Equal("Only a New work order can be sent to a vendor.", ex.Message);

        using var verifyDb = CreateContext(connectionString);
        Assert.Equal(1, await verifyDb.WorkOrderStageEvents.CountAsync(e => e.WorkOrderId == workOrderId && e.Stage == "Sent to Vendor"));
    }

    [Fact]
    public async Task EmployeeFixAsync_StaleReadAfterConcurrentFix_RejectsInsteadOfDuplicatingStageHistory()
    {
        var (connectionString, keepAlive) = CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "New");
        using (var db = CreateContext(connectionString))
        {
            var wo = await db.WorkOrders.FindAsync(workOrderId);
            wo!.AssignedToUserId = "test-user-1";
            await db.SaveChangesAsync();
        }

        using var dbA = CreateContext(connectionString);
        await dbA.WorkOrders.Include(w => w.Parts).FirstAsync(w => w.Id == workOrderId);

        using (var dbB = CreateContext(connectionString))
            await CreateService(dbB).EmployeeFixAsync(workOrderId, "fixed by B", null, null, new List<(int, int)>(), "test-user-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(dbA).EmployeeFixAsync(workOrderId, "fixed by A", null, null, new List<(int, int)>(), "test-user-1"));
        Assert.Equal("This work order isn't awaiting a fix.", ex.Message);

        using var verifyDb = CreateContext(connectionString);
        Assert.Equal(1, await verifyDb.WorkOrderStageEvents.CountAsync(e => e.WorkOrderId == workOrderId && e.Stage == "Fixed - Pending Confirmation"));
    }
}
