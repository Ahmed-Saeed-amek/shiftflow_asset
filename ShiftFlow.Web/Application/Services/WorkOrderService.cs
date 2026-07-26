using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class WorkOrderService : IWorkOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    public WorkOrderService(ApplicationDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    public async Task<WorkOrder> CreateAsync(WorkOrder workOrder, string userId)
    {
        var year = DateTime.UtcNow.Year;
        var seq = await _db.WorkOrders.CountAsync(w => w.CreatedDate.Year == year) + 1;
        workOrder.WorkOrderNumber = $"WO-{year}-{seq:D4}";
        workOrder.Stage = "New";
        workOrder.CreatedByUserId = userId;
        workOrder.CreatedDate = DateTime.UtcNow;
        workOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "New", ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });
        _db.WorkOrders.Add(workOrder);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "WorkOrder", workOrder.Id.ToString(), userId, newValue: workOrder.WorkOrderNumber);
        return workOrder;
    }

    public async Task<WorkOrder> ReportAsync(WorkOrder workOrder, string userId)
    {
        var year = DateTime.UtcNow.Year;
        var seq = await _db.WorkOrders.CountAsync(w => w.CreatedDate.Year == year) + 1;
        workOrder.WorkOrderNumber = $"WO-{year}-{seq:D4}";
        workOrder.Stage = "Draft";
        workOrder.CreatedByUserId = userId;
        workOrder.CreatedDate = DateTime.UtcNow;
        workOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "Draft", ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });
        _db.WorkOrders.Add(workOrder);
        await SetAssetStatusAsync(workOrder.AssetId, "Defective");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Report", "WorkOrder", workOrder.Id.ToString(), userId, newValue: workOrder.WorkOrderNumber);
        return workOrder;
    }

    /// <summary>Keeps Asset.Status in sync with the work order lifecycle so nobody has to flip it by
    /// hand: reporting a defect marks the asset Defective, sending it to a vendor marks it under
    /// Maintenance, and closing the fix returns it to Working (unless another work order on the same
    /// asset is still open, or the asset has been Retired — that status is never overridden).</summary>
    private static readonly string[] OpenStages = ["Draft", "New", "Sent to Vendor", "Blocked", "Fixed - Pending Confirmation"];

    private async Task SetAssetStatusAsync(int assetId, string status)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset != null && asset.Status != "Retired") asset.Status = status;
    }

    private async Task<int> ResolveServiceVendorAsync(int assetId, int vendorId)
    {
        var candidates = await _db.ContractAssets
            .Where(ca => ca.AssetId == assetId && ca.Contract!.ContractType == "Service"
                && (ca.Contract.EndDate == null || ca.Contract.EndDate >= DateTime.UtcNow.Date))
            .Select(ca => ca.Contract!.VendorId).Distinct().ToListAsync();
        if (candidates.Count == 0)
            throw new InvalidOperationException("This asset has no active Service contract — link one before sending a work order to a vendor.");
        if (!candidates.Contains(vendorId))
            throw new InvalidOperationException("The selected vendor doesn't have an active Service contract on this asset.");
        return vendorId;
    }

    private void AddStageEvent(WorkOrder wo, string stage, string userId) =>
        _db.WorkOrderStageEvents.Add(new WorkOrderStageEvent { WorkOrderId = wo.Id, Stage = stage, ChangedAt = DateTime.UtcNow, ChangedByUserId = userId });

    public async Task AcceptAsync(int workOrderId, int vendorId, string priority, string? description, string? notes, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Draft") throw new InvalidOperationException("Only a Draft report can be accepted.");
        await ResolveServiceVendorAsync(wo.AssetId, vendorId);

        wo.Priority = priority; wo.Description = description; wo.Notes = notes;
        wo.VendorId = vendorId; wo.Stage = "Sent to Vendor";
        AddStageEvent(wo, "Sent to Vendor", userId);
        await SetAssetStatusAsync(wo.AssetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Accept", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Draft", newValue: "Sent to Vendor");
    }

    public async Task RejectAsync(int workOrderId, string? reason, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Draft") throw new InvalidOperationException("Only a Draft report can be rejected.");
        wo.Stage = "Rejected";
        wo.Notes = string.IsNullOrWhiteSpace(reason) ? wo.Notes : $"{wo.Notes}\n\nRejected: {reason}".Trim();
        AddStageEvent(wo, "Rejected", userId);
        await SetAssetStatusAsync(wo.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reject", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Draft", newValue: "Rejected", details: reason);
    }

    public async Task SendToVendorAsync(int workOrderId, int vendorId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "New") throw new InvalidOperationException("Only a New work order can be sent to a vendor.");
        await ResolveServiceVendorAsync(wo.AssetId, vendorId);

        wo.VendorId = vendorId; wo.Stage = "Sent to Vendor";
        AddStageEvent(wo, "Sent to Vendor", userId);
        await SetAssetStatusAsync(wo.AssetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SendToVendor", "WorkOrder", wo.Id.ToString(), userId, oldValue: "New", newValue: "Sent to Vendor");
    }

    public async Task VendorFixAsync(int workOrderId, string description, decimal? cost, DateTime? completionDate, List<(string Name, int Quantity)> parts, string vendorUserId)
    {
        var wo = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Sent to Vendor") throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        wo.FixDescription = description; wo.FixCost = cost; wo.FixCompletionDate = completionDate;
        _db.WorkOrderParts.RemoveRange(wo.Parts);
        foreach (var p in parts.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            _db.WorkOrderParts.Add(new WorkOrderPart { WorkOrderId = wo.Id, Name = p.Name, Quantity = p.Quantity });

        wo.Stage = "Fixed - Pending Confirmation";
        AddStageEvent(wo, "Fixed - Pending Confirmation", vendorUserId);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VendorFix", "WorkOrder", wo.Id.ToString(), vendorUserId, oldValue: "Sent to Vendor", newValue: "Fixed - Pending Confirmation");
    }

    public async Task VendorBlockAsync(int workOrderId, int blockReasonId, string? detail, string vendorUserId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Sent to Vendor") throw new InvalidOperationException("This work order isn't awaiting a vendor response.");

        wo.BlockReasonId = blockReasonId; wo.BlockDetail = detail;
        wo.Stage = "Blocked";
        AddStageEvent(wo, "Blocked", vendorUserId);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VendorBlock", "WorkOrder", wo.Id.ToString(), vendorUserId, oldValue: "Sent to Vendor", newValue: "Blocked", details: detail);
    }

    public async Task ResendToVendorAsync(int workOrderId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Blocked") throw new InvalidOperationException("Only a Blocked work order can be resent.");
        if (wo.VendorId == null) throw new InvalidOperationException("This work order has no vendor to resend to.");

        wo.BlockReasonId = null; wo.BlockDetail = null;
        wo.Stage = "Sent to Vendor";
        AddStageEvent(wo, "Sent to Vendor", userId);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Resend", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Blocked", newValue: "Sent to Vendor");
    }

    public async Task ConfirmFixAsync(int workOrderId, string userId)
    {
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage != "Fixed - Pending Confirmation") throw new InvalidOperationException("Only a fix pending confirmation can be confirmed.");

        wo.Stage = "Closed"; wo.ClosedDate = DateTime.UtcNow;
        AddStageEvent(wo, "Closed", userId);
        // Only restore Working if no other work order on this asset is still open — a second,
        // unrelated defect shouldn't get silently cleared just because a different one closed.
        var hasOtherOpenWorkOrders = await _db.WorkOrders.AnyAsync(w => w.AssetId == wo.AssetId && w.Id != wo.Id && OpenStages.Contains(w.Stage));
        if (!hasOtherOpenWorkOrders) await SetAssetStatusAsync(wo.AssetId, "Working");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ConfirmFix", "WorkOrder", wo.Id.ToString(), userId, oldValue: "Fixed - Pending Confirmation", newValue: "Closed");
    }

    public async Task UpdatePriorityAsync(int workOrderId, string priority, string userId)
    {
        if (!WorkOrder.Priorities.Contains(priority)) throw new InvalidOperationException("Invalid priority.");
        var wo = await _db.WorkOrders.FindAsync(workOrderId) ?? throw new InvalidOperationException("Work order not found.");
        if (wo.Stage is not ("Draft" or "New")) throw new InvalidOperationException("Priority can only be changed before a work order is sent to a vendor.");
        if (wo.Priority == priority) return;
        var oldPriority = wo.Priority;
        wo.Priority = priority;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("UpdatePriority", "WorkOrder", wo.Id.ToString(), userId, oldValue: oldPriority, newValue: priority);
    }

    public async Task<WorkOrder> CreatePreventiveMaintenanceOccurrenceAsync(int assetId, int vendorId, int sourceContractId, DateTime scheduledDate, string? contractNumber, string systemUserId)
    {
        var year = DateTime.UtcNow.Year;
        var seq = await _db.WorkOrders.CountAsync(w => w.CreatedDate.Year == year) + 1;
        var contractLabel = string.IsNullOrWhiteSpace(contractNumber) ? sourceContractId.ToString() : contractNumber;
        var wo = new WorkOrder
        {
            WorkOrderNumber = $"WO-{year}-{seq:D4}",
            AssetId = assetId,
            VendorId = vendorId,
            SourceContractId = sourceContractId,
            ScheduledDate = scheduledDate.Date,
            Stage = "Sent to Vendor",
            Priority = "Medium",
            Description = $"Preventive Maintenance — due {scheduledDate:yyyy-MM-dd} (Contract {contractLabel})",
            CreatedByUserId = systemUserId,
            CreatedDate = DateTime.UtcNow,
        };
        wo.StageEvents.Add(new WorkOrderStageEvent { Stage = "Sent to Vendor", ChangedAt = DateTime.UtcNow, ChangedByUserId = systemUserId });
        _db.WorkOrders.Add(wo);
        await SetAssetStatusAsync(assetId, "Maintenance");
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AutoGeneratePM", "WorkOrder", wo.Id.ToString(), systemUserId,
            newValue: wo.WorkOrderNumber, details: $"Contract #{sourceContractId}, due {scheduledDate:yyyy-MM-dd}");
        return wo;
    }

    private async Task<List<WorkOrder>> GetExportRowsAsync() =>
        await _db.WorkOrders.Include(w => w.Asset).Include(w => w.Vendor).OrderByDescending(w => w.CreatedDate).ToListAsync();

    public async Task<byte[]> ExportToExcelAsync()
    {
        var orders = await GetExportRowsAsync();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Work Orders");
        string[] headers = ["Work Order #", "Asset", "Priority", "Stage", "Vendor", "Created", "Closed"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        using (var range = ws.Cells[1, 1, 1, headers.Length]) { range.Style.Font.Bold = true; }

        var row = 2;
        foreach (var w in orders)
        {
            ws.Cells[row, 1].Value = w.WorkOrderNumber;
            ws.Cells[row, 2].Value = w.Asset?.AssetTag;
            ws.Cells[row, 3].Value = w.Priority;
            ws.Cells[row, 4].Value = w.Stage;
            ws.Cells[row, 5].Value = w.Vendor?.Name;
            ws.Cells[row, 6].Value = w.CreatedDate.ToString("yyyy-MM-dd");
            ws.Cells[row, 7].Value = w.ClosedDate?.ToString("yyyy-MM-dd");
            row++;
        }
        ws.Cells.AutoFitColumns();
        return await pkg.GetAsByteArrayAsync();
    }

    public async Task<byte[]> ExportToPdfAsync()
    {
        var orders = await GetExportRowsAsync();
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdf = new PdfDocument(writer))
        {
            var doc = new Document(pdf);
            doc.Add(new Paragraph("Work Orders").SetBold().SetFontSize(16));
            var table = new Table(7, true).UseAllAvailableWidth();
            foreach (var h in new[] { "Work Order #", "Asset", "Priority", "Stage", "Vendor", "Created", "Closed" })
                table.AddHeaderCell(h);
            foreach (var w in orders)
            {
                table.AddCell(w.WorkOrderNumber);
                table.AddCell(w.Asset?.AssetTag ?? "");
                table.AddCell(w.Priority);
                table.AddCell(w.Stage);
                table.AddCell(w.Vendor?.Name ?? "");
                table.AddCell(w.CreatedDate.ToString("yyyy-MM-dd"));
                table.AddCell(w.ClosedDate?.ToString("yyyy-MM-dd") ?? "");
            }
            doc.Add(table);
        }
        return ms.ToArray();
    }
}
