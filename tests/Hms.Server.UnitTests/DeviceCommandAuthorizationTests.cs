using System.Collections.Frozen;
using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Commands;

namespace Hms.Server.UnitTests;

// Commands authorized by the permission the pinned catalog (spec §11) assigns
// to each (type, action), against an actor whose only scope is a membership.
public sealed class DeviceCommandAuthorizationTests
{
    private readonly DeviceAccessTestFixture _world = new();
    private readonly InMemoryCommandCatalog _catalog;
    private readonly RecordingDeviceCommandTransport _transport = new();
    private readonly SendDeviceCommandHandler _handler;

    public DeviceCommandAuthorizationTests()
    {
        _catalog = new InMemoryCommandCatalog(
            _world.Catalog.List().Select(x => x with { Availability = CommandAvailability.Enabled }));
        _handler = new SendDeviceCommandHandler(
            _catalog,
            new HmsIdfProvisionalProtocolAdapter(),
            _transport,
            new InMemoryCommandAuditStore(),
            id => _world.Devices.Find(id),
            _world.Resolver);
    }

    [Theory]
    [InlineData("mqtt")]
    [InlineData("sms")]
    [InlineData("battery")]
    [InlineData("ota")]
    [InlineData("control")]
    public async Task Adc_view_role_cannot_run_get_status_of_other_namespaces(string type)
    {
        Assert.NotEqual("adc.view", _catalog.Find(type, "get_status")!.RequiredPermission);
        var actor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");

        var result = await Send(actor, type, "get_status");

        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, result.Code);
        Assert.Empty(_transport.Records);
    }

    [Fact]
    public async Task Adc_view_role_cannot_run_mqtt_get_config()
    {
        var actor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");

        var result = await Send(actor, "mqtt", "get_config");

        Assert.Equal(DeviceAccessCodes.PermissionRequired, result.Code);
        Assert.Empty(_transport.Records);
    }

    [Fact]
    public async Task Adc_view_role_runs_adc_get_status()
    {
        var actor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");

        var result = await Send(actor, "adc", "get_status");

        Assert.Equal("COMMAND_RECORDED", result.Code);
        Assert.Single(_transport.Records);
    }

    [Fact]
    public async Task Config_get_resolves_view_permission_from_json_string_domain()
    {
        var actor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");

        var allowed = await Send(actor, "config", "get", Payload(JsonDocument.Parse("\"adc\"").RootElement));
        var otherDomain = await Send(actor, "config", "get", Payload(JsonDocument.Parse("\"mqtt\"").RootElement));

        Assert.Equal("COMMAND_RECORDED", allowed.Code);
        Assert.Equal(DeviceAccessCodes.PermissionRequired, otherDomain.Code);
    }

    [Fact]
    public async Task Config_set_requires_edit_permission_of_domain()
    {
        var viewer = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");
        var editor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.config_edit");

        var denied = await Send(viewer, "config", "set", Payload("adc"));
        var allowed = await Send(editor, "config", "set", Payload("adc"));

        Assert.Equal(DeviceAccessCodes.PermissionRequired, denied.Code);
        Assert.Equal("COMMAND_RECORDED", allowed.Code);
    }

    public static TheoryData<object?> UnmappedDomains => new()
    {
        null,
        "ADC",
        42,
        "nope"
    };

    [Theory]
    [MemberData(nameof(UnmappedDomains))]
    public async Task Config_command_with_unmapped_domain_is_permission_unmapped(object? domain)
    {
        var actor = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var payload = domain is null ? new Dictionary<string, object?>() : Payload(domain);

        var result = await Send(actor, "config", "get", payload);

        Assert.Equal(CommandExecutionStatus.ContractUnavailable, result.Status);
        Assert.Equal("PERMISSION_UNMAPPED", result.Code);
        Assert.Empty(_transport.Records);
    }

    public static TheoryData<string, string, string?> BlankConfirmations => new()
    {
        { "system", "reboot", null },
        { "system", "reboot", "  " },
        { "system", "factory_reset", null },
        { "system", "factory_reset", "  " },
        { "ota", "apply", null },
        { "ota", "apply", "  " }
    };

    [Theory]
    [MemberData(nameof(BlankConfirmations))]
    public async Task Confirm_required_rows_reject_blank_confirm(string type, string action, string? confirm)
    {
        var actor = _world.MasterAdmin(new UserId(Guid.NewGuid()));

        var result = await Send(actor, type, action, confirm: confirm);

        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal("CONFIRM_REQUIRED", result.Code);
        Assert.Empty(_transport.Records);
    }

    [Fact]
    public async Task Confirm_required_rows_accept_non_blank_confirm()
    {
        var actor = _world.MasterAdmin(new UserId(Guid.NewGuid()));

        var result = await Send(actor, "system", "reboot", confirm: "line-1");

        Assert.Equal("COMMAND_RECORDED", result.Code);
    }

    [Fact]
    public async Task Replay_of_a_confirmed_command_without_confirm_still_requires_confirm()
    {
        var actor = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var request = Request(actor, "system", "reboot", confirm: "line-1");
        Assert.Equal("COMMAND_RECORDED", (await _handler.HandleAsync(request, actor)).Code);

        // Confirm is outside the fingerprint, so the replay finds the cached result.
        var replay = await _handler.HandleAsync(request with { Confirm = null }, actor);

        Assert.Equal("CONFIRM_REQUIRED", replay.Code);
        Assert.Single(_transport.Records);
    }

    [Fact]
    public async Task Out_of_scope_device_reads_like_a_missing_device_before_catalog_checks()
    {
        var actor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");
        var staleOnForeign = Request(actor, "adc", "get_status") with
        {
            DeviceId = _world.OtherDevice.Id,
            CatalogHash = "stale"
        };
        var staleOnMissing = staleOnForeign with { DeviceId = DeviceId.New(), CorrelationId = Guid.NewGuid().ToString("N") };
        var unknownOnForeign = Request(actor, "nope", "nothing") with { DeviceId = _world.OtherDevice.Id };

        Assert.Equal(DeviceAccessCodes.DeviceNotFound, (await _handler.HandleAsync(staleOnForeign, actor)).Code);
        Assert.Equal(DeviceAccessCodes.DeviceNotFound, (await _handler.HandleAsync(staleOnMissing, actor)).Code);
        Assert.Equal(DeviceAccessCodes.DeviceNotFound, (await _handler.HandleAsync(unknownOnForeign, actor)).Code);
        Assert.Empty(_transport.Records);
    }

    [Fact]
    public void Only_three_rows_require_confirmation()
    {
        Assert.Equal(
            ["ota.apply", "system.factory_reset", "system.reboot"],
            _catalog.List().Where(x => x.ConfirmTemplate is not null).Select(x => $"{x.Type}.{x.Action}")
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Catalog_ttl_60000_rows_plan_sixty_second_expiry()
    {
        var actor = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var rows = _catalog.List().Where(x => x.DefaultTtlMs == 60_000).ToArray();

        foreach (var row in rows)
        {
            var result = await Send(actor, row.Type, row.Action,
                row.PermissionByDomain is null ? null : Payload("adc"),
                row.ConfirmTemplate is null ? null : "line-1");
            Assert.Equal("COMMAND_RECORDED", result.Code);
        }

        Assert.Equal(24, rows.Length);
        Assert.Equal(24, _transport.Records.Count);
        Assert.All(_transport.Records, x =>
            Assert.Equal(TimeSpan.FromSeconds(60), x.Plan.ExpiresAt - x.Plan.Request.RequestedAt));
    }

    [Fact]
    public async Task Rows_without_ttl_use_dispatcher_default()
    {
        var catalog = new InMemoryCommandCatalog([
            new CommandDefinition("adc", "get_status", "adc.view", CommandAvailability.Enabled, false, true, null)
        ]);
        var handler = new SendDeviceCommandHandler(
            catalog, new HmsIdfProvisionalProtocolAdapter(), _transport, new InMemoryCommandAuditStore(),
            id => _world.Devices.Find(id), _world.Resolver);
        var actor = _world.Member(new UserId(Guid.NewGuid()), _world.Device, "adc.view");

        var result = await handler.HandleAsync(Request(actor, "adc", "get_status"), actor);

        Assert.Equal("COMMAND_RECORDED", result.Code);
        var plan = Assert.Single(_transport.Records).Plan;
        Assert.Equal(TimeSpan.FromMilliseconds(CommandDefinition.DispatcherDefaultTtlMs), plan.ExpiresAt - plan.Request.RequestedAt);
    }

    [Fact]
    public void Confirm_is_not_part_of_request_fingerprint()
    {
        var actor = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var request = Request(actor, "system", "reboot");

        Assert.Equal(
            CommandRequestFingerprint.Compute(request),
            CommandRequestFingerprint.Compute(request with { Confirm = "line-1" }));
    }

    [Fact]
    public async Task Removed_then_reassigned_membership_runs_command_again()
    {
        var master = _world.MasterAdmin(new UserId(Guid.NewGuid()));
        var user = new UserId(Guid.NewGuid());
        _world.Users.Add(user, _world.Child.Id);
        var role = _world.SeedRole(_world.Child.Id, "adc.view");
        var member = new AccessActor(user, false, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);

        Assert.Equal("OK", _world.Assign().Handle(master, _world.Device.Id, user, role.Id, null, role.Version).Code);
        Assert.Equal("COMMAND_RECORDED", (await Send(member, "adc", "get_status")).Code);

        Assert.Equal("OK", _world.Remove().Handle(master, _world.Device.Id, user, role.Id).Code);
        Assert.Equal(DeviceAccessCodes.DeviceNotFound, (await Send(member, "adc", "get_status")).Code);

        Assert.Equal("OK", _world.Assign().Handle(master, _world.Device.Id, user, role.Id, null, role.Version).Code);
        Assert.Equal("COMMAND_RECORDED", (await Send(member, "adc", "get_status")).Code);
    }

    private ValueTask<CommandExecutionResult> Send(
        AccessActor actor,
        string type,
        string action,
        IReadOnlyDictionary<string, object?>? payload = null,
        string? confirm = null) =>
        _handler.HandleAsync(Request(actor, type, action, payload, confirm), actor);

    private DeviceCommandRequest Request(
        AccessActor actor,
        string type,
        string action,
        IReadOnlyDictionary<string, object?>? payload = null,
        string? confirm = null) => new(
        _world.Device.Id,
        actor.UserId,
        type,
        action,
        payload ?? new Dictionary<string, object?>(),
        Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow,
        CatalogVersion: InMemoryCommandCatalog.ProvisionalCatalogVersion,
        CatalogHash: InMemoryCommandCatalog.ProvisionalCatalogHash,
        Confirm: confirm);

    private static Dictionary<string, object?> Payload(object domain) => new() { ["domain"] = domain };
}
