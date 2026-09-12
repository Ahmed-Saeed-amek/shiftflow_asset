using Microsoft.AspNetCore.Http;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Web.Services;

/// <summary>
/// Saves a contract's attachments (signed PDF, scanned certificate, etc.) to
/// App_Data/uploads/contracts/{contractId}/ — outside wwwroot, same pattern as
/// WorkOrderAttachmentStorage — and records a ContractAttachment row per accepted file.
/// Rejected files are reported back to the caller instead of silently dropped.
/// </summary>
public static class ContractAttachmentStorage
{
    public static async Task<List<(string FileName, string Reason)>> SaveAsync(
        ApplicationDbContext db, int contractId, IEnumerable<IFormFile>? files, string uploaderId)
    {
        if (files is null) return [];

        var (accepted, rejected) = await FileUploadValidator.ValidateAllAsync(files);
        if (accepted.Count == 0) return rejected;

        var rel = $"/uploads/contracts/{contractId}";
        var abs = Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads", "contracts", contractId.ToString());
        Directory.CreateDirectory(abs);

        foreach (var f in accepted)
        {
            var ext = Path.GetExtension(f.FileName);
            var stored = Guid.NewGuid() + ext;
            using (var fs = new FileStream(Path.Combine(abs, stored), FileMode.Create))
                await f.CopyToAsync(fs);

            db.ContractAttachments.Add(new ContractAttachment
            {
                ContractId       = contractId,
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

    /// <summary>Maps a stored FilePath back to disk. Returns null if the result would escape the
    /// uploads root (a "../" in a tampered FilePath), so a caller can never serve or delete a file
    /// outside App_Data/uploads.</summary>
    public static string? ResolvePhysicalPath(string relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(relativeFilePath)) return null;

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads"))
                       .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var trimmed = relativeFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "App_Data", trimmed));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
