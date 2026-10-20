using System.Collections.Frozen;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

public sealed class CommandBoundaryTests
{
    private static readonly UserId TestActor = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private readonly InMemoryDeviceAccessStore _access = new();
    private readonly DeviceAccessResolver _resolver;

    public CommandBoundaryTests() => _resolver = new DeviceAccessResolver(_access, _access);

    [Fact]
    public async Task Authorized_command_is_recorded_without_claiming_delivery()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, transport, audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));

        var result = await handler.HandleAsync(
            Request(device, "system", "get_info"),
            Member(device, "device.read"));

        Assert.Equal(CommandExecutionStatus.Recorded, result.Status);
        Assert.Equal("COMMAND_RECORDED", result.Code);
        Assert.Single(transport.Records);
        Assert.Single(audit.Records);
        Assert.DoesNotContain(audit.Records, x => x.Result.Status is CommandExecutionStatus.Published or CommandExecutionStatus.Executed);
    }

    [Fact]
    public async Task Deferred_transport_never_claims_publish_or_execution()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, new DeferredDeviceCommandTransport(), audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));

        var result = await handler.HandleAsync(
            Request(device, "system", "get_info"),
            Member(device, "device.read"));

        Assert.Equal(CommandExecutionStatus.MqttDeferred, result.Status);
        Assert.Equal("MQTT_DEFERRED", result.Code);
        Assert.DoesNotContain(audit.Records, x => x.Result.Status is CommandExecutionStatus.Published or CommandExecutionStatus.Acknowledged or CommandExecutionStatus.Executed);
    }

    [Fact]
    public async Task Missing_action_contract_is_denied_before_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, transport, audit, new CommandDefinition(
            "system", "dangerous_unknown", "device.write", CommandAvailability.DeniedUntilActionContract, true, false, null));

        var result = await handler.HandleAsync(
            Request(device, "system", "dangerous_unknown"),
            Member(device, "device.write"));

        Assert.Equal(CommandExecutionStatus.ContractUnavailable, result.Status);
        Assert.Equal("DENIED_UNTIL_ACTION_CONTRACT", result.Code);
        Assert.Empty(transport.Records);
        Assert.Empty(audit.Records);
    }

    [Fact]
    public async Task Missing_device_permission_is_denied_before_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));

        var result = await handler.HandleAsync(
            Request(device, "system", "get_info"),
            Member(device, "device.write"));

        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal("DEVICE_PERMISSION_REQUIRED", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Command_actor_mismatch_fails_closed_before_transport_or_audit()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, transport, audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info") with { UserId = new UserId(Guid.NewGuid()) };

        var result = await handler.HandleAsync(
            request,
            Member(device, "device.read"));

        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal("COMMAND_ACTOR_MISMATCH", result.Code);
        Assert.Empty(transport.Records);
        Assert.Empty(audit.Records);
    }

    [Fact]
    public async Task Future_command_timestamp_is_rejected()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));

        var request = Request(device, "system", "get_info") with { RequestedAt = DateTimeOffset.UtcNow.AddMinutes(2) };
        var result = await handler.HandleAsync(request, Member(device, "device.read"));

        Assert.Equal("COMMAND_TIMESTAMP_IN_FUTURE", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Same_correlation_reuses_the_recorded_result()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, transport, audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info");
        var authorization = Member(device, "device.read");

        var first = await handler.HandleAsync(request, authorization);
        var second = await handler.HandleAsync(
            request with { RequestedAt = request.RequestedAt.AddSeconds(1) },
            authorization);

        Assert.Equal(first, second);
        Assert.Single(transport.Records);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task Cached_result_is_not_returned_after_device_is_revoked()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, new RecordingDeviceCommandTransport(), audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info");
        var authorization = Member(device, "device.read");

        _ = await handler.HandleAsync(request, authorization);
        device.Revoke();

        var result = await handler.HandleAsync(request, authorization);

        Assert.Equal("DEVICE_REVOKED", result.Code);
    }

    [Fact]
    public async Task Cached_result_is_not_returned_after_membership_is_removed()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, new RecordingDeviceCommandTransport(), audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info");
        var authorization = Member(device, "device.read");

        _ = await handler.HandleAsync(request, authorization);
        _access.Remove(TestActor, device.Id, _access.Find(TestActor, device.Id)!.RoleId,
            Audit(DeviceAccessChanges.MembershipRemoved));

        var result = await handler.HandleAsync(request, authorization);

        Assert.Equal("DEVICE_NOT_FOUND", result.Code);
    }

    [Fact]
    public async Task Command_on_revoked_device_returns_device_revoked_for_member()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        device.Revoke();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));

        var result = await handler.HandleAsync(Request(device, "system", "get_info"), Member(device, "device.read"));

        Assert.Equal("DEVICE_REVOKED", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Cached_result_is_not_returned_from_an_untrusted_catalog()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var audit = new InMemoryCommandAuditStore();
        var catalog = new InMemoryCommandCatalog(
            [new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000)],
            new CommandCatalogMetadata("test", "hash", IsTrusted: true));
        var handler = new SendDeviceCommandHandler(
            catalog,
            new Hms.Server.Infrastructure.Commands.HmsIdfProvisionalProtocolAdapter(),
            new RecordingDeviceCommandTransport(),
            audit,
            id => id == device.Id ? device : null,
            _resolver);
        var request = Request(device, "system", "get_info");
        var authorization = Member(device, "device.read");

        _ = await handler.HandleAsync(request, authorization);
        var untrustedHandler = new SendDeviceCommandHandler(
            new InMemoryCommandCatalog(
                [new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000)],
                new CommandCatalogMetadata("test", "hash", IsTrusted: false)),
            new Hms.Server.Infrastructure.Commands.HmsIdfProvisionalProtocolAdapter(),
            new RecordingDeviceCommandTransport(),
            audit,
            id => id == device.Id ? device : null,
            _resolver);

        var result = await untrustedHandler.HandleAsync(request, authorization);

        Assert.Equal("CATALOG_UNTRUSTED", result.Code);
    }

    [Fact]
    public async Task Stale_client_catalog_is_rejected_before_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var catalog = new InMemoryCommandCatalog(
            [new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000)],
            new CommandCatalogMetadata("current", "current-hash"));
        var handler = new SendDeviceCommandHandler(
            catalog,
            new Hms.Server.Infrastructure.Commands.HmsIdfProvisionalProtocolAdapter(),
            transport,
            new InMemoryCommandAuditStore(),
            id => id == device.Id ? device : null,
            _resolver);
        var request = Request(device, "system", "get_info") with
        {
            CatalogVersion = "old",
            CatalogHash = "old-hash"
        };

        var result = await handler.HandleAsync(
            request,
            Member(device, "device.read"));

        Assert.Equal("CATALOG_STALE", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Missing_catalog_metadata_is_rejected_before_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info") with
        {
            CatalogVersion = null,
            CatalogHash = null
        };

        var result = await handler.HandleAsync(
            request,
            Member(device, "device.read"));

        Assert.Equal("CATALOG_STALE", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Partial_catalog_metadata_is_rejected_before_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info") with
        {
            CatalogHash = null
        };

        var result = await handler.HandleAsync(
            request,
            Member(device, "device.read"));

        Assert.Equal("CATALOG_STALE", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Blank_catalog_metadata_is_rejected_before_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info") with
        {
            CatalogVersion = " ",
            CatalogHash = "current-hash"
        };

        var result = await handler.HandleAsync(
            request,
            Member(device, "device.read"));

        Assert.Equal("CATALOG_STALE", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Same_correlation_with_different_command_is_rejected()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var audit = new InMemoryCommandAuditStore();
        var handler = new SendDeviceCommandHandler(
            new InMemoryCommandCatalog([
                new CommandDefinition("system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000),
                new CommandDefinition("system", "restart", "device.write", CommandAvailability.Enabled, true, false, 120_000)
            ]),
            new Hms.Server.Infrastructure.Commands.HmsIdfProvisionalProtocolAdapter(),
            transport,
            audit,
            id => id == device.Id ? device : null,
            _resolver);
        var authorization = Member(device, "device.read", "device.write");
        var correlationId = Guid.NewGuid().ToString("N");
        var first = Request(device, "system", "get_info") with { CorrelationId = correlationId };
        var second = Request(device, "system", "restart") with
        {
            CorrelationId = correlationId,
            RequestedAt = first.RequestedAt
        };

        _ = await handler.HandleAsync(first, authorization);
        var result = await handler.HandleAsync(second, authorization);

        Assert.Equal("CORRELATION_ID_REUSED", result.Code);
        Assert.Single(transport.Records);
    }

    [Fact]
    public void Request_fingerprint_canonicalizes_nested_object_order()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        var correlationId = Guid.NewGuid().ToString("N");
        var first = Request(device, "system", "get_info") with
        {
            CorrelationId = correlationId,
            Payload = new Dictionary<string, object?>
            {
                ["nested"] = new Dictionary<string, object?>
                {
                    ["z"] = 1,
                    ["a"] = 2
                }
            }
        };
        var second = first with
        {
            Payload = new Dictionary<string, object?>
            {
                ["nested"] = new Dictionary<string, object?>
                {
                    ["a"] = 2,
                    ["z"] = 1
                }
            }
        };

        Assert.Equal(
            CommandRequestFingerprint.Compute(first),
            CommandRequestFingerprint.Compute(second));
    }

    [Fact]
    public async Task Concurrent_same_correlation_claims_transport_once()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new BlockingRecordingTransport();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, transport, audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info");
        var authorization = Member(device, "device.read");

        var firstTask = handler.HandleAsync(request, authorization).AsTask();
        // Bounded, so a request that never reaches the transport fails the test instead of hanging it.
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await handler.HandleAsync(request, authorization);
        transport.Release.TrySetResult(true);
        var first = await firstTask;

        Assert.Equal("IDEMPOTENCY_IN_PROGRESS", second.Code);
        Assert.Equal(CommandExecutionStatus.Recorded, first.Status);
        Assert.Single(transport.Records);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task Stale_in_flight_reservation_fails_closed_without_retrying_transport()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var now = DateTimeOffset.UtcNow;
        var clock = now;
        var audit = new InMemoryCommandAuditStore(
            TimeSpan.FromMinutes(5),
            () => clock);
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info") with { RequestedAt = now };
        var authorization = Member(device, "device.read");
        var definition = new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000);
        var plan = new CommandExecutionPlan(request, definition, device.KeyGeneration, now.AddMinutes(2));

        var claimed = await audit.ReserveAsync(plan, CommandRequestFingerprint.Compute(request));
        Assert.Equal(CommandAuditReservationStatus.Claimed, claimed.Status);

        clock = now.AddMinutes(6);
        var result = await handler.HandleAsync(request, authorization);

        Assert.Equal("IDEMPOTENCY_OUTCOME_UNKNOWN", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Actor_without_scope_or_membership_gets_device_not_found()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var outsider = new AccessActor(TestActor, false, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);

        var result = await handler.HandleAsync(Request(device, "system", "get_info"), outsider);

        Assert.Equal("DEVICE_NOT_FOUND", result.Code);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Master_admin_bypasses_membership_but_not_revocation()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var transport = new RecordingDeviceCommandTransport();
        var handler = CreateHandler(device, transport, new InMemoryCommandAuditStore(), new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var master = new AccessActor(TestActor, true, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);

        var allowed = await handler.HandleAsync(Request(device, "system", "get_info"), master);
        device.Revoke();
        var revoked = await handler.HandleAsync(Request(device, "system", "get_info"), master);

        Assert.Equal("COMMAND_RECORDED", allowed.Code);
        // Revocation is a device state, not a deny, so it stops a master-admin too.
        Assert.Equal("DEVICE_REVOKED", revoked.Code);
        Assert.Single(transport.Records);
    }

    [Fact]
    public async Task Retrying_after_key_rotation_is_fenced()
    {
        var organization = Organization.Create("Master");
        var device = Device.Create("ESP-001", organization.Id);
        device.Activate();
        var audit = new InMemoryCommandAuditStore();
        var handler = CreateHandler(device, new DeferredDeviceCommandTransport(), audit, new CommandDefinition(
            "system", "get_info", "device.read", CommandAvailability.Enabled, false, true, 120_000));
        var request = Request(device, "system", "get_info");
        var authorization = Member(device, "device.read");

        _ = await handler.HandleAsync(request, authorization);
        device.RotateKeyGeneration();

        var result = await handler.HandleAsync(request, authorization);

        Assert.Equal("KEY_GENERATION_FENCE", result.Code);
    }

    private SendDeviceCommandHandler CreateHandler(
        Device device,
        IDeviceCommandTransport transport,
        ICommandAuditStore audit,
        CommandDefinition definition) => new(
            new InMemoryCommandCatalog([definition]),
            new Hms.Server.Infrastructure.Commands.HmsIdfProvisionalProtocolAdapter(),
            transport,
            audit,
            id => id == device.Id ? device : null,
            _resolver);

    private static DeviceCommandRequest Request(Device device, string type, string action) => new(
        device.Id,
        TestActor,
        type,
        action,
        new Dictionary<string, object?>(),
        Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow,
        CatalogVersion: InMemoryCommandCatalog.ProvisionalCatalogVersion,
        CatalogHash: InMemoryCommandCatalog.ProvisionalCatalogHash);

    // The test actor with a membership on the device whose role holds the permissions,
    // and no other scope.
    private AccessActor Member(Device device, params string[] permissions)
    {
        var role = DeviceRole.Create(device.OwnerOrganizationId, $"role-{Guid.NewGuid():N}", null, permissions);
        _access.Add(role, Audit(DeviceAccessChanges.RoleCreated));
        var current = _access.Find(TestActor, device.Id)?.RoleId;
        _access.Upsert(new DeviceMembership(TestActor, device.Id, role.Id, TestActor, DateTimeOffset.UtcNow), current,
            Audit(current is null ? DeviceAccessChanges.MembershipAssigned : DeviceAccessChanges.MembershipChanged));
        return new AccessActor(TestActor, false, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);
    }

    private static DeviceAccessAuditEntry Audit(string change) =>
        new(Guid.NewGuid(), TestActor, change, null, null, null, null, null, null, DateTimeOffset.UtcNow);

    private sealed class BlockingRecordingTransport : IDeviceCommandTransport
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(CommandExecutionPlan Plan, PreparedDeviceCommand Prepared)> Records { get; } = [];

        public async ValueTask<CommandExecutionResult> SendAsync(
            CommandExecutionPlan plan,
            PreparedDeviceCommand prepared,
            CancellationToken cancellationToken = default)
        {
            Records.Add((plan, prepared));
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new CommandExecutionResult(CommandExecutionStatus.Recorded, "COMMAND_RECORDED", plan.Request.CorrelationId);
        }
    }
}
