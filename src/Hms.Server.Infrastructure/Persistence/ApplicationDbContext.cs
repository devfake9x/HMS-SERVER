using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid? OrganizationId { get; set; }
    public long AuthorizationEpoch { get; set; }
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
}

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();
    public DbSet<DeviceAuthorizationEpochRecord> DeviceAuthorizationEpochs => Set<DeviceAuthorizationEpochRecord>();
    public DbSet<DeviceKeysetRecord> DeviceKeysets => Set<DeviceKeysetRecord>();
    public DbSet<PolicyVersionRecord> PolicyVersions => Set<PolicyVersionRecord>();
    public DbSet<ProjectRecord> Projects => Set<ProjectRecord>();
    public DbSet<PackageRecord> Packages => Set<PackageRecord>();
    public DbSet<AreaRecord> Areas => Set<AreaRecord>();
    public DbSet<CommandAuditRecord> CommandAudits => Set<CommandAuditRecord>();
    public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();
    public DbSet<DeviceRoleRecord> DeviceRoles => Set<DeviceRoleRecord>();
    public DbSet<DeviceRolePermissionRecord> DeviceRolePermissions => Set<DeviceRolePermissionRecord>();
    public DbSet<DeviceMembershipRecord> DeviceMemberships => Set<DeviceMembershipRecord>();
    public DbSet<DeviceAccessAuditRecord> DeviceAccessAudits => Set<DeviceAccessAuditRecord>();
    public DbSet<LicenseTrustRecord> LicenseTrustEntries => Set<LicenseTrustRecord>();
    public DbSet<LicenseTrustAuditRecord> LicenseTrustAudits => Set<LicenseTrustAuditRecord>();
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();
    public DbSet<LicenseApplyJournalRecord> LicenseApplyJournals => Set<LicenseApplyJournalRecord>();
    public DbSet<ProvisioningBundleRecord> ProvisioningBundles => Set<ProvisioningBundleRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.AuthorizationEpoch).HasColumnName("authorization_epoch");
            entity.HasIndex(x => x.OrganizationId);
        });

        modelBuilder.Entity<OrganizationRecord>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
            entity.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceRecord>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.OwnerOrganizationId, x.ProjectId, x.PackageId, x.AreaId });
            entity.HasOne(x => x.OwnerOrganization)
                .WithMany()
                .HasForeignKey(x => x.OwnerOrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceAuthorizationEpochRecord>(entity =>
        {
            entity.ToTable("device_authorization_epochs");
            entity.HasKey(x => x.DeviceId);
            entity.Property(x => x.Epoch).IsRequired();
        });

        modelBuilder.Entity<DeviceKeysetRecord>(entity =>
        {
            entity.ToTable("device_keysets");
            entity.HasKey(x => new { x.DeviceId, x.Generation });
            entity.Property(x => x.ProtectedPayload).IsRequired();
        });

        modelBuilder.Entity<PolicyVersionRecord>(entity =>
        {
            entity.ToTable("policy_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Version).IsRequired();
        });

        modelBuilder.Entity<ProjectRecord>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            entity.HasOne<OrganizationRecord>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackageRecord>(entity =>
        {
            entity.ToTable("packages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            entity.HasOne<ProjectRecord>()
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AreaRecord>(entity =>
        {
            entity.ToTable("areas");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            entity.HasOne<OrganizationRecord>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommandAuditRecord>(entity =>
        {
            entity.ToTable("command_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.DeviceId, x.CorrelationId }).IsUnique();
        });

        modelBuilder.Entity<PermissionGrantRecord>(entity =>
        {
            entity.ToTable("permission_grants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Permission).HasMaxLength(160);
            entity.Property(x => x.Action).HasMaxLength(160);
            entity.HasIndex(x => new { x.UserId, x.Kind, x.PolicyVersion });
        });

        modelBuilder.Entity<DeviceRoleRecord>(entity =>
        {
            entity.ToTable("device_roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512);
            // Compare-and-set for update and delete: EF puts the original value
            // into the WHERE clause. Model-only; the column is a plain integer.
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.NormalizedName }).IsUnique();
            entity.HasOne<OrganizationRecord>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Permissions)
                .WithOne()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceRolePermissionRecord>(entity =>
        {
            entity.ToTable("device_role_permissions");
            entity.HasKey(x => new { x.RoleId, x.Permission });
            entity.Property(x => x.Permission).HasMaxLength(160);
        });

        modelBuilder.Entity<DeviceMembershipRecord>(entity =>
        {
            entity.ToTable("device_memberships");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.DeviceId }).IsUnique();
            entity.HasIndex(x => x.DeviceId);
            entity.HasIndex(x => x.RoleId);
            // Compare-and-set on the current role, model-only like Version above.
            entity.Property(x => x.RoleId).IsConcurrencyToken();
            entity.HasOne<DeviceRecord>()
                .WithMany()
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DeviceRoleRecord>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceAccessAuditRecord>(entity =>
        {
            // No foreign keys: the audit outlives the roles and memberships it describes.
            entity.ToTable("device_access_audits");
            entity.HasKey(x => x.AuditId);
            entity.Property(x => x.Actor).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Change).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.DeviceId, x.OccurredAt });
            entity.HasIndex(x => new { x.RoleId, x.OccurredAt });
        });

        modelBuilder.Entity<LicenseTrustRecord>(entity =>
        {
            entity.ToTable("license_trust_store");
            entity.HasKey(x => x.Kid);
            entity.Property(x => x.Kid).HasMaxLength(128);
            entity.Property(x => x.Issuer).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PublicKey).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.Status, x.EffectiveFrom });
        });

        modelBuilder.Entity<LicenseTrustAuditRecord>(entity =>
        {
            entity.ToTable("license_trust_audits");
            entity.HasKey(x => x.AuditId);
            entity.Property(x => x.Kid).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Issuer).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Change).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PreviousStatus).HasMaxLength(32);
            entity.Property(x => x.NewStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Actor).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => new { x.Kid, x.OccurredAt });
        });

        modelBuilder.Entity<LicenseRecord>(entity =>
        {
            entity.ToTable("licenses");
            entity.HasKey(x => x.LicenseId);
            entity.Property(x => x.Kid).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Issuer).HasMaxLength(256).IsRequired();
            entity.Property(x => x.RawEnvelope).IsRequired();
            entity.Property(x => x.RawSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.DeviceId, x.RawSha256 }).IsUnique();
            // One signed envelope owns a generation for a device. The unique
            // fence makes concurrent applies fail closed instead of creating
            // two different documents for the same firmware generation.
            entity.HasIndex(x => new { x.DeviceId, x.Generation }).IsUnique();
        });

        modelBuilder.Entity<LicenseApplyJournalRecord>(entity =>
        {
            entity.ToTable("license_apply_journals");
            entity.HasKey(x => x.JournalId);
            entity.Property(x => x.PriorRawSha256).HasMaxLength(64);
            entity.Property(x => x.TargetRawSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.DeviceId, x.State, x.UpdatedAt });
        });

        modelBuilder.Entity<ProvisioningBundleRecord>(entity =>
        {
            entity.ToTable("provisioning_bundles");
            entity.HasKey(x => x.BundleId);
            entity.Property(x => x.ProtectedPayload).IsRequired();
            entity.HasIndex(x => new { x.DeviceId, x.Used, x.ExpiresAt });
        });
    }
}
