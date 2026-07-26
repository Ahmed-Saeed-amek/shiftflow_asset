using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;

namespace ShiftFlow.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // ── Legacy / Support ─────────────────────────────────────────────────────
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // ── Inspection Orders / Teams ────────────────────────────────────────────
    public DbSet<InspectionOrder> InspectionOrders => Set<InspectionOrder>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<InspectionRun> InspectionRuns => Set<InspectionRun>();
    public DbSet<InspectionRunAsset> InspectionRunAssets => Set<InspectionRunAsset>();

    // -- RBAC
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    // ── Asset Management ─────────────────────────────────────────────────────
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderStageEvent> WorkOrderStageEvents => Set<WorkOrderStageEvent>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractAsset> ContractAssets => Set<ContractAsset>();
    public DbSet<UserAssetScope> UserAssetScopes => Set<UserAssetScope>();
    public DbSet<AssetActionType> AssetActionTypes => Set<AssetActionType>();
    public DbSet<AssetActionCause> AssetActionCauses => Set<AssetActionCause>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<WorkOrderBlockReason> WorkOrderBlockReasons => Set<WorkOrderBlockReason>();
    public DbSet<WorkOrderAttachment> WorkOrderAttachments => Set<WorkOrderAttachment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ── ApplicationRole ──────────────────────────────────────────────────
        b.Entity<ApplicationRole>(e =>
        {
            e.Property(r => r.NameAr).HasMaxLength(100);
        });

        // ── ApplicationUser ──────────────────────────────────────────────────
        b.Entity<ApplicationUser>(e =>
        {
            e.HasOne(u => u.Location).WithMany().HasForeignKey(u => u.LocationId).OnDelete(DeleteBehavior.SetNull);
            e.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            e.HasIndex(u => u.EmployeeNumber).IsUnique().HasFilter("[EmployeeNumber] IS NOT NULL");
        });

        b.Entity<AuditLog>(e =>
        {
            // Perf index: paginated audit log per user
            e.HasIndex(al => new { al.UserId, al.CreatedDate });
        });
        b.Entity<AuditLog>(e =>
            e.HasOne(a => a.User).WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction));

        // ── Team / TeamMember ────────────────────────────────────────────────
        b.Entity<Team>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.Property(t => t.NameAr).HasMaxLength(200);
            e.HasOne(t => t.CreatedByUser).WithMany()
                .HasForeignKey(t => t.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TeamMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();
            e.HasOne(m => m.Team).WithMany(t => t.Members)
                .HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.TeamMemberships)
                .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── InspectionOrder ──────────────────────────────────────────────────
        b.Entity<InspectionOrder>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.OrderNumber).HasMaxLength(30).IsRequired();
            e.Property(o => o.Title).HasMaxLength(300).IsRequired();
            e.Property(o => o.Status).HasMaxLength(20).IsRequired();
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.HasIndex(o => o.Status);
            e.HasOne(o => o.AssignedToUser).WithMany(u => u.AssignedInspectionOrders)
                .HasForeignKey(o => o.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.AssignedToTeam).WithMany()
                .HasForeignKey(o => o.AssignedToTeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.CreatedByUser).WithMany()
                .HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            // Exactly one of AssignedToUserId/AssignedToTeamId must be set.
            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_InspectionOrder_ExactlyOneAssignee",
                "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)"));
        });

        // ── InspectionRun / InspectionRunAsset ──────────────────────────────
        b.Entity<InspectionRun>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasOne(r => r.InspectionOrder).WithOne(o => o.InspectionRun)
                .HasForeignKey<InspectionRun>(r => r.InspectionOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Zone).WithMany()
                .HasForeignKey(r => r.ZoneId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => r.InspectionOrderId).IsUnique();
        });
        b.Entity<InspectionRunAsset>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Outcome).HasMaxLength(20).IsRequired();
            e.HasOne(i => i.InspectionRun).WithMany(r => r.Items)
                .HasForeignKey(i => i.InspectionRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Asset).WithMany()
                .HasForeignKey(i => i.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.InspectedByUser).WithMany()
                .HasForeignKey(i => i.InspectedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.WorkOrder).WithMany()
                .HasForeignKey(i => i.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.InspectionRunId);
        });

        // ── RBAC ──────────────────────────────────────────────────────────────
        b.Entity<Permission>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.Description).HasMaxLength(500);
            e.Property(p => p.Category).HasMaxLength(100);
        });
        b.Entity<RolePermission>(e =>
        {
            e.HasKey(rp => new { rp.RoleId, rp.PermissionName });
            e.Property(rp => rp.PermissionName).HasMaxLength(100).IsRequired();
            e.HasOne<ApplicationRole>().WithMany()
                .HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UserPermission>(e =>
        {
            e.HasKey(up => new { up.UserId, up.PermissionName });
            e.Property(up => up.PermissionName).HasMaxLength(100).IsRequired();
            e.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(up => up.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Asset Management ─────────────────────────────────────────────────
        b.Entity<AssetCategory>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(c => new { c.ParentCategoryId, c.Name }).IsUnique();
            e.HasOne(c => c.ParentCategory).WithMany(c => c.Subcategories)
                .HasForeignKey(c => c.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        // ── Asset Location hierarchy (Governorate → Area → Block → Zone) ────────
        b.Entity<Governorate>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(g => g.Name).IsUnique();
        });
        b.Entity<Area>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(a => new { a.GovernorateId, a.Name }).IsUnique();
            e.HasOne(a => a.Governorate).WithMany(g => g.Areas)
                .HasForeignKey(a => a.GovernorateId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Block>(e =>
        {
            e.HasKey(bl => bl.Id);
            e.HasIndex(bl => new { bl.AreaId, bl.Number }).IsUnique();
            e.HasOne(bl => bl.Area).WithMany(a => a.Blocks)
                .HasForeignKey(bl => bl.AreaId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Zone>(e =>
        {
            e.HasKey(z => z.Id);
            e.Property(z => z.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(z => new { z.BlockId, z.Name }).IsUnique();
            e.HasOne(z => z.Block).WithMany(bl => bl.Zones)
                .HasForeignKey(z => z.BlockId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Asset>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.AssetTag).HasMaxLength(50).IsRequired();
            e.Property(a => a.Name).HasMaxLength(200).IsRequired();
            e.Property(a => a.Status).HasMaxLength(20).IsRequired();
            e.Property(a => a.Sku).HasMaxLength(100);
            e.HasIndex(a => a.AssetTag).IsUnique();
            e.HasIndex(a => a.Status);
            e.HasIndex(a => a.ZoneId);
            e.HasOne(a => a.Category).WithMany(c => c.Assets)
                .HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Zone).WithMany(z => z.Assets)
                .HasForeignKey(a => a.ZoneId).OnDelete(DeleteBehavior.Restrict);
            // Multiple FKs to AspNetUsers on this entity — Restrict/NoAction on all of
            // them to avoid SQL Server's "multiple cascade paths" error (see DEPLOYMENT.md).
            e.HasOne(a => a.AssignedToUser).WithMany()
                .HasForeignKey(a => a.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.CreatedByUser).WithMany()
                .HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.UpdatedByUser).WithMany()
                .HasForeignKey(a => a.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Vendor>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Name).HasMaxLength(200).IsRequired();
            e.Property(v => v.Status).HasMaxLength(20).IsRequired();
            e.HasIndex(v => v.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasOne(v => v.User).WithMany()
                .HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<WorkOrder>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.WorkOrderNumber).HasMaxLength(30).IsRequired();
            e.Property(w => w.Priority).HasMaxLength(20).IsRequired();
            e.Property(w => w.Stage).HasMaxLength(40).IsRequired();
            e.Property(w => w.FixCost).HasColumnType("decimal(12,2)");
            e.HasIndex(w => w.WorkOrderNumber).IsUnique();
            e.HasIndex(w => new { w.Stage, w.Priority });
            e.HasIndex(w => w.AssetId);
            e.HasOne(w => w.Asset).WithMany(a => a.WorkOrders)
                .HasForeignKey(w => w.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.Vendor).WithMany(v => v.WorkOrders)
                .HasForeignKey(w => w.VendorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(w => w.CreatedByUser).WithMany()
                .HasForeignKey(w => w.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.ActionType).WithMany()
                .HasForeignKey(w => w.ActionTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.Cause).WithMany()
                .HasForeignKey(w => w.CauseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.BlockReason).WithMany()
                .HasForeignKey(w => w.BlockReasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.SourceContract).WithMany()
                .HasForeignKey(w => w.SourceContractId).OnDelete(DeleteBehavior.Restrict);
            // Prevents the PM generator from ever double-creating the same (contract, asset, due date)
            // occurrence. Filtered because almost every existing WorkOrder has SourceContractId == NULL,
            // and SQL Server only allows one NULL per unqualified unique key.
            e.HasIndex(w => new { w.SourceContractId, w.AssetId, w.ScheduledDate })
                .IsUnique()
                .HasFilter("[SourceContractId] IS NOT NULL");
        });
        b.Entity<WorkOrderPart>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.HasOne(p => p.WorkOrder).WithMany(w => w.Parts)
                .HasForeignKey(p => p.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkOrderBlockReason>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(r => r.Name).IsUnique();
        });
        b.Entity<WorkOrderAttachment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.FileName).HasMaxLength(300).IsRequired();
            e.Property(a => a.FilePath).HasMaxLength(500).IsRequired();
            e.Property(a => a.FileType).HasMaxLength(150);
            e.HasOne(a => a.WorkOrder).WithMany(w => w.Attachments)
                .HasForeignKey(a => a.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.UploadedByUser).WithMany()
                .HasForeignKey(a => a.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AssetActionType>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(a => new { a.CategoryId, a.Name }).IsUnique();
            e.HasOne(a => a.Category).WithMany(c => c.ActionTypes)
                .HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AssetActionCause>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(c => new { c.ActionTypeId, c.Name }).IsUnique();
            e.HasOne(c => c.ActionType).WithMany(a => a.Causes)
                .HasForeignKey(c => c.ActionTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Contract>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.ContractType).HasMaxLength(30).IsRequired();
            e.Property(c => c.PmCadence).HasMaxLength(20);
            e.Property(c => c.Cost).HasColumnType("decimal(12,2)");
            e.HasIndex(c => c.VendorId);
            e.HasOne(c => c.Vendor).WithMany()
                .HasForeignKey(c => c.VendorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ContractAsset>(e =>
        {
            e.HasKey(ca => ca.Id);
            e.HasIndex(ca => new { ca.ContractId, ca.AssetId }).IsUnique();
            e.HasOne(ca => ca.Contract).WithMany(c => c.AssetLinks)
                .HasForeignKey(ca => ca.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ca => ca.Asset).WithMany(a => a.ContractLinks)
                .HasForeignKey(ca => ca.AssetId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserAssetScope>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.ScopeType).HasMaxLength(20).IsRequired();
            e.HasIndex(s => s.UserId).IsUnique();
            e.HasOne(s => s.User).WithMany()
                .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkOrderStageEvent>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Stage).HasMaxLength(40).IsRequired();
            e.HasIndex(s => s.WorkOrderId);
            e.HasOne(s => s.WorkOrder).WithMany(w => w.StageEvents)
                .HasForeignKey(s => s.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.ChangedByUser).WithMany()
                .HasForeignKey(s => s.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
