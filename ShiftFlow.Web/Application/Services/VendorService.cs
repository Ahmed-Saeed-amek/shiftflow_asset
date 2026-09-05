using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

public class VendorService : IVendorService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    public VendorService(ApplicationDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    public async Task<Vendor> CreateAsync(Vendor vendor, string userId)
    {
        vendor.CreatedDate = DateTime.UtcNow;
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Vendor", vendor.Id.ToString(), userId, newValue: vendor.Name);
        return vendor;
    }

    // Every editable field is saved, but the audit entry used to hard-code Status alone as
    // old/new — an edit that changed ContactName/Phone/Email/Specialization/Name without also
    // touching Status produced a byte-identical, useless audit row (same defect class as
    // AssetService.UpdateAsync, fixed separately).
    private static string Snapshot(Vendor v) =>
        $"{v.Name}, Contact: {v.ContactName ?? "—"}, Phone: {v.Phone ?? "—"}, Email: {v.Email ?? "—"}, Specialization: {v.Specialization ?? "—"}, Status: {v.Status}";

    public async Task UpdateAsync(Vendor vendor, string userId)
    {
        var existing = await _db.Vendors.FindAsync(vendor.Id) ?? throw new InvalidOperationException("Vendor not found.");
        var oldValue = Snapshot(existing);
        existing.Name = vendor.Name; existing.NameAr = vendor.NameAr; existing.ContactName = vendor.ContactName;
        existing.Phone = vendor.Phone; existing.Email = vendor.Email; existing.Specialization = vendor.Specialization;
        existing.Status = vendor.Status;
        var newValue = Snapshot(existing);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Vendor", existing.Id.ToString(), userId, oldValue: oldValue, newValue: newValue);
    }
}
