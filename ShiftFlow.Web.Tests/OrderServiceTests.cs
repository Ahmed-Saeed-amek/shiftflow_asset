using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using Xunit;

namespace ShiftFlow.Web.Tests;

/// <summary>Covers the order-service fixes that aren't stage-transition races: atomic approval,
/// the Defective -> Work Order rule living inside one transaction, stock restoration on a
/// re-submitted fix, and the PendingApproval -> Open escape hatch.</summary>
public class OrderServiceTests
{
    private static async Task<int> StockAsync(string connectionString, int partId)
    {
        using var db = OrderTestDb.CreateContext(connectionString);
        return await db.SpareParts.Where(p => p.Id == partId).Select(p => p.StockQuantity).FirstAsync();
    }

    private static async Task<(int PartId, int OrderTypeId)> SeedPartAndTypeAsync(string connectionString, int assetId,
        int stock, bool requiresApproval = false, bool isDirectFix = true)
    {
        using var db = OrderTestDb.CreateContext(connectionString);
        var part = new SparePart { Name = $"Part {Guid.NewGuid():N}"[..12], StockQuantity = stock, UnitCost = 10m, IsActive = true };
        db.SpareParts.Add(part);
        var type = new OrderType
        {
            Name = $"Type {Guid.NewGuid():N}"[..12], Prefix = "MT", IsActive = true,
            IsDirectFix = isDirectFix, RequiresApproval = requiresApproval,
        };
        db.OrderTypes.Add(type);
        await db.SaveChangesAsync();
        db.SparePartAssets.Add(new SparePartAsset { SparePartId = part.Id, AssetId = assetId });
        await db.SaveChangesAsync();
        return (part.Id, type.Id);
    }

    // ---------- Finding 2: ApproveAsync must not overwrite a concurrent Cancel ----------

    [Fact]
    public async Task MaintenanceApproveAsync_AfterConcurrentCancel_IsRejected()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (_, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 5, requiresApproval: true);

        int orderId;
        using (var db = OrderTestDb.CreateContext(cs))
        {
            var order = await OrderTestDb.CreateMaintenanceOrderService(db)
                .CreateAsync(seed.AssetId, seed.UserId, null, null, null, seed.ManagerId, typeId);
            orderId = order.Id;
            await OrderTestDb.CreateMaintenanceOrderService(db).CompleteAsync(orderId, null, [], seed.UserId);
        }

        // "Request A" loads (and tracks) the order while it is still PendingApproval.
        using var dbA = OrderTestDb.CreateContext(cs);
        await dbA.MaintenanceOrders.FindAsync(orderId);

        using (var dbB = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateMaintenanceOrderService(dbB).CancelAsync(orderId, "changed our mind", seed.ManagerId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OrderTestDb.CreateMaintenanceOrderService(dbA).ApproveAsync(orderId, seed.ManagerId));
        Assert.Equal("This order isn't awaiting approval.", ex.Message);

        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal("Cancelled", await verify.MaintenanceOrders.Where(m => m.Id == orderId).Select(m => m.Status).FirstAsync());
    }

    [Fact]
    public async Task InspectionApproveAsync_AfterConcurrentCancel_IsRejected()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (_, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 5, requiresApproval: true, isDirectFix: false);

        int orderId;
        using (var db = OrderTestDb.CreateContext(cs))
        {
            var svc = OrderTestDb.CreateInspectionOrderService(db);
            var order = await svc.CreateAsync(typeId, null, seed.UserId, null, [seed.AssetId], null, seed.ManagerId);
            orderId = order.Id;
            var itemId = await db.InspectionRunAssets.Where(i => i.InspectionRun!.InspectionOrderId == orderId).Select(i => i.Id).FirstAsync();
            await svc.UpdateInspectionItemAsync(itemId, "OK", null, null, null, seed.UserId);
        }

        using var dbA = OrderTestDb.CreateContext(cs);
        await dbA.InspectionOrders.FindAsync(orderId);

        using (var dbB = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateInspectionOrderService(dbB).CancelAsync(orderId, "changed our mind", seed.ManagerId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OrderTestDb.CreateInspectionOrderService(dbA).ApproveAsync(orderId, seed.ManagerId));
        Assert.Equal("This order isn't awaiting approval.", ex.Message);

        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal("Cancelled", await verify.InspectionOrders.Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync());
    }

    // ---------- Finding 3: Defective spawns its Work Order inside the same transaction ----------

    [Fact]
    public async Task UpdateInspectionItemAsync_DefectiveOutcome_CreatesWorkOrderAndLinksIt()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (_, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 5, isDirectFix: false);

