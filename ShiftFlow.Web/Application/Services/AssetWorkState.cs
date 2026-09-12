using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>The single "does this asset still have open work?" check, shared by every place that
/// restores Asset.Status to Working (WorkOrder Reject/ConfirmFix/ForceClose, MaintenanceOrder
/// Complete/Cancel/RejectApproval) — a second, unrelated issue on the same asset must not get
/// silently cleared just because a different order closed.</summary>
public static class AssetWorkState
{
    public static Task<bool> HasOtherOpenWorkAsync(ApplicationDbContext db, int assetId,
        int? excludeWorkOrderId = null, int? excludeMaintenanceOrderId = null) =>
        HasOpenAsync(db, assetId, excludeWorkOrderId, excludeMaintenanceOrderId);

    private static async Task<bool> HasOpenAsync(ApplicationDbContext db, int assetId,
        int? excludeWorkOrderId, int? excludeMaintenanceOrderId)
    {
        var openWorkOrder = await db.WorkOrders.AnyAsync(w => w.AssetId == assetId
            && (excludeWorkOrderId == null || w.Id != excludeWorkOrderId)
            && WorkOrderStages.Open.Contains(w.Stage));
        if (openWorkOrder) return true;
        return await db.MaintenanceOrders.AnyAsync(m => m.AssetId == assetId
            && (excludeMaintenanceOrderId == null || m.Id != excludeMaintenanceOrderId)
            && (m.Status == OrderStatuses.Open || m.Status == OrderStatuses.PendingApproval));
    }
}
