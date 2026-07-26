using Microsoft.AspNetCore.Http;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Web.Services;

/// <summary>
/// Saves a vendor's Fix-report attachments to App_Data/uploads/workorders/{workOrderId}/ — outside
/// wwwroot, same pattern as ReportAttachmentStorage — and records a WorkOrderAttachment row per
/// accepted file. Rejected files are reported back to the caller instead of silently dropped.
/// </summary>
public static class WorkOrderAttachmentStorage
{
    public static async Task<List<(string FileName, string Reason)>> SaveAsync(
        ApplicationDbContext db, int workOrderId, IEnumerable<IFormFile>? files, string uploaderId)
    {
        if (files is null) return [];

        var (accepted, rejected) = await FileUploadValidator.ValidateAllAsync(files);
        if (accepted.Count == 0) return rejected;

        var rel = $"/uploads/workorders/{workOrderId}";
        var abs = Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads", "workorders", workOrderId.ToString());
        Directory.CreateDirectory(abs);

        foreach (var f in accepted)
        {
            var ext = Path.GetExtension(f.FileName);
            var stored = Guid.NewGuid() + ext;
            using (var fs = new FileStream(Path.Combine(abs, stored), FileMode.Create))
                await f.CopyToAsync(fs);

            db.WorkOrderAttachments.Add(new WorkOrderAttachment
            {
                WorkOrderId      = workOrderId,
                FileName         = Path.GetFileName(f.FileName),
                FilePath         = $"{rel}/{stored}",
                FileType         = FileUploadValidator.SafeMimeType(ext),
                FileSize         = f.Length,
                UploadedByUserId = uploaderId,
            });
        }

        await db.SaveChangesAsync();
        return rejected;
    }

    public static string ResolvePhysicalPath(string relativeFilePath)
    {
        var trimmed = relativeFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(AppContext.BaseDirectory, "App_Data", trimmed);
    }
}