        int itemId;
        int? workOrderId;
        using (var db = OrderTestDb.CreateContext(cs))
        {
            var svc = OrderTestDb.CreateInspectionOrderService(db);
            var order = await svc.CreateAsync(typeId, null, seed.UserId, null, [seed.AssetId], null, seed.ManagerId);
            itemId = await db.InspectionRunAssets.Where(i => i.InspectionRun!.InspectionOrderId == order.Id).Select(i => i.Id).FirstAsync();
            workOrderId = await svc.UpdateInspectionItemAsync(itemId, "Defective", null, null, "broken", seed.UserId);
        }

        Assert.NotNull(workOrderId);
        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal(workOrderId, await verify.InspectionRunAssets.Where(i => i.Id == itemId).Select(i => i.WorkOrderId).FirstAsync());
        Assert.Equal("Draft", await verify.WorkOrders.Where(w => w.Id == workOrderId).Select(w => w.Stage).FirstAsync());
        Assert.Equal("Defective", await verify.Assets.Where(a => a.Id == seed.AssetId).Select(a => a.Status).FirstAsync());
    }

    /// <summary>A cancelled order must leave no orphaned Work Order behind — the two used to be
    /// separate commits, so the Work Order survived the rejected item update.</summary>
    [Fact]
    public async Task UpdateInspectionItemAsync_OnCancelledOrder_CreatesNoWorkOrder()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (_, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 5, isDirectFix: false);

        using var db = OrderTestDb.CreateContext(cs);
        var svc = OrderTestDb.CreateInspectionOrderService(db);
        var order = await svc.CreateAsync(typeId, null, seed.UserId, null, [seed.AssetId], null, seed.ManagerId);
        var itemId = await db.InspectionRunAssets.Where(i => i.InspectionRun!.InspectionOrderId == order.Id).Select(i => i.Id).FirstAsync();
        await svc.CancelAsync(order.Id, "no longer needed", seed.ManagerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpdateInspectionItemAsync(itemId, "Defective", null, null, null, seed.UserId));

        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal(0, await verify.WorkOrders.CountAsync());
        Assert.NotEqual("Defective", await verify.Assets.Where(a => a.Id == seed.AssetId).Select(a => a.Status).FirstAsync());
    }

    // ---------- Finding 6: a re-submitted fix returns the superseded parts to stock ----------

    [Fact]
    public async Task MaintenanceComplete_ResubmittedAfterReturnToOpen_RestoresPreviousPartsStock()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (partId, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 10, requiresApproval: true);

        // A fresh DbContext per step, the way each HTTP request gets its own.
        int orderId;
        using (var db = OrderTestDb.CreateContext(cs))
            orderId = (await OrderTestDb.CreateMaintenanceOrderService(db)
                .CreateAsync(seed.AssetId, seed.UserId, null, null, null, seed.ManagerId, typeId)).Id;

        using (var db = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateMaintenanceOrderService(db).CompleteAsync(orderId, null, [(partId, 3)], seed.UserId);
        Assert.Equal(7, await StockAsync(cs, partId));

        // Manager sends it back: the three units return to stock along with the cleared parts list.
        using (var db = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateMaintenanceOrderService(db).RejectApprovalAsync(orderId, "wrong parts", seed.ManagerId);
        Assert.Equal(10, await StockAsync(cs, partId));

        // Second submission consumes one unit - not four.
        using (var db = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateMaintenanceOrderService(db).CompleteAsync(orderId, null, [(partId, 1)], seed.UserId);

        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal(9, await verify.SpareParts.Where(p => p.Id == partId).Select(p => p.StockQuantity).FirstAsync());
        Assert.Equal(1, await verify.MaintenanceOrderParts.CountAsync(p => p.MaintenanceOrderId == orderId));
    }

    [Fact]
    public async Task WorkOrderVendorFix_ResubmittedAfterResend_RestoresPreviousPartsStock()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (partId, _) = await SeedPartAndTypeAsync(cs, seed.AssetId, 10);

        int workOrderId;
        using (var db = OrderTestDb.CreateContext(cs))
        {
            var svc = OrderTestDb.CreateWorkOrderService(db);
            var wo = await svc.CreateAsync(new WorkOrder { AssetId = seed.AssetId, AssignedToUserId = seed.UserId }, seed.ManagerId);
            workOrderId = wo.Id;
            await svc.SendToVendorAsync(workOrderId, 1, seed.ManagerId);
        }
        using (var db = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateWorkOrderService(db).VendorFixAsync(workOrderId, null, [(partId, 4)], seed.UserId);
        Assert.Equal(6, await StockAsync(cs, partId));

        // A rejected fix goes back to the vendor, who re-submits with a corrected parts list.
        using (var db = OrderTestDb.CreateContext(cs))
            await db.WorkOrders.Where(w => w.Id == workOrderId).ExecuteUpdateAsync(s => s.SetProperty(w => w.Stage, "Sent to Vendor"));
        using (var db = OrderTestDb.CreateContext(cs))
            await OrderTestDb.CreateWorkOrderService(db).VendorFixAsync(workOrderId, null, [(partId, 1)], seed.UserId);

        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal(9, await verify.SpareParts.Where(p => p.Id == partId).Select(p => p.StockQuantity).FirstAsync());
        Assert.Equal(1, await verify.WorkOrderParts.CountAsync(p => p.WorkOrderId == workOrderId));
    }

    // ---------- Finding 7: PendingApproval is no longer a dead end ----------

    [Fact]
    public async Task MaintenanceRejectApprovalAsync_ReturnsOrderToOpenAndClearsCost()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (partId, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 10, requiresApproval: true);

        using var db = OrderTestDb.CreateContext(cs);
        var svc = OrderTestDb.CreateMaintenanceOrderService(db);
        var order = await svc.CreateAsync(seed.AssetId, seed.UserId, null, null, null, seed.ManagerId, typeId);
        await svc.CompleteAsync(order.Id, DateTime.UtcNow, [(partId, 2)], seed.UserId);

        await svc.RejectApprovalAsync(order.Id, "redo it", seed.ManagerId);

        using var verify = OrderTestDb.CreateContext(cs);
        var reopened = await verify.MaintenanceOrders.FirstAsync(m => m.Id == order.Id);
        Assert.Equal("Open", reopened.Status);
        Assert.Null(reopened.Cost);
        Assert.Null(reopened.CompletedDate);
        Assert.Null(reopened.ClosedDate);
        Assert.Equal(0, await verify.MaintenanceOrderParts.CountAsync(p => p.MaintenanceOrderId == order.Id));
        Assert.Equal(10, await verify.SpareParts.Where(p => p.Id == partId).Select(p => p.StockQuantity).FirstAsync());
    }

    [Fact]
    public async Task MaintenanceCancelAsync_FromPendingApproval_IsAllowed()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);
        var (_, typeId) = await SeedPartAndTypeAsync(cs, seed.AssetId, 5, requiresApproval: true);

        using var db = OrderTestDb.CreateContext(cs);
        var svc = OrderTestDb.CreateMaintenanceOrderService(db);
        var order = await svc.CreateAsync(seed.AssetId, seed.UserId, null, null, null, seed.ManagerId, typeId);
        await svc.CompleteAsync(order.Id, null, [], seed.UserId);

        await svc.CancelAsync(order.Id, "not needed", seed.ManagerId);

        using var verify = OrderTestDb.CreateContext(cs);
        Assert.Equal("Cancelled", await verify.MaintenanceOrders.Where(m => m.Id == order.Id).Select(m => m.Status).FirstAsync());
    }

    // ---------- Finding 11: one atomic order-number sequence ----------

    [Fact]
    public async Task OrderNumberGenerator_IssuesDistinctSequentialNumbers()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        await OrderTestDb.SeedAsync(cs);

        using var db = OrderTestDb.CreateContext(cs);
        var first = await OrderNumberGenerator.NextAsync(db, "WO", 2026);
        var second = await OrderNumberGenerator.NextAsync(db, "WO", 2026);
        var otherPrefix = await OrderNumberGenerator.NextAsync(db, "INS", 2026);

        Assert.Equal("WO-2026-0001", first);
        Assert.Equal("WO-2026-0002", second);
        Assert.Equal("INS-2026-0001", otherPrefix);
    }

    /// <summary>A gap left by a hard-deleted row used to make the old COUNT-based helper recompute
    /// an already-used number forever; the sequence seeds past whatever already exists.</summary>
    [Fact]
    public async Task OrderNumberGenerator_SeedsPastExistingNumbers()
    {
        var (cs, keepAlive) = OrderTestDb.CreateSharedMemoryDb();
        using var _ = keepAlive;
        var seed = await OrderTestDb.SeedAsync(cs);

        using var db = OrderTestDb.CreateContext(cs);
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNumber = "WO-2026-0042", AssetId = seed.AssetId, Stage = "New",
            CreatedByUserId = seed.UserId, CreatedDate = new DateTime(2026, 1, 1),
        });
        await db.SaveChangesAsync();

        Assert.Equal("WO-2026-0043", await OrderNumberGenerator.NextAsync(db, "WO", 2026));
    }
}
