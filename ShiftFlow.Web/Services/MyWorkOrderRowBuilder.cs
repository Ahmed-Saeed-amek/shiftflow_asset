using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Services;

/// <summary>Builds the unified "my work" row list (Inspection + Maintenance + Work Orders) shared by
/// UsersController.MyOrders and MyHomeController.Index — extracted so both stay in sync instead of
/// each re-deriving which orders count as "assigned to me" (MyHome previously only looked at
/// Inspection Orders, silently ignoring Maintenance/Work Orders assigned to the same user).</summary>
public static class MyWorkOrderRowBuilder
{
    public static async Task<List<MyWorkOrderRow>> BuildAsync(ApplicationDbContext db, ILanguageService loc,
        string userId, bool showAll, DateTime? from = null, DateTime? to = null)
    {
        var myTeamIds = await db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync();

        var inspectionQuery = db.InspectionOrders.AsNoTracking()
            .Where(o => o.AssignedToUserId == userId || (o.AssignedToTeamId != null && myTeamIds.Contains(o.AssignedToTeamId.Value)));
        if (!showAll) inspectionQuery = inspectionQuery.Where(o => o.Status != "Done" && o.Status != "Cancelled");
        if (from.HasValue) inspectionQuery = inspectionQuery.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) inspectionQuery = inspectionQuery.Where(o => o.CreatedAt <= to.Value);
        var inspectionRowsRaw = await inspectionQuery
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.Status, o.DueDate, o.CreatedAt,
                Done = o.InspectionRun!.Items.Count(i => i.Outcome != "Pending"),
                Total = o.InspectionRun!.Items.Count,
            })
            .ToListAsync();
        var inspectionRows = inspectionRowsRaw.Select(o => new MyWorkOrderRow
        {
            Category = "Inspection", CategoryLabel = "Inspection", Id = o.Id, OrderNumber = o.OrderNumber,
            AssetLabel = $"{o.Done}/{o.Total} " + (o.Total == 1 ? loc.T("asset") : loc.T("assets")),
            Status = o.Status, DueDate = o.DueDate, CreatedAt = o.CreatedAt, DetailsController = "InspectionOrders",
        }).ToList();

        var maintenanceQuery = db.MaintenanceOrders.AsNoTracking().Include(m => m.Asset)
            .Where(m => m.AssignedToUserId == userId || (m.AssignedToTeamId != null && myTeamIds.Contains(m.AssignedToTeamId.Value)));
        if (!showAll) maintenanceQuery = maintenanceQuery.Where(m => m.Status == "Open");
        if (from.HasValue) maintenanceQuery = maintenanceQuery.Where(m => m.CreatedDate >= from.Value);
        if (to.HasValue) maintenanceQuery = maintenanceQuery.Where(m => m.CreatedDate <= to.Value);
        var maintenanceRows = await maintenanceQuery
            .Select(m => new MyWorkOrderRow
            {
                Category = "Maintenance", CategoryLabel = "Maintenance", Id = m.Id, OrderNumber = m.OrderNumber,
                AssetLabel = m.Asset!.AssetTag, Status = m.Status, DueDate = m.DueDate, CreatedAt = m.CreatedDate, DetailsController = "MaintenanceOrders",
            })
            .ToListAsync();

        var workOrderQuery = db.WorkOrders.AsNoTracking().Include(w => w.Asset).Where(w => w.AssignedToUserId == userId);
        if (!showAll) workOrderQuery = workOrderQuery.Where(w => w.Stage != "Closed");
        if (from.HasValue) workOrderQuery = workOrderQuery.Where(w => w.CreatedDate >= from.Value);
        if (to.HasValue) workOrderQuery = workOrderQuery.Where(w => w.CreatedDate <= to.Value);
        var workOrderRows = await workOrderQuery
            .Select(w => new MyWorkOrderRow
            {
                Category = "WorkOrder", CategoryLabel = "Work Order", Id = w.Id, OrderNumber = w.WorkOrderNumber,
                AssetLabel = w.Asset!.AssetTag, Status = w.Stage, DueDate = null, CreatedAt = w.CreatedDate, DetailsController = "WorkOrders",
            })
            .ToListAsync();

        return inspectionRows.Concat(maintenanceRows).Concat(workOrderRows)
            .OrderByDescending(r => r.CreatedAt)
            .Take(300)
            .ToList();
    }
}
