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

    public async Task UpdateAsync(Vendor vendor, string userId)
    {
        var existing = await _db.Vendors.FindAsync(vendor.Id) ?? throw new InvalidOperationException("Vendor not found.");
        var oldStatus = existing.Status;
        existing.Name = vendor.Name; existing.NameAr = vendor.NameAr; existing.ContactName = vendor.ContactName;
        existing.Phone = vendor.Phone; existing.Email = vendor.Email; existing.Specialization = vendor.Specialization;
        existing.Status = vendor.Status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Vendor", existing.Id.ToString(), userId, oldValue: oldStatus, newValue: existing.Status);
    }
}
