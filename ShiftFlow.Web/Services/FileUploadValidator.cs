using Microsoft.AspNetCore.Http;

namespace ShiftFlow.Web.Services;

public record FileValidationResult(IFormFile File, bool IsAllowed, string? RejectionReason);

/// <summary>
/// Validates uploaded files against an extension allowlist and a magic-byte signature check,
/// so a renamed executable (or any other spoofed file type) can't pass as an allowed document/image
/// just because its extension or client-supplied Content-Type claims otherwise.
/// </summary>
public static class FileUploadValidator
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB per file
    public const long MaxTotalRequestBytes = 50 * 1024 * 1024; // 50 MB per request

    private static readonly Dictionary<string, string> AllowedExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = "application/pdf",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"]  = "image/png",
        [".doc"]  = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"]  = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public static IReadOnlyCollection<string> AllowedExtensions => AllowedExtensionToMime.Keys;

    // Signature checks are prefix-based; PK-header formats (docx/xlsx) share the zip signature
    // with plain .zip, but that's fine here since only zip-based Office extensions are allowlisted.
    private static readonly (byte[] Signature, string[] Extensions)[] SignaturesByExtension =
    [
        (new byte[] { 0x25, 0x50, 0x44, 0x46 }, new[] { ".pdf" }),                 // %PDF
        (new byte[] { 0xFF, 0xD8, 0xFF }, new[] { ".jpg", ".jpeg" }),              // JPEG
        (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, new[] { ".png" }), // PNG
        (new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, new[] { ".doc", ".xls" }), // legacy OLE
        (new byte[] { 0x50, 0x4B, 0x03, 0x04 }, new[] { ".docx", ".xlsx" }),       // PK zip (OOXML)
    ];

    public static string SafeMimeType(string extension) =>
        AllowedExtensionToMime.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";

    public static async Task<FileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file.Length <= 0)
            return new FileValidationResult(file, false, "Empty file.");

        if (file.Length > MaxFileSizeBytes)
            return new FileValidationResult(file, false, $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)} MB limit.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensionToMime.ContainsKey(ext))
            return new FileValidationResult(file, false, $"File type \"{ext}\" is not allowed.");

        var matchingSignature = SignaturesByExtension.FirstOrDefault(s => s.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
        if (matchingSignature.Signature is null)
            return new FileValidationResult(file, false, $"File type \"{ext}\" is not allowed.");

        var header = new byte[matchingSignature.Signature.Length];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);
            if (read < header.Length || !header.SequenceEqual(matchingSignature.Signature))
                return new FileValidationResult(file, false, $"\"{file.FileName}\" doesn't match the expected format for {ext} — it may be renamed or corrupted.");
        }

        return new FileValidationResult(file, true, null);
    }

    public static async Task<(List<IFormFile> Accepted, List<(string FileName, string Reason)> Rejected)> ValidateAllAsync(
        IEnumerable<IFormFile> files, CancellationToken ct = default)
    {
        var accepted = new List<IFormFile>();
        var rejected = new List<(string, string)>();

        foreach (var file in files)
        {
            var result = await ValidateAsync(file, ct);
            if (result.IsAllowed) accepted.Add(file);
            else rejected.Add((file.FileName, result.RejectionReason ?? "Rejected."));
        }

        return (accepted, rejected);
    }
}
