using Hms.Server.Infrastructure.Persistence;
using Hms.Server.Infrastructure.Persistence.Migrations;
using Hms.Server.Infrastructure;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Licensing;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Server.IntegrationTests;

public sealed class PersistenceModelTests
{
    private static readonly UserId Grantor = new(Guid.NewGuid());
    private static readonly DateTimeOffset GrantedAt = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Device_classification_columns_are_nullable_and_owner_is_required()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Device_classification_columns_are_nullable_and_owner_is_required))
            .Options;

        await using var db = new ApplicationDbContext(options);
        var organizationId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Master" });
        db.Devices.Add(new DeviceRecord { Id = deviceId, Name = "ESP-001", OwnerOrganizationId = organizationId });
        await db.SaveChangesAsync();

        var saved = await db.Devices.SingleAsync(x => x.Id == deviceId);
        Assert.Equal(organizationId, saved.OwnerOrganizationId);
        Assert.Null(saved.ProjectId);
        Assert.Null(saved.PackageId);
        Assert.Null(saved.AreaId);
    }

    [Fact]
    public void Model_has_management_tables_only()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Model_has_management_tables_only))
            .Options;

        using var db = new ApplicationDbContext(options);
        var tables = db.Model.GetEntityTypes().Select(x => x.GetTableName()).ToArray();

        Assert.Contains("organizations", tables);
        Assert.Contains("devices", tables);
        Assert.Contains("command_audits", tables);
        Assert.Contains("provisioning_bundles", tables);
        Assert.Contains("device_keysets", tables);
        Assert.Contains("policy_versions", tables);
        Assert.Contains("license_apply_journals", tables);
        Assert.DoesNotContain(tables, x => x is not null && x.Contains("telemetry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("license_trust_audits", tables);
        Assert.Contains("device_roles", tables);
        Assert.Contains("device_role_permissions", tables);
        Assert.Contains("device_memberships", tables);
        Assert.Contains("device_access_audits", tables);
    }

    [Fact]
    public void Model_snapshot_has_no_pending_changes()
    {
        // Same provider as the design-time factory; comparing the model with
        // the migrations snapshot does not open a connection.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=hms_server_design;Username=hms")
            .Options;

        using var db = new ApplicationDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Ef_license_trust_store_persists_rotation_audit()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_license_trust_store_persists_rotation_audit))
            .Options;

        using var db = new ApplicationDbContext(options);
        var store = new EfLicenseTrustStore(db);
        var now = DateTimeOffset.UtcNow;
        var entry = new LicenseTrustEntry("kid-1", "vendor", "public-key", "active", now, null);

        store.Upsert(entry, LicenseTrustChange.Created, "bootstrap", now);
        store.Upsert(entry with { Status = "revoked", EffectiveFrom = now.AddMinutes(1) },
            LicenseTrustChange.Revoked, "operator-1", now.AddMinutes(1));

        var audit = db.LicenseTrustAudits.OrderBy(x => x.OccurredAt).ToArray();
        Assert.Equal(2, audit.Length);
        Assert.Equal("active", audit[1].PreviousStatus);
        Assert.Equal("revoked", audit[1].NewStatus);
    }

    [Fact]
    public void License_generation_is_unique_per_device_for_concurrent_apply_fence()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(License_generation_is_unique_per_device_for_concurrent_apply_fence))
            .Options;

        using var db = new ApplicationDbContext(options);
        var entity = db.Model.FindEntityType(typeof(LicenseRecord));
        var index = entity?.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(LicenseRecord.DeviceId), nameof(LicenseRecord.Generation)]));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void Ef_device_revoke_persists_the_lifecycle_state()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_device_revoke_persists_the_lifecycle_state))
            .Options;

        using var db = new ApplicationDbContext(options);
        var organization = Organization.Create("Master");
        db.Organizations.Add(new OrganizationRecord { Id = organization.Id.Value, Name = organization.Name });
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        db.Devices.Add(new DeviceRecord
        {
            Id = device.Id.Value,
            Name = device.Name,
            OwnerOrganizationId = organization.Id.Value,
            State = device.State.ToString(),
            KeyGeneration = device.KeyGeneration
        });
        db.SaveChanges();

        var store = new EfDeviceStore(db);
        var handler = new RevokeDeviceHandler(store, new InMemoryAuthorizationEpochStore());
        handler.Handle(device.Id);

        Assert.Equal(nameof(DeviceLifecycleState.Revoked), db.Devices.Single(x => x.Id == device.Id.Value).State);
    }

    [Fact]
    public void Ef_authorization_epoch_advances_monotonically_for_development_provider()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_authorization_epoch_advances_monotonically_for_development_provider))
            .Options;

        using var db = new ApplicationDbContext(options);
        var store = new EfAuthorizationEpochStore(db);
        var device = DeviceId.New();

        Assert.Equal(1, store.Advance(device));
        Assert.Equal(2, store.Advance(device));
        Assert.Equal(2, store.Current(device));
    }

    [Fact]
    public void Ef_permission_policy_version_uses_a_persistent_counter()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_permission_policy_version_uses_a_persistent_counter))
            .Options;

        using var db = new ApplicationDbContext(options);
        var store = new EfPermissionStore(db);

        Assert.Equal(0, store.PolicyVersion);
        Assert.Equal(1, store.Advance());
        Assert.Equal(2, store.Advance());
        Assert.Equal(2, store.PolicyVersion);
    }

    [Fact]
    public void Development_registration_uses_an_ephemeral_data_protection_provider()
    {
        var services = new ServiceCollection();
        services.AddHmsInfrastructure(allowInMemoryDevelopment: true, secretRootConfigured: false);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<EphemeralDataProtectionProvider>(provider.GetRequiredService<IDataProtectionProvider>());
    }

    [Fact]
    public void Development_registration_wires_identity_realtime_principal_refresher()
    {
        var services = new ServiceCollection();
        services.AddHmsInfrastructure(allowInMemoryDevelopment: true, secretRootConfigured: false);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var refresher = scope.ServiceProvider.GetRequiredService<IRealtimePrincipalRefresher>();

        Assert.IsType<Hms.Server.Infrastructure.Identity.IdentityRealtimePrincipalRefresher>(refresher);
    }

    [Fact]
    public async Task Ef_command_audit_reservation_rejects_correlation_collisions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_command_audit_reservation_rejects_correlation_collisions))
            .Options;

        await using var db = new ApplicationDbContext(options);
        var store = new EfCommandAuditStore(db);
        var device = DeviceId.New();
        var request = new DeviceCommandRequest(
            device,
            new UserId(Guid.NewGuid()),
            "system",
            "get_info",
            new Dictionary<string, object?> { ["value"] = 1 },
            "same-correlation",
            DateTimeOffset.UtcNow);
        var definition = new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000);
        var plan = new CommandExecutionPlan(
            request, definition, 1, request.RequestedAt.AddMinutes(2));

        var first = await store.ReserveAsync(plan, CommandRequestFingerprint.Compute(request));
        var second = await store.ReserveAsync(
            plan with
            {
                Request = request with { Action = "restart" }
            },
            "different-fingerprint");

        Assert.Equal(CommandAuditReservationStatus.Claimed, first.Status);
        Assert.Equal(CommandAuditReservationStatus.Conflict, second.Status);
    }

    [Fact]
    public async Task Ef_command_audit_marks_expired_in_flight_reservation_as_outcome_unknown()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_command_audit_marks_expired_in_flight_reservation_as_outcome_unknown))
            .Options;

        await using var db = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var clock = now;
        var store = new EfCommandAuditStore(db, TimeSpan.FromMinutes(5), () => clock);
        var request = new DeviceCommandRequest(
            DeviceId.New(), new UserId(Guid.NewGuid()), "system", "get_info",
            new Dictionary<string, object?>(), "stale-correlation", now);
        var definition = new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000);
        var plan = new CommandExecutionPlan(request, definition, 1, now.AddMinutes(2));

        var first = await store.ReserveAsync(plan, CommandRequestFingerprint.Compute(request));
        clock = now.AddMinutes(6);
        var second = await store.ReserveAsync(plan, CommandRequestFingerprint.Compute(request));

        Assert.Equal(CommandAuditReservationStatus.Claimed, first.Status);
        Assert.Equal(CommandAuditReservationStatus.StaleInFlight, second.Status);
    }

    [Fact]
    public async Task Ef_device_revoke_marks_in_flight_command_as_device_revoked()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_device_revoke_marks_in_flight_command_as_device_revoked))
            .Options;

        await using var db = new ApplicationDbContext(options);
        var organization = Organization.Create("Master");
        db.Organizations.Add(new OrganizationRecord { Id = organization.Id.Value, Name = organization.Name });
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        db.Devices.Add(new DeviceRecord
        {
            Id = device.Id.Value,
            Name = device.Name,
            OwnerOrganizationId = organization.Id.Value,
            State = device.State.ToString(),
            KeyGeneration = device.KeyGeneration
        });
        await db.SaveChangesAsync();

        var request = new DeviceCommandRequest(
            device.Id,
            new UserId(Guid.NewGuid()),
            "system",
            "get_info",
            new Dictionary<string, object?>(),
            "pending-revoke",
            DateTimeOffset.UtcNow);
        var plan = new CommandExecutionPlan(
            request,
            new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000),
            device.KeyGeneration,
            request.RequestedAt.AddMinutes(2));
        var audit = new EfCommandAuditStore(db);
        Assert.Equal(
            CommandAuditReservationStatus.Claimed,
            (await audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(request))).Status);

        new RevokeDeviceHandler(new EfDeviceStore(db), new InMemoryAuthorizationEpochStore(), audit).Handle(device.Id);

        await audit.RecordAsync(
            plan,
            new CommandExecutionResult(CommandExecutionStatus.MqttDeferred, "MQTT_DEFERRED", request.CorrelationId));

        var record = await db.CommandAudits.SingleAsync(x => x.CorrelationId == request.CorrelationId);
        Assert.Equal(nameof(CommandExecutionStatus.Failed), record.Status);
        Assert.Equal("DEVICE_REVOKED", record.Code);
    }

    [Fact]
    public async Task Ef_command_completion_without_reservation_does_not_stage_an_audit_row()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_command_completion_without_reservation_does_not_stage_an_audit_row))
            .Options;
        await using var db = new ApplicationDbContext(options);
        var request = new DeviceCommandRequest(
            new DeviceId(Guid.NewGuid()), new UserId(Guid.NewGuid()), "system", "get_info",
            new Dictionary<string, object?>(), "unreserved-completion", DateTimeOffset.UtcNow);
        var plan = new CommandExecutionPlan(
            request,
            new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000),
            1, request.RequestedAt.AddMinutes(2));

        await new EfCommandAuditStore(db).RecordAsync(
            plan,
            new CommandExecutionResult(CommandExecutionStatus.MqttDeferred, "MQTT_DEFERRED", request.CorrelationId));
        await db.SaveChangesAsync();

        Assert.Empty(db.CommandAudits);
    }

    [Fact]
    public void Device_role_name_is_unique_per_organization()
    {
        using var db = InMemoryContexts(nameof(Device_role_name_is_unique_per_organization))();
        var roles = Entity<DeviceRoleRecord>(db);

        var index = FindIndex(roles, nameof(DeviceRoleRecord.OrganizationId), nameof(DeviceRoleRecord.NormalizedName));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
        Assert.DoesNotContain(roles.GetIndexes(), x =>
            x.IsUnique && x.Properties.Any(property => property.Name == nameof(DeviceRoleRecord.Name)));
    }

    [Fact]
    public void Device_membership_is_unique_per_user_and_device()
    {
        using var db = InMemoryContexts(nameof(Device_membership_is_unique_per_user_and_device))();
        var memberships = Entity<DeviceMembershipRecord>(db);

        var byUserAndDevice = FindIndex(memberships, nameof(DeviceMembershipRecord.UserId), nameof(DeviceMembershipRecord.DeviceId));
        var byDevice = FindIndex(memberships, nameof(DeviceMembershipRecord.DeviceId));
        var byRole = FindIndex(memberships, nameof(DeviceMembershipRecord.RoleId));

        Assert.NotNull(byUserAndDevice);
        Assert.True(byUserAndDevice!.IsUnique);
        Assert.NotNull(byDevice);
        Assert.False(byDevice!.IsUnique);
        Assert.NotNull(byRole);
        Assert.False(byRole!.IsUnique);
    }

    [Fact]
    public void Device_access_foreign_keys_restrict_except_role_permissions_cascade()
    {
        using var db = InMemoryContexts(nameof(Device_access_foreign_keys_restrict_except_role_permissions_cascade))();
        var roles = Entity<DeviceRoleRecord>(db);
        var permissions = Entity<DeviceRolePermissionRecord>(db);
        var memberships = Entity<DeviceMembershipRecord>(db);
        var audits = Entity<DeviceAccessAuditRecord>(db);

        AssertForeignKey(roles, nameof(DeviceRoleRecord.OrganizationId), typeof(OrganizationRecord), DeleteBehavior.Restrict);
        AssertForeignKey(permissions, nameof(DeviceRolePermissionRecord.RoleId), typeof(DeviceRoleRecord), DeleteBehavior.Cascade);
        AssertForeignKey(memberships, nameof(DeviceMembershipRecord.DeviceId), typeof(DeviceRecord), DeleteBehavior.Restrict);
        AssertForeignKey(memberships, nameof(DeviceMembershipRecord.RoleId), typeof(DeviceRoleRecord), DeleteBehavior.Restrict);
        Assert.Single(roles.GetForeignKeys());
        Assert.Single(permissions.GetForeignKeys());
        Assert.Equal(2, memberships.GetForeignKeys().Count());
        Assert.Empty(audits.GetForeignKeys());
    }

    [Fact]
    public void Device_access_columns_match_contract_lengths_and_keys()
    {
        using var db = InMemoryContexts(nameof(Device_access_columns_match_contract_lengths_and_keys))();
        var roles = Entity<DeviceRoleRecord>(db);
        var permissions = Entity<DeviceRolePermissionRecord>(db);
        var memberships = Entity<DeviceMembershipRecord>(db);
        var audits = Entity<DeviceAccessAuditRecord>(db);

        AssertColumn(roles, nameof(DeviceRoleRecord.Name), 128, nullable: false);
        AssertColumn(roles, nameof(DeviceRoleRecord.NormalizedName), 128, nullable: false);
        AssertColumn(roles, nameof(DeviceRoleRecord.Description), 512, nullable: true);
        AssertColumn(permissions, nameof(DeviceRolePermissionRecord.Permission), 160, nullable: false);
        AssertColumn(audits, nameof(DeviceAccessAuditRecord.Actor), 256, nullable: false);
        AssertColumn(audits, nameof(DeviceAccessAuditRecord.Change), 32, nullable: false);
        AssertColumn(audits, nameof(DeviceAccessAuditRecord.PreviousValue), null, nullable: true);
        AssertColumn(audits, nameof(DeviceAccessAuditRecord.NewValue), null, nullable: true);
        Assert.Equal(
            [nameof(DeviceRolePermissionRecord.RoleId), nameof(DeviceRolePermissionRecord.Permission)],
            permissions.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal([nameof(DeviceAccessAuditRecord.AuditId)], audits.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.NotNull(FindIndex(audits, nameof(DeviceAccessAuditRecord.DeviceId), nameof(DeviceAccessAuditRecord.OccurredAt)));
        Assert.NotNull(FindIndex(audits, nameof(DeviceAccessAuditRecord.RoleId), nameof(DeviceAccessAuditRecord.OccurredAt)));

        // The compare-and-set tokens exist in the model only: EF puts the
        // original value into the WHERE clause; the database never generates them.
        var tokens = new[]
        {
            roles.FindProperty(nameof(DeviceRoleRecord.Version))!,
            memberships.FindProperty(nameof(DeviceMembershipRecord.RoleId))!
        };
        Assert.All(tokens, token =>
        {
            Assert.True(token.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.Never, token.ValueGenerated);
        });
    }

    [Fact]
    public void Ef_device_role_store_round_trips_permissions_and_writes_audit()
    {
        var probe = new SaveChangesProbe();
        var contexts = InMemoryContexts(nameof(Ef_device_role_store_round_trips_permissions_and_writes_audit), probe);
        var organization = OrganizationId.New();
        var role = DeviceRole.Create(organization, " Operator ", "Runs the line", ["device.view", "sys.reboot"]);
        var audit = AccessAudit(DeviceAccessChanges.RoleCreated, role.Id);

        using (var write = contexts())
            new EfDeviceRoleStore(write).Add(role, audit);

        Assert.Equal(1, probe.Count);
        using var read = contexts();
        var store = new EfDeviceRoleStore(read);
        var found = store.Find(role.Id);
        Assert.NotNull(found);
        Assert.Equal("Operator", found!.Name);
        Assert.Equal("Runs the line", found.Description);
        Assert.Equal(organization, found.OrganizationId);
        Assert.Equal(1, found.Version);
        Assert.Equal(["device.view", "sys.reboot"], found.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal([role.Id], store.ListByOrganization(organization).Select(x => x.Id));
        Assert.Empty(read.ChangeTracker.Entries());

        var record = read.DeviceRoles.AsNoTracking().Single();
        Assert.Equal("OPERATOR", record.NormalizedName);
        Assert.Equal(Grantor.Value, record.CreatedBy);
        Assert.Equal(GrantedAt, record.CreatedAt);
        Assert.Equal(GrantedAt, record.UpdatedAt);
        var saved = Assert.Single(read.DeviceAccessAudits.AsNoTracking());
        Assert.Equal(audit.AuditId, saved.AuditId);
        Assert.Equal(DeviceAccessChanges.RoleCreated, saved.Change);
        Assert.Equal(Grantor.Value.ToString("D"), saved.Actor);
        Assert.Equal(role.Id.Value, saved.RoleId);
    }

    [Fact]
    public void Ef_device_role_update_diffs_permissions_and_bumps_version()
    {
        var contexts = InMemoryContexts(nameof(Ef_device_role_update_diffs_permissions_and_bumps_version));
        var organization = OrganizationId.New();
        var role = DeviceRole.Create(organization, "Operator", null, ["device.view", "sys.reboot"]);
        var updatedAt = GrantedAt.AddMinutes(5);
        using (var db = contexts())
            new EfDeviceRoleStore(db).Add(role, AccessAudit(DeviceAccessChanges.RoleCreated, role.Id));

        using (var db = contexts())
        {
            var store = new EfDeviceRoleStore(db);
            var current = store.Find(role.Id)!;
            current.Update("Supervisor", "Runs the line", ["device.view", "device.edit_basics"]);
            store.Update(current, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id) with { OccurredAt = updatedAt });
        }

        using (var db = contexts())
        {
            var record = db.DeviceRoles.AsNoTracking().Include(x => x.Permissions).Single();
            Assert.Equal(2, record.Version);
            Assert.Equal("Supervisor", record.Name);
            Assert.Equal("SUPERVISOR", record.NormalizedName);
            Assert.Equal("Runs the line", record.Description);
            Assert.Equal(
                ["device.edit_basics", "device.view"],
                record.Permissions.Select(x => x.Permission).Order(StringComparer.Ordinal));
            Assert.Equal(Grantor.Value, record.CreatedBy);
            Assert.Equal(GrantedAt, record.CreatedAt);
            Assert.Equal(updatedAt, record.UpdatedAt);
            Assert.Equal(2, db.DeviceRolePermissions.Count());
            Assert.Equal(2, db.DeviceAccessAudits.Count());
        }

        using (var db = contexts())
        {
            var store = new EfDeviceRoleStore(db);
            var stale = DeviceRole.Rehydrate(role.Id.Value, organization.Value, "Operator", null, [], 1);
            stale.Update("Operator", null, []);
            AssertCode("DEVICE_ROLE_VERSION_STALE", () => store.Update(stale, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id)));

            // The organization is immutable: a copy that claims another one does not move the role.
            var moved = DeviceRole.Rehydrate(role.Id.Value, Guid.NewGuid(), "Supervisor", null, ["device.view"], 2);
            moved.Update("Supervisor", null, ["device.view"]);
            store.Update(moved, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id));
        }

        using (var db = contexts())
        {
            var record = db.DeviceRoles.AsNoTracking().Single();
            Assert.Equal(organization.Value, record.OrganizationId);
            Assert.Equal(3, record.Version);
            Assert.Equal(3, db.DeviceAccessAudits.Count());
        }
    }

    [Fact]
    public void Ef_device_role_store_rejects_case_insensitive_duplicate_name_in_same_organization()
    {
        var contexts = InMemoryContexts(nameof(Ef_device_role_store_rejects_case_insensitive_duplicate_name_in_same_organization));
        using var db = contexts();
        var store = new EfDeviceRoleStore(db);
        var organization = OrganizationId.New();
        var operatorRole = DeviceRole.Create(organization, "Operator", null, []);
        var technician = DeviceRole.Create(organization, "Technician", null, []);
        store.Add(operatorRole, AccessAudit(DeviceAccessChanges.RoleCreated, operatorRole.Id));
        store.Add(technician, AccessAudit(DeviceAccessChanges.RoleCreated, technician.Id));

        AssertCode("DEVICE_ROLE_NAME_TAKEN", () =>
            store.Add(DeviceRole.Create(organization, " operator ", null, []), AccessAudit(DeviceAccessChanges.RoleCreated)));
        var renamed = store.Find(technician.Id)!;
        renamed.Update("OPERATOR", null, []);
        AssertCode("DEVICE_ROLE_NAME_TAKEN", () => store.Update(renamed, AccessAudit(DeviceAccessChanges.RoleUpdated)));

        using (var read = contexts())
        {
            Assert.Equal(2, read.DeviceRoles.Count());
            Assert.Equal(2, read.DeviceAccessAudits.Count());
            Assert.Equal("Technician", read.DeviceRoles.AsNoTracking().Single(x => x.Id == technician.Id.Value).Name);
        }

        store.Add(DeviceRole.Create(OrganizationId.New(), "Operator", null, []), AccessAudit(DeviceAccessChanges.RoleCreated));
        var recased = store.Find(operatorRole.Id)!;
        recased.Update("OPERATOR", null, []);
        store.Update(recased, AccessAudit(DeviceAccessChanges.RoleUpdated));

        Assert.Equal("OPERATOR", store.Find(operatorRole.Id)!.Name);
    }

    [Fact]
    public void Ef_device_role_remove_checks_missing_then_version_then_in_use()
    {
        using var db = InMemoryContexts(nameof(Ef_device_role_remove_checks_missing_then_version_then_in_use))();
        var roles = new EfDeviceRoleStore(db);
        var role = DeviceRole.Create(OrganizationId.New(), "Operator", null, ["device.view"]);
        roles.Add(role, AccessAudit(DeviceAccessChanges.RoleCreated, role.Id));
        new EfDeviceMembershipStore(db).Upsert(
            Member(NewUser(), DeviceId.New(), role), null, AccessAudit(DeviceAccessChanges.MembershipAssigned, role.Id));
        var auditCount = db.DeviceAccessAudits.Count();

        AssertCode("DEVICE_ROLE_NOT_FOUND", () => roles.Remove(DeviceRoleId.New(), 1, AccessAudit(DeviceAccessChanges.RoleDeleted)));
        AssertCode("DEVICE_ROLE_VERSION_STALE", () => roles.Remove(role.Id, 2, AccessAudit(DeviceAccessChanges.RoleDeleted, role.Id)));
        AssertCode("DEVICE_ROLE_IN_USE", () => roles.Remove(role.Id, 1, AccessAudit(DeviceAccessChanges.RoleDeleted, role.Id)));

        Assert.NotNull(roles.Find(role.Id));
        Assert.Equal(auditCount, db.DeviceAccessAudits.Count());
    }

    [Fact]
    public void Ef_device_role_remove_deletes_permissions_and_audit_in_one_save()
    {
        var probe = new SaveChangesProbe();
        var contexts = InMemoryContexts(nameof(Ef_device_role_remove_deletes_permissions_and_audit_in_one_save), probe);
        var role = DeviceRole.Create(OrganizationId.New(), "Operator", null, ["device.view", "sys.reboot"]);
        using (var db = contexts())
            new EfDeviceRoleStore(db).Add(role, AccessAudit(DeviceAccessChanges.RoleCreated, role.Id));
        var savesBefore = probe.Count;

        using (var db = contexts())
            new EfDeviceRoleStore(db).Remove(role.Id, 1, AccessAudit(DeviceAccessChanges.RoleDeleted, role.Id));

        Assert.Equal(savesBefore + 1, probe.Count);
        Assert.Equal(
            [
                "Added DeviceAccessAuditRecord",
                "Deleted DeviceRolePermissionRecord",
                "Deleted DeviceRolePermissionRecord",
                "Deleted DeviceRoleRecord"
            ],
            probe.LastBatch.Order(StringComparer.Ordinal));
        using var read = contexts();
        Assert.Empty(read.DeviceRoles);
        Assert.Empty(read.DeviceRolePermissions);
        Assert.Single(read.DeviceAccessAudits, x => x.Change == DeviceAccessChanges.RoleDeleted);
    }

    [Fact]
    public void Ef_membership_upsert_replaces_role_and_writes_audit()
    {
        var probe = new SaveChangesProbe();
        var contexts = InMemoryContexts(nameof(Ef_membership_upsert_replaces_role_and_writes_audit), probe);
        var user = NewUser();
        var device = DeviceId.New();
        var secondGrantor = NewUser();
        var regrantedAt = GrantedAt.AddMinutes(1);
        DeviceRole viewer;
        DeviceRole editor;
        using (var db = contexts())
        {
            (viewer, editor) = AddViewerAndEditor(db);
            var memberships = new EfDeviceMembershipStore(db);
            var saves = probe.Count;
            memberships.Upsert(Member(user, device, viewer), null,
                AccessAudit(DeviceAccessChanges.MembershipAssigned, viewer.Id, device, user));
            Assert.Equal(saves + 1, probe.Count);
            var firstId = db.DeviceMemberships.AsNoTracking().Single().Id;

            memberships.Upsert(new DeviceMembership(user, device, editor.Id, secondGrantor, regrantedAt), viewer.Id,
                AccessAudit(DeviceAccessChanges.MembershipChanged, editor.Id, device, user));

            Assert.Equal(firstId, db.DeviceMemberships.AsNoTracking().Single().Id);
        }

        using var read = contexts();
        var record = Assert.Single(read.DeviceMemberships.AsNoTracking());
        Assert.Equal(editor.Id.Value, record.RoleId);
        Assert.Equal(secondGrantor.Value, record.GrantedBy);
        Assert.Equal(regrantedAt, record.GrantedAt);
        Assert.Equal(editor.Id, new EfDeviceMembershipStore(read).Find(user, device)?.RoleId);
        var audits = read.DeviceAccessAudits.AsNoTracking().Where(x => x.TargetUserId == user.Value).ToArray();
        Assert.Single(audits, x => x.Change == DeviceAccessChanges.MembershipAssigned);
        Assert.Single(audits, x => x.Change == DeviceAccessChanges.MembershipChanged);
    }

    [Fact]
    public void Ef_membership_upsert_and_remove_reject_expected_role_mismatch()
    {
        var contexts = InMemoryContexts(nameof(Ef_membership_upsert_and_remove_reject_expected_role_mismatch));
        var user = NewUser();
        var other = NewUser();
        var device = DeviceId.New();
        DeviceRole viewer;
        int auditCount;
        using (var db = contexts())
        {
            (viewer, var editor) = AddViewerAndEditor(db);
            var memberships = new EfDeviceMembershipStore(db);
            memberships.Upsert(Member(user, device, viewer), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
            auditCount = db.DeviceAccessAudits.Count();

            AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
                memberships.Upsert(Member(user, device, editor), null, AccessAudit(DeviceAccessChanges.MembershipChanged)));
            AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
                memberships.Upsert(Member(user, device, editor), editor.Id, AccessAudit(DeviceAccessChanges.MembershipChanged)));
            AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
                memberships.Upsert(Member(other, device, editor), viewer.Id, AccessAudit(DeviceAccessChanges.MembershipAssigned)));
            AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
                memberships.Remove(user, device, editor.Id, AccessAudit(DeviceAccessChanges.MembershipRemoved)));
            AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
                memberships.Remove(user, device, null, AccessAudit(DeviceAccessChanges.MembershipRemoved)));
            AssertCode("DEVICE_MEMBERSHIP_CHANGED", () =>
                memberships.Remove(other, device, viewer.Id, AccessAudit(DeviceAccessChanges.MembershipRemoved)));
            Assert.False(memberships.Remove(other, device, null, AccessAudit(DeviceAccessChanges.MembershipRemoved)));
        }

        using var read = contexts();
        Assert.Equal(viewer.Id.Value, Assert.Single(read.DeviceMemberships.AsNoTracking()).RoleId);
        Assert.Equal(auditCount, read.DeviceAccessAudits.Count());
    }

    [Fact]
    public void Ef_lost_race_maps_concurrency_conflicts_to_store_codes()
    {
        var probe = new SaveChangesProbe();
        var contexts = InMemoryContexts(nameof(Ef_lost_race_maps_concurrency_conflicts_to_store_codes), probe);
        var organization = OrganizationId.New();
        var viewer = DeviceRole.Create(organization, "Viewer", null, ["device.view"]);
        var editor = DeviceRole.Create(organization, "Editor", null, ["device.edit_basics"]);
        var unused = DeviceRole.Create(organization, "Unused", null, ["device.view"]);
        var user = NewUser();
        var device = DeviceId.New();
        using (var seed = contexts())
        {
            var roles = new EfDeviceRoleStore(seed);
            roles.Add(viewer, AccessAudit(DeviceAccessChanges.RoleCreated, viewer.Id));
            roles.Add(editor, AccessAudit(DeviceAccessChanges.RoleCreated, editor.Id));
            roles.Add(unused, AccessAudit(DeviceAccessChanges.RoleCreated, unused.Id));
            new EfDeviceMembershipStore(seed).Upsert(
                Member(user, device, viewer), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
        }

        // Each stale context tracks a row as it was; another context writes
        // first. Only the concurrency token can catch the stale write, because
        // the pre-checks read the stale tracked row.
        using (var stale = contexts())
        {
            stale.DeviceMemberships.Single(x => x.UserId == user.Value);
            using (var winner = contexts())
                new EfDeviceMembershipStore(winner).Upsert(
                    Member(user, device, editor), viewer.Id, AccessAudit(DeviceAccessChanges.MembershipChanged));

            // Re-granting the tracked values changes no column, yet the stored row moved on.
            AssertLostRace(stale, probe, "DEVICE_MEMBERSHIP_CHANGED", "Modified DeviceMembershipRecord",
                () => new EfDeviceMembershipStore(stale).Upsert(
                    Member(user, device, viewer), viewer.Id, AccessAudit(DeviceAccessChanges.MembershipChanged)));
        }

        using (var stale = contexts())
        {
            stale.DeviceMemberships.Single(x => x.UserId == user.Value);
            using (var winner = contexts())
                new EfDeviceMembershipStore(winner).Upsert(
                    Member(user, device, viewer), editor.Id, AccessAudit(DeviceAccessChanges.MembershipChanged));

            AssertLostRace(stale, probe, "DEVICE_MEMBERSHIP_CHANGED", "Deleted DeviceMembershipRecord",
                () => new EfDeviceMembershipStore(stale).Remove(
                    user, device, editor.Id, AccessAudit(DeviceAccessChanges.MembershipRemoved)));
        }

        using (var stale = contexts())
        {
            stale.DeviceRoles.Include(x => x.Permissions).Single(x => x.Id == unused.Id.Value);
            RenameRole(contexts, unused.Id, "Unused v2");
            var update = DeviceRole.Rehydrate(unused.Id.Value, organization.Value, "Unused", null, ["device.view"], 1);
            update.Update("Unused stale", null, ["device.view"]);

            AssertLostRace(stale, probe, "DEVICE_ROLE_VERSION_STALE", "Modified DeviceRoleRecord",
                () => new EfDeviceRoleStore(stale).Update(update, AccessAudit(DeviceAccessChanges.RoleUpdated, unused.Id)));
        }

        using (var stale = contexts())
        {
            stale.DeviceRoles.Include(x => x.Permissions).Single(x => x.Id == unused.Id.Value);
            RenameRole(contexts, unused.Id, "Unused v3");

            AssertLostRace(stale, probe, "DEVICE_ROLE_VERSION_STALE", "Deleted DeviceRoleRecord",
                () => new EfDeviceRoleStore(stale).Remove(unused.Id, 2, AccessAudit(DeviceAccessChanges.RoleDeleted, unused.Id)));
        }

        using var read = contexts();
        Assert.Equal(viewer.Id.Value, Assert.Single(read.DeviceMemberships.AsNoTracking()).RoleId);
        var unusedRecord = read.DeviceRoles.AsNoTracking().Single(x => x.Id == unused.Id.Value);
        Assert.Equal(3, unusedRecord.Version);
        Assert.Equal("Unused v3", unusedRecord.Name);
    }

    [Fact]
    public void Ef_membership_remove_writes_audit_and_regrant_restores()
    {
        using var db = InMemoryContexts(nameof(Ef_membership_remove_writes_audit_and_regrant_restores))();
        var (viewer, _) = AddViewerAndEditor(db);
        var memberships = new EfDeviceMembershipStore(db);
        var user = NewUser();
        var device = DeviceId.New();
        memberships.Upsert(Member(user, device, viewer), null,
            AccessAudit(DeviceAccessChanges.MembershipAssigned, viewer.Id, device, user));

        Assert.True(memberships.Remove(user, device, viewer.Id,
            AccessAudit(DeviceAccessChanges.MembershipRemoved, viewer.Id, device, user)));
        Assert.Null(memberships.Find(user, device));
        Assert.Single(db.DeviceAccessAudits.AsNoTracking(), x => x.Change == DeviceAccessChanges.MembershipRemoved);

        memberships.Upsert(Member(user, device, viewer), null,
            AccessAudit(DeviceAccessChanges.MembershipAssigned, viewer.Id, device, user));

        Assert.Equal(viewer.Id, memberships.Find(user, device)?.RoleId);
    }

    [Fact]
    public void Ef_membership_lists_by_role_device_and_user()
    {
        var contexts = InMemoryContexts(nameof(Ef_membership_lists_by_role_device_and_user));
        var first = NewUser();
        var second = NewUser();
        var line = DeviceId.New();
        var pump = DeviceId.New();
        DeviceRole viewer;
        DeviceRole editor;
        using (var db = contexts())
        {
            (viewer, editor) = AddViewerAndEditor(db);
            var memberships = new EfDeviceMembershipStore(db);
            memberships.Upsert(Member(first, line, viewer), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
            memberships.Upsert(Member(first, pump, viewer), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
            memberships.Upsert(Member(second, line, editor), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
        }

        using var read = contexts();
        var store = new EfDeviceMembershipStore(read);

        Assert.Equal(
            new HashSet<(UserId, DeviceId)> { (first, line), (first, pump) },
            store.ListByRole(viewer.Id).Select(x => (x.UserId, x.DeviceId)).ToHashSet());
        Assert.Equal(new HashSet<UserId> { first, second }, store.ListByDevice(line).Select(x => x.UserId).ToHashSet());
        Assert.Equal(new HashSet<DeviceId> { line, pump }, store.ListByUser(first).Select(x => x.DeviceId).ToHashSet());
        Assert.Equal(editor.Id, Assert.Single(store.ListByUser(second)).RoleId);
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Fact]
    public void Ef_membership_upsert_rejects_unknown_role()
    {
        using var db = InMemoryContexts(nameof(Ef_membership_upsert_rejects_unknown_role))();
        var memberships = new EfDeviceMembershipStore(db);

        AssertCode("DEVICE_ROLE_NOT_FOUND", () => memberships.Upsert(
            new DeviceMembership(NewUser(), DeviceId.New(), DeviceRoleId.New(), Grantor, GrantedAt),
            null,
            AccessAudit(DeviceAccessChanges.MembershipAssigned)));

        Assert.Empty(db.DeviceMemberships);
        Assert.Empty(db.DeviceAccessAudits);

        // The role check runs before the compare-and-set: the expected role is wrong too.
        var (viewer, _) = AddViewerAndEditor(db);
        var user = NewUser();
        var device = DeviceId.New();
        memberships.Upsert(Member(user, device, viewer), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
        AssertCode("DEVICE_ROLE_NOT_FOUND", () => memberships.Upsert(
            new DeviceMembership(user, device, DeviceRoleId.New(), Grantor, GrantedAt),
            null,
            AccessAudit(DeviceAccessChanges.MembershipChanged)));
    }

    [Fact]
    public void Ef_device_role_update_of_missing_role_reports_not_found()
    {
        using var db = InMemoryContexts(nameof(Ef_device_role_update_of_missing_role_reports_not_found))();
        var missing = DeviceRole.Create(OrganizationId.New(), "Missing", null, []);
        missing.Update("Missing", null, []);

        AssertCode("DEVICE_ROLE_NOT_FOUND", () =>
            new EfDeviceRoleStore(db).Update(missing, AccessAudit(DeviceAccessChanges.RoleUpdated)));

        Assert.Empty(db.DeviceAccessAudits);
    }

    [Fact]
    public void Ef_device_role_update_checks_names_in_the_stored_organization()
    {
        using var db = InMemoryContexts(nameof(Ef_device_role_update_checks_names_in_the_stored_organization))();
        var store = new EfDeviceRoleStore(db);
        var organization = OrganizationId.New();
        var otherOrganization = OrganizationId.New();
        var technician = DeviceRole.Create(organization, "Technician", null, []);
        store.Add(DeviceRole.Create(organization, "Operator", null, []), AccessAudit(DeviceAccessChanges.RoleCreated));
        store.Add(technician, AccessAudit(DeviceAccessChanges.RoleCreated, technician.Id));
        store.Add(DeviceRole.Create(otherOrganization, "Supervisor", null, []), AccessAudit(DeviceAccessChanges.RoleCreated));

        var claimsOther = DeviceRole.Rehydrate(technician.Id.Value, otherOrganization.Value, "Technician", null, [], 1);
        claimsOther.Update("Operator", null, []);
        AssertCode("DEVICE_ROLE_NAME_TAKEN", () => store.Update(claimsOther, AccessAudit(DeviceAccessChanges.RoleUpdated)));

        var moved = DeviceRole.Rehydrate(technician.Id.Value, otherOrganization.Value, "Technician", null, [], 1);
        moved.Update("Supervisor", null, []);
        store.Update(moved, AccessAudit(DeviceAccessChanges.RoleUpdated));

        var stored = store.Find(technician.Id)!;
        Assert.Equal(organization, stored.OrganizationId);
        Assert.Equal("Supervisor", stored.Name);
    }

    [Fact]
    public void Ef_device_access_writes_reject_unknown_audit_change_without_writing()
    {
        using var db = InMemoryContexts(nameof(Ef_device_access_writes_reject_unknown_audit_change_without_writing))();
        var roles = new EfDeviceRoleStore(db);
        var memberships = new EfDeviceMembershipStore(db);
        var (viewer, _) = AddViewerAndEditor(db);
        var user = NewUser();
        var device = DeviceId.New();
        memberships.Upsert(Member(user, device, viewer), null, AccessAudit(DeviceAccessChanges.MembershipAssigned));
        var auditCount = db.DeviceAccessAudits.Count();
        var unknown = AccessAudit("ROLE_RENAMED");
        var renamed = roles.Find(viewer.Id)!;
        renamed.Update("Renamed", null, ["device.view"]);

        Assert.Throws<ArgumentException>(() => roles.Add(DeviceRole.Create(OrganizationId.New(), "Other", null, []), unknown));
        Assert.Throws<ArgumentException>(() => roles.Update(renamed, unknown));
        Assert.Throws<ArgumentException>(() => roles.Remove(viewer.Id, 1, unknown));
        Assert.Throws<ArgumentException>(() => memberships.Upsert(Member(NewUser(), device, viewer), null, unknown));
        Assert.Throws<ArgumentException>(() => memberships.Remove(user, device, viewer.Id, unknown));

        Assert.Equal("Viewer", roles.Find(viewer.Id)!.Name);
        Assert.Single(memberships.ListByDevice(device));
        Assert.Equal(auditCount, db.DeviceAccessAudits.Count());
    }

    [Fact]
    public void Ef_failed_staging_leaves_nothing_for_a_later_save()
    {
        var contexts = InMemoryContexts(nameof(Ef_failed_staging_leaves_nothing_for_a_later_save));
        var organization = OrganizationId.New();
        var audit = AccessAudit(DeviceAccessChanges.RoleCreated);
        var second = DeviceRole.Create(organization, "Technician", null, []);
        using (var db = contexts())
        {
            var store = new EfDeviceRoleStore(db);
            store.Add(DeviceRole.Create(organization, "Operator", null, []), audit);

            // Reusing an audit id is a caller bug; the store must not leave the role staged.
            Assert.ThrowsAny<InvalidOperationException>(() => store.Add(second, audit));
            db.SaveChanges();
        }

        using var read = contexts();
        Assert.Null(new EfDeviceRoleStore(read).Find(second.Id));
        Assert.Single(read.DeviceAccessAudits);
    }

    // The race tests below commit a competing write inside SavingChanges,
    // after the store's tracked load and pre-checks, then reject the save the
    // way PostgreSQL would (unique index or foreign key). EF InMemory enforces
    // neither, so the provider error is thrown by RaceInterceptor; the store's
    // reload-and-map code under test is the real one.
    [Fact]
    public void Ef_role_update_lost_to_identical_concurrent_update_reports_stale()
    {
        // Double submit: the winner adds the same permission first, so
        // PostgreSQL rejects the loser's permission row (duplicate key) before
        // its version-guarded UPDATE runs.
        var contexts = InMemoryContexts(nameof(Ef_role_update_lost_to_identical_concurrent_update_reports_stale));
        var role = SeedRole(contexts, OrganizationId.New(), "Operator", "device.view");
        var race = new RaceInterceptor(() => UpdateRolePermissions(contexts, role.Id, "device.view", "device.edit_basics"));
        using var db = contexts(race);
        var mine = DeviceRole.Rehydrate(role.Id.Value, role.OrganizationId.Value, "Operator", null, ["device.view"], 1);
        mine.Update("Operator", null, ["device.view", "device.edit_basics"]);

        AssertRejected(db, "DEVICE_ROLE_VERSION_STALE", () =>
            new EfDeviceRoleStore(db).Update(mine, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id)));
    }

    [Fact]
    public void Ef_role_update_lost_to_concurrent_delete_reports_not_found()
    {
        // PostgreSQL rejects the new permission row (foreign key) of a role deleted meanwhile.
        var contexts = InMemoryContexts(nameof(Ef_role_update_lost_to_concurrent_delete_reports_not_found));
        var role = SeedRole(contexts, OrganizationId.New(), "Operator", "device.view");
        var race = new RaceInterceptor(() => DeleteRole(contexts, role.Id));
        using var db = contexts(race);
        var mine = DeviceRole.Rehydrate(role.Id.Value, role.OrganizationId.Value, "Operator", null, ["device.view"], 1);
        mine.Update("Operator", null, ["device.view", "device.edit_basics"]);

        AssertRejected(db, "DEVICE_ROLE_NOT_FOUND", () =>
            new EfDeviceRoleStore(db).Update(mine, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id)));
    }

    [Fact]
    public void Ef_role_rename_lost_to_concurrent_delete_reports_not_found_through_the_token()
    {
        // No new permission row: the version token rejects the UPDATE itself.
        var contexts = InMemoryContexts(nameof(Ef_role_rename_lost_to_concurrent_delete_reports_not_found_through_the_token));
        var role = SeedRole(contexts, OrganizationId.New(), "Operator", "device.view");
        var race = new RaceInterceptor(() => DeleteRole(contexts, role.Id), rejectSave: false);
        using var db = contexts(race);
        var mine = DeviceRole.Rehydrate(role.Id.Value, role.OrganizationId.Value, "Operator", null, ["device.view"], 1);
        mine.Update("Renamed", null, ["device.view"]);

        AssertRejected(db, "DEVICE_ROLE_NOT_FOUND", () =>
            new EfDeviceRoleStore(db).Update(mine, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id)));
    }

    [Fact]
    public void Ef_role_rename_lost_to_concurrent_same_name_rename_reports_name_taken()
    {
        // Another role takes the name after the pre-check; PostgreSQL rejects
        // the loser through the unique (OrganizationId, NormalizedName) index.
        // The caller's copy claims another organization: the check must use
        // the stored one.
        var contexts = InMemoryContexts(nameof(Ef_role_rename_lost_to_concurrent_same_name_rename_reports_name_taken));
        var organization = OrganizationId.New();
        var role = SeedRole(contexts, organization, "Operator", "device.view");
        var other = SeedRole(contexts, organization, "Technician", "device.view");
        var race = new RaceInterceptor(() => RenameRole(contexts, other.Id, "supervisor"));
        using var db = contexts(race);
        var mine = DeviceRole.Rehydrate(role.Id.Value, Guid.NewGuid(), "Operator", null, ["device.view"], 1);
        mine.Update("Supervisor", null, ["device.view"]);

        AssertRejected(db, "DEVICE_ROLE_NAME_TAKEN", () =>
            new EfDeviceRoleStore(db).Update(mine, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id)));
    }

    [Fact]
    public void Ef_role_remove_lost_to_concurrent_remove_reports_not_found()
    {
        // Double-submitted delete: the loser's DELETE matches no row.
        var contexts = InMemoryContexts(nameof(Ef_role_remove_lost_to_concurrent_remove_reports_not_found));
        var role = SeedRole(contexts, OrganizationId.New(), "Operator", "device.view");
        var race = new RaceInterceptor(() => DeleteRole(contexts, role.Id), rejectSave: false);
        using var db = contexts(race);

        AssertRejected(db, "DEVICE_ROLE_NOT_FOUND", () =>
            new EfDeviceRoleStore(db).Remove(role.Id, 1, AccessAudit(DeviceAccessChanges.RoleDeleted, role.Id)));
    }

    [Fact]
    public void Ef_membership_writes_lost_to_remove_and_regrant_of_the_same_role_report_changed()
    {
        // Another admin removes the membership and grants the same role again:
        // the row the check read is gone, though the current role matches the
        // expected one again. The loser must not succeed silently or escape raw.
        var contexts = InMemoryContexts(nameof(Ef_membership_writes_lost_to_remove_and_regrant_of_the_same_role_report_changed));
        var viewer = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
        var editor = SeedRole(contexts, OrganizationId.New(), "Editor", "device.edit_basics");
        var user = NewUser();
        var device = DeviceId.New();
        Assign(contexts, user, device, viewer.Id);

        void RemoveAndRegrant()
        {
            using (var other = contexts())
                new EfDeviceMembershipStore(other).Remove(
                    user, device, viewer.Id, AccessAudit(DeviceAccessChanges.MembershipRemoved));
            Assign(contexts, user, device, viewer.Id);
        }

        using (var db = contexts(new RaceInterceptor(RemoveAndRegrant, rejectSave: false)))
        {
            AssertRejected(db, "DEVICE_MEMBERSHIP_CHANGED", () => new EfDeviceMembershipStore(db).Upsert(
                Member(user, device, editor), viewer.Id, AccessAudit(DeviceAccessChanges.MembershipChanged)));
        }

        using (var db = contexts(new RaceInterceptor(RemoveAndRegrant, rejectSave: false)))
        {
            AssertRejected(db, "DEVICE_MEMBERSHIP_CHANGED", () => new EfDeviceMembershipStore(db).Remove(
                user, device, viewer.Id, AccessAudit(DeviceAccessChanges.MembershipRemoved)));
        }
    }

    [Fact]
    public void Ef_role_update_reports_stale_before_name_taken()
    {
        using var db = InMemoryContexts(nameof(Ef_role_update_reports_stale_before_name_taken))();
        var store = new EfDeviceRoleStore(db);
        var organization = OrganizationId.New();
        var role = DeviceRole.Create(organization, "Operator", null, []);
        store.Add(role, AccessAudit(DeviceAccessChanges.RoleCreated, role.Id));
        store.Add(DeviceRole.Create(organization, "Supervisor", null, []), AccessAudit(DeviceAccessChanges.RoleCreated));
        var current = store.Find(role.Id)!;
        current.Update("Operator", "Second version", []);
        store.Update(current, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id));

        // A copy of version 1 that also renames onto a taken name.
        var stale = DeviceRole.Rehydrate(role.Id.Value, organization.Value, "Operator", null, [], 1);
        stale.Update("Supervisor", null, []);

        AssertCode("DEVICE_ROLE_VERSION_STALE", () =>
            store.Update(stale, AccessAudit(DeviceAccessChanges.RoleUpdated, role.Id)));
    }

    [Fact]
    public void Ef_role_add_lost_to_concurrent_same_name_reports_name_taken()
    {
        var contexts = InMemoryContexts(nameof(Ef_role_add_lost_to_concurrent_same_name_reports_name_taken));
        var organization = OrganizationId.New();
        var race = new RaceInterceptor(() => SeedRole(contexts, organization, "operator"));
        using var db = contexts(race);

        AssertRejected(db, "DEVICE_ROLE_NAME_TAKEN", () => new EfDeviceRoleStore(db).Add(
            DeviceRole.Create(organization, "Operator", null, []), AccessAudit(DeviceAccessChanges.RoleCreated)));
    }

    [Fact]
    public void Ef_role_remove_lost_to_concurrent_assignment_reports_in_use()
    {
        var contexts = InMemoryContexts(nameof(Ef_role_remove_lost_to_concurrent_assignment_reports_in_use));
        var role = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
        var race = new RaceInterceptor(() => Assign(contexts, NewUser(), DeviceId.New(), role.Id));
        using var db = contexts(race);

        AssertRejected(db, "DEVICE_ROLE_IN_USE", () =>
            new EfDeviceRoleStore(db).Remove(role.Id, 1, AccessAudit(DeviceAccessChanges.RoleDeleted, role.Id)));
    }

    [Fact]
    public void Ef_membership_insert_lost_to_concurrent_insert_reports_changed()
    {
        var contexts = InMemoryContexts(nameof(Ef_membership_insert_lost_to_concurrent_insert_reports_changed));
        var role = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
        var user = NewUser();
        var device = DeviceId.New();
        var race = new RaceInterceptor(() => Assign(contexts, user, device, role.Id));
        using var db = contexts(race);

        AssertRejected(db, "DEVICE_MEMBERSHIP_CHANGED", () => new EfDeviceMembershipStore(db).Upsert(
            Member(user, device, role), null, AccessAudit(DeviceAccessChanges.MembershipAssigned)));
    }

    [Fact]
    public void Ef_membership_insert_lost_to_role_delete_reports_role_not_found()
    {
        var contexts = InMemoryContexts(nameof(Ef_membership_insert_lost_to_role_delete_reports_role_not_found));
        var role = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
        var race = new RaceInterceptor(() => DeleteRole(contexts, role.Id));
        using var db = contexts(race);

        AssertRejected(db, "DEVICE_ROLE_NOT_FOUND", () => new EfDeviceMembershipStore(db).Upsert(
            Member(NewUser(), DeviceId.New(), role), null, AccessAudit(DeviceAccessChanges.MembershipAssigned)));
    }

    [Fact]
    public void Ef_membership_change_lost_to_concurrent_change_and_role_delete_reports_role_not_found()
    {
        // The token rejects the stale UPDATE; re-running the checks in order
        // finds the target role gone before the changed membership.
        var contexts = InMemoryContexts(nameof(Ef_membership_change_lost_to_concurrent_change_and_role_delete_reports_role_not_found));
        var viewer = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
        var editor = SeedRole(contexts, OrganizationId.New(), "Editor", "device.edit_basics");
        var target = SeedRole(contexts, OrganizationId.New(), "Target", "device.view");
        var user = NewUser();
        var device = DeviceId.New();
        Assign(contexts, user, device, viewer.Id);
        var race = new RaceInterceptor(() =>
        {
            Assign(contexts, user, device, editor.Id, expectedRoleId: viewer.Id);
            DeleteRole(contexts, target.Id);
        }, rejectSave: false);
        using var db = contexts(race);

        AssertRejected(db, "DEVICE_ROLE_NOT_FOUND", () => new EfDeviceMembershipStore(db).Upsert(
            Member(user, device, target), viewer.Id, AccessAudit(DeviceAccessChanges.MembershipChanged)));
    }

    [Fact]
    public void Ef_unexplained_database_rejection_is_rethrown_and_nothing_stays_tracked()
    {
        // For example a membership on a device that does not exist (foreign
        // key): no store check explains it, so the provider error surfaces.
        var contexts = InMemoryContexts(nameof(Ef_unexplained_database_rejection_is_rethrown_and_nothing_stays_tracked));
        var role = SeedRole(contexts, OrganizationId.New(), "Viewer", "device.view");
        var race = new RaceInterceptor(() => { });
        using var db = contexts(race);

        Assert.Throws<DbUpdateException>(() => new EfDeviceMembershipStore(db).Upsert(
            Member(NewUser(), DeviceId.New(), role), null, AccessAudit(DeviceAccessChanges.MembershipAssigned)));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Migration_purges_legacy_device_action_grants()
    {
        var operations = new AddDeviceRolesAndMemberships().UpOperations;

        Assert.Equal(
            ["device_access_audits", "device_memberships", "device_role_permissions", "device_roles"],
            operations.OfType<CreateTableOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.All(operations, operation => Assert.True(
            operation is CreateTableOperation or CreateIndexOperation or SqlOperation,
            $"Unexpected migration operation {operation.GetType().Name}."));
        var statements = operations.OfType<SqlOperation>().Select(x => x.Sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Contains("UPDATE policy_versions", statements[0], StringComparison.Ordinal);
        Assert.Contains("GREATEST", statements[0], StringComparison.Ordinal);
        Assert.Contains("""DELETE FROM permission_grants WHERE "Kind" = 'device-action'""", statements[1], StringComparison.Ordinal);
        // Both statements run after the tables exist.
        Assert.IsType<SqlOperation>(operations[^2]);
        Assert.IsType<SqlOperation>(operations[^1]);
    }

    [Fact]
    public void Migration_adds_permission_grant_grantor_columns()
    {
        var operations = new AddPermissionGrantGrantor().UpOperations;

        Assert.Equal(2, operations.Count);
        var columns = operations.Select(x => Assert.IsType<AddColumnOperation>(x)).ToDictionary(x => x.Name);
        Assert.All(columns.Values, column => Assert.Equal("permission_grants", column.Table));
        Assert.Equal(typeof(Guid), columns["GrantedBy"].ClrType);
        Assert.True(columns["GrantedBy"].IsNullable);
        Assert.Equal(typeof(bool), columns["GrantedByMasterAdmin"].ClrType);
        Assert.False(columns["GrantedByMasterAdmin"].IsNullable);
        Assert.Equal(false, columns["GrantedByMasterAdmin"].DefaultValue);
    }

    [Fact]
    public void Ef_permission_store_round_trips_grantor()
    {
        using var db = InMemoryContexts(nameof(Ef_permission_store_round_trips_grantor))();
        var store = new EfPermissionStore(db);
        var user = NewUser();
        var organization = OrganizationId.New();
        var legacyOrganization = OrganizationId.New();

        store.GrantFeature(new FeaturePermissionGrant(user, "devices.manage", true, 1, Grantor, true));
        store.GrantResource(new ResourceScopeGrant(user, organization, false, 2, Grantor, true));
        store.GrantFeature(new FeaturePermissionGrant(user, "licenses.manage", false, 3));
        // A row written before the grantor columns existed.
        db.PermissionGrants.Add(new PermissionGrantRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Value,
            Kind = "resource",
            OrganizationId = legacyOrganization.Value,
            PolicyVersion = 4
        });
        db.SaveChanges();

        var features = store.FeatureGrants(user);
        var resources = store.ResourceGrants(user);

        Assert.Contains(new FeaturePermissionGrant(user, "devices.manage", true, 1, Grantor, true), features);
        Assert.Contains(new FeaturePermissionGrant(user, "licenses.manage", false, 3, null, false), features);
        Assert.Contains(new ResourceScopeGrant(user, organization, false, 2, Grantor, true), resources);
        Assert.Contains(new ResourceScopeGrant(user, legacyOrganization, false, 4, null, false), resources);
    }

    [Fact]
    public void Development_registration_resolves_one_in_memory_device_access_store()
    {
        var services = new ServiceCollection();
        services.AddHmsInfrastructure(allowInMemoryDevelopment: true, secretRootConfigured: false);
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var store = Assert.IsType<InMemoryDeviceAccessStore>(first.ServiceProvider.GetRequiredService<IDeviceRoleStore>());

        Assert.Same(store, first.ServiceProvider.GetRequiredService<IDeviceMembershipStore>());
        Assert.Same(store, second.ServiceProvider.GetRequiredService<IDeviceRoleStore>());
    }

    [Fact]
    public void Database_registration_uses_scoped_ef_device_access_stores()
    {
        var services = new ServiceCollection();
        services.AddHmsInfrastructure(
            databaseConnectionString: "Host=localhost;Database=hms_server_design;Username=hms",
            allowInMemoryDevelopment: true,
            secretRootConfigured: false);

        var roles = Assert.Single(services, x => x.ServiceType == typeof(IDeviceRoleStore));
        var memberships = Assert.Single(services, x => x.ServiceType == typeof(IDeviceMembershipStore));

        Assert.Equal(typeof(EfDeviceRoleStore), roles.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, roles.Lifetime);
        Assert.Equal(typeof(EfDeviceMembershipStore), memberships.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, memberships.Lifetime);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(InMemoryDeviceAccessStore));
    }

    [Fact]
    public void Development_registration_resolves_device_access_resolver_and_handlers()
    {
        var services = new ServiceCollection();
        services.AddHmsInfrastructure(allowInMemoryDevelopment: true, secretRootConfigured: false);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<DeviceAccessResolver>(scope.ServiceProvider.GetRequiredService<IDeviceAccessResolver>());
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IDeviceAccessResolver)).Lifetime);
        foreach (var handler in new[]
                 {
                     typeof(CreateDeviceRoleHandler), typeof(UpdateDeviceRoleHandler), typeof(DeleteDeviceRoleHandler),
                     typeof(AssignDeviceMembershipHandler), typeof(RemoveDeviceMembershipHandler)
                 })
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(handler));
            Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == handler).Lifetime);
        }
    }

    [Fact]
    public void Database_registration_resolves_device_access_resolver_and_handlers_with_scope_validation()
    {
        var services = new ServiceCollection();
        services.AddHmsInfrastructure(
            databaseConnectionString: "Host=localhost;Database=hms_server_design;Username=hms",
            allowInMemoryDevelopment: true,
            secretRootConfigured: false);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        foreach (var service in new[]
                 {
                     typeof(IDeviceAccessResolver), typeof(CreateDeviceRoleHandler), typeof(UpdateDeviceRoleHandler),
                     typeof(DeleteDeviceRoleHandler), typeof(AssignDeviceMembershipHandler),
                     typeof(RemoveDeviceMembershipHandler), typeof(GrantFeaturePermissionHandler),
                     typeof(GrantResourceScopeHandler)
                 })
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(service));
    }

    [Fact]
    public void Ef_permission_store_runs_a_user_locked_decision_and_keeps_its_write()
    {
        using var db = InMemoryContexts(nameof(Ef_permission_store_runs_a_user_locked_decision_and_keeps_its_write))();
        var store = new EfPermissionStore(db);
        var user = new UserId(Guid.NewGuid());

        var version = store.WithUserLock(user, () =>
        {
            var next = store.Advance();
            store.GrantFeature(new FeaturePermissionGrant(user, "users.manage", false, next));
            return next;
        });

        Assert.Equal(version, Assert.Single(store.FeatureGrants(user)).PolicyVersion);
    }

    [Fact]
    public void Authorization_epoch_advance_many_increments_each_device_once()
    {
        using var db = InMemoryContexts(nameof(Authorization_epoch_advance_many_increments_each_device_once))();
        var store = new EfAuthorizationEpochStore(db);
        var existing = DeviceId.New();
        var fresh = DeviceId.New();
        var untouched = DeviceId.New();
        store.Advance(existing);
        store.Advance(existing);
        store.Advance(untouched);

        store.AdvanceMany([existing, fresh, fresh]);

        Assert.Equal(3, store.Current(existing));
        Assert.Equal(1, store.Current(fresh));
        Assert.Equal(1, store.Current(untouched));
    }

    private delegate ApplicationDbContext ContextFactory(IInterceptor? interceptor = null);

    private static ContextFactory InMemoryContexts(string name, SaveChangesProbe? probe = null)
    {
        // An explicit root shares one database across every context the test opens.
        var root = new InMemoryDatabaseRoot();
        return interceptor =>
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name, root);
            if (probe is not null)
                builder.AddInterceptors(probe);
            if (interceptor is not null)
                builder.AddInterceptors(interceptor);
            return new ApplicationDbContext(builder.Options);
        };
    }

    private static void AssertRejected(ApplicationDbContext db, string code, Action write)
    {
        AssertCode(code, write);
        // Nothing stays staged for a later SaveChanges on the same scope.
        Assert.Empty(db.ChangeTracker.Entries());
    }

    // The rejected write and its audit row reach the database in one
    // SaveChanges, which PostgreSQL runs in one transaction, so the audit row
    // rolls back with the write. EF InMemory applies rows one by one, so only
    // the single save and the cleared tracker are observable here.
    private static void AssertLostRace(
        ApplicationDbContext stale, SaveChangesProbe probe, string code, string guardedWrite, Action write)
    {
        var saves = probe.Count;

        AssertCode(code, write);

        Assert.Equal(saves + 1, probe.Count);
        Assert.Contains(guardedWrite, probe.LastBatch);
        Assert.Contains("Added DeviceAccessAuditRecord", probe.LastBatch);
        // Nothing stays staged for a later SaveChanges on the same scope.
        Assert.Empty(stale.ChangeTracker.Entries());
    }

    private static IEntityType Entity<TRecord>(ApplicationDbContext db) =>
        db.Model.FindEntityType(typeof(TRecord)) ?? throw new InvalidOperationException($"{typeof(TRecord).Name} is not mapped.");

    private static IIndex? FindIndex(IEntityType entity, params string[] properties) =>
        entity.GetIndexes().SingleOrDefault(index => index.Properties.Select(x => x.Name).SequenceEqual(properties));

    private static void AssertForeignKey(IEntityType entity, string property, Type principal, DeleteBehavior deleteBehavior)
    {
        var foreignKey = Assert.Single(entity.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual([property]));
        Assert.Equal(principal, foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
    }

    private static void AssertColumn(IEntityType entity, string property, int? maxLength, bool nullable)
    {
        var column = entity.FindProperty(property);
        Assert.NotNull(column);
        Assert.Equal(maxLength, column!.GetMaxLength());
        Assert.Equal(nullable, column.IsNullable);
    }

    private static (DeviceRole Viewer, DeviceRole Editor) AddViewerAndEditor(ApplicationDbContext db)
    {
        var organization = OrganizationId.New();
        var viewer = DeviceRole.Create(organization, "Viewer", null, ["device.view"]);
        var editor = DeviceRole.Create(organization, "Editor", null, ["device.view", "device.edit_basics"]);
        var roles = new EfDeviceRoleStore(db);
        roles.Add(viewer, AccessAudit(DeviceAccessChanges.RoleCreated, viewer.Id));
        roles.Add(editor, AccessAudit(DeviceAccessChanges.RoleCreated, editor.Id));
        return (viewer, editor);
    }

    private static DeviceRole SeedRole(
        ContextFactory contexts, OrganizationId organization, string name, params string[] permissions)
    {
        var role = DeviceRole.Create(organization, name, null, permissions);
        using var db = contexts();
        new EfDeviceRoleStore(db).Add(role, AccessAudit(DeviceAccessChanges.RoleCreated, role.Id));
        return role;
    }

    private static void RenameRole(ContextFactory contexts, DeviceRoleId id, string name)
    {
        using var db = contexts();
        var store = new EfDeviceRoleStore(db);
        var role = store.Find(id)!;
        role.Update(name, role.Description, role.Permissions);
        store.Update(role, AccessAudit(DeviceAccessChanges.RoleUpdated, id));
    }

    private static void UpdateRolePermissions(ContextFactory contexts, DeviceRoleId id, params string[] permissions)
    {
        using var db = contexts();
        var store = new EfDeviceRoleStore(db);
        var role = store.Find(id)!;
        role.Update(role.Name, role.Description, permissions);
        store.Update(role, AccessAudit(DeviceAccessChanges.RoleUpdated, id));
    }

    private static void DeleteRole(ContextFactory contexts, DeviceRoleId id)
    {
        using var db = contexts();
        var store = new EfDeviceRoleStore(db);
        store.Remove(id, store.Find(id)!.Version, AccessAudit(DeviceAccessChanges.RoleDeleted, id));
    }

    private static void Assign(
        ContextFactory contexts, UserId user, DeviceId device, DeviceRoleId roleId, DeviceRoleId? expectedRoleId = null)
    {
        using var db = contexts();
        new EfDeviceMembershipStore(db).Upsert(
            new DeviceMembership(user, device, roleId, Grantor, GrantedAt),
            expectedRoleId,
            AccessAudit(expectedRoleId is null
                ? DeviceAccessChanges.MembershipAssigned
                : DeviceAccessChanges.MembershipChanged));
    }

    private static UserId NewUser() => new(Guid.NewGuid());

    private static DeviceMembership Member(UserId user, DeviceId device, DeviceRole role) =>
        new(user, device, role.Id, Grantor, GrantedAt);

    private static DeviceAccessAuditEntry AccessAudit(
        string change,
        DeviceRoleId? roleId = null,
        DeviceId? deviceId = null,
        UserId? targetUserId = null) =>
        new(Guid.NewGuid(), Grantor, change, null, deviceId, targetUserId, roleId, null, null, GrantedAt);

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<InvalidOperationException>(action).Message);

    // Commits the competing write once, after the store's load and checks.
    // With rejectSave, the save then fails as PostgreSQL would fail it on a
    // unique index or foreign key; without it, EF's own concurrency check runs.
    private sealed class RaceInterceptor(Action competingWrite, bool rejectSave = true) : SaveChangesInterceptor
    {
        private bool _fired;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (_fired)
                return result;
            _fired = true;
            competingWrite();
            if (rejectSave)
                throw new DbUpdateException("Rejected by a database constraint.");
            return result;
        }
    }

    // Counts SaveChanges calls and records what the last one tried to write.
    private sealed class SaveChangesProbe : SaveChangesInterceptor
    {
        public int Count { get; private set; }
        public IReadOnlyList<string> LastBatch { get; private set; } = [];

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Count++;
            LastBatch = eventData.Context!.ChangeTracker.Entries()
                .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(x => $"{x.State} {x.Metadata.ClrType.Name}")
                .ToArray();
            return result;
        }
    }
}
