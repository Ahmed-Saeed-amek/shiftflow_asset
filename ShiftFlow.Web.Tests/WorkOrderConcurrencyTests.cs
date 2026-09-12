using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using Xunit;

namespace ShiftFlow.Web.Tests;

/// <summary>
/// Regression coverage for the TOCTOU race in WorkOrder stage-transition methods: two concurrent
/// callers must not both succeed in appending a stage-history row for the same transition.
///
/// Racing two Task.WhenAll calls against a fast local SQLite database is NOT a reliable way to
/// reproduce this - both calls typically run start-to-finish without ever actually interleaving
/// at the vulnerable window. Instead, each test below deterministically stages the real root
/// cause: EF Core's DbSet.FindAsync returns an already-tracked entity straight out of the change
/// tracker without re-querying. So "request A" loads and tracks the WorkOrder first; "request B"
/// (a separate DbContext/service) completes its own transition and commits; "request A" then
/// resumes using its now-stale tracked entity. A correctly guarded method must reject it via a
/// database-state check (ExecuteUpdateAsync's WHERE clause), not an in-memory property check.
/// </summary>
public class WorkOrderConcurrencyTests
{
    private static async Task<int> SeedWorkOrderAsync(string connectionString, string stage)
    {
        var seed = await OrderTestDb.SeedAsync(connectionString);
        using var db = OrderTestDb.CreateContext(connectionString);
        var workOrder = new WorkOrder
        {
            WorkOrderNumber = $"WO-TEST-{Guid.NewGuid():N}"[..16],
            AssetId = seed.AssetId, Stage = stage, CreatedByUserId = seed.UserId,
        };
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        return workOrder.Id;
    }

    [Fact]
    public async Task ForceCloseAsync_StaleReadAfterConcurrentClose_RejectsInsteadOfDuplicatingStageHistory()
    {
        var (connectionString, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "Sent to Vendor");

        // "Request A" loads (and tracks) the work order while it's still "Sent to Vendor".
        using var dbA = OrderTestDb.CreateContext(connectionString);
        await dbA.WorkOrders.FindAsync(workOrderId);

        // "Request B" - a fully independent DbContext/service - force-closes it first and commits.
        using (var dbB = OrderTestDb.CreateContext(connectionString))
            await OrderTestDb.CreateWorkOrderService(dbB).ForceCloseAsync(workOrderId, "request B", "test-user-1");

        // "Request A" resumes against its stale tracked entity: must be rejected, not silently succeed.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OrderTestDb.CreateWorkOrderService(dbA).ForceCloseAsync(workOrderId, "request A", "test-user-1"));
        Assert.Equal("Already closed.", ex.Message);

        using var verifyDb = OrderTestDb.CreateContext(connectionString);
        Assert.Equal(1, await verifyDb.WorkOrderStageEvents.CountAsync(e => e.WorkOrderId == workOrderId && e.Stage == "Closed"));
        Assert.Equal("Closed", await verifyDb.WorkOrders.Where(w => w.Id == workOrderId).Select(w => w.Stage).FirstAsync());
    }

    [Fact]
    public async Task SendToVendorAsync_StaleReadAfterConcurrentSend_RejectsInsteadOfDuplicatingStageHistory()
    {
        var (connectionString, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "New");

        using var dbA = OrderTestDb.CreateContext(connectionString);
        await dbA.WorkOrders.FindAsync(workOrderId);

        using (var dbB = OrderTestDb.CreateContext(connectionString))
            await OrderTestDb.CreateWorkOrderService(dbB).SendToVendorAsync(workOrderId, vendorId: 1, "test-user-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OrderTestDb.CreateWorkOrderService(dbA).SendToVendorAsync(workOrderId, vendorId: 1, "test-user-1"));
        Assert.Equal("Only a New work order can be sent to a vendor.", ex.Message);

        using var verifyDb = OrderTestDb.CreateContext(connectionString);
        Assert.Equal(1, await verifyDb.WorkOrderStageEvents.CountAsync(e => e.WorkOrderId == workOrderId && e.Stage == "Sent to Vendor"));
    }

    [Fact]
    public async Task EmployeeFixAsync_StaleReadAfterConcurrentFix_RejectsInsteadOfDuplicatingStageHistory()
    {
        var (connectionString, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "New");
        using (var db = OrderTestDb.CreateContext(connectionString))
        {
            var wo = await db.WorkOrders.FindAsync(workOrderId);
            wo!.AssignedToUserId = "test-user-1";
            await db.SaveChangesAsync();
        }

        using var dbA = OrderTestDb.CreateContext(connectionString);
        await dbA.WorkOrders.Include(w => w.Parts).FirstAsync(w => w.Id == workOrderId);

        using (var dbB = OrderTestDb.CreateContext(connectionString))
            await OrderTestDb.CreateWorkOrderService(dbB).EmployeeFixAsync(workOrderId, null, [], "test-user-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OrderTestDb.CreateWorkOrderService(dbA).EmployeeFixAsync(workOrderId, null, [], "test-user-1"));
        Assert.Equal("This work order isn't awaiting a fix.", ex.Message);

        using var verifyDb = OrderTestDb.CreateContext(connectionString);
        Assert.Equal(1, await verifyDb.WorkOrderStageEvents.CountAsync(e => e.WorkOrderId == workOrderId && e.Stage == "Fixed - Pending Confirmation"));
    }

    /// <summary>RequiresVendorResponse was stored and shown but never enforced - a manager could
    /// still advance past the vendor.</summary>
    [Fact]
    public async Task AdvanceWithoutVendorAsync_WhenVendorResponseRequired_IsRejected()
    {
        var (connectionString, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var workOrderId = await SeedWorkOrderAsync(connectionString, "Sent to Vendor");
        using (var db = OrderTestDb.CreateContext(connectionString))
        {
            var wo = await db.WorkOrders.FindAsync(workOrderId);
            wo!.RequiresVendorResponse = true;
            wo.AssignedToUserId = "test-user-1";
            await db.SaveChangesAsync();
        }

        using var dbA = OrderTestDb.CreateContext(connectionString);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OrderTestDb.CreateWorkOrderService(dbA, "test-manager").AdvanceWithoutVendorAsync(workOrderId, null, [], "test-manager"));
        Assert.Contains("requires the vendor's own response", ex.Message);

        using var verifyDb = OrderTestDb.CreateContext(connectionString);
        Assert.Equal("Sent to Vendor", await verifyDb.WorkOrders.Where(w => w.Id == workOrderId).Select(w => w.Stage).FirstAsync());
    }
}
