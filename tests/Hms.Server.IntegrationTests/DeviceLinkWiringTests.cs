using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Infrastructure;
using Hms.Server.Infrastructure.Commands;
using Hms.Server.Infrastructure.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hms.Server.IntegrationTests;

/// <summary>The reboot gate lives in a tracker that the transport, the reply and heartbeat handlers
/// and the rebind and revoke handlers all share. Unit tests build those by hand, so only the real
/// registration shows that none of them was left with its own tracker or none.</summary>
public sealed class DeviceLinkWiringTests
{
    private static readonly CommandDefinition Reboot = new PinnedCommandCatalog().Find("system", "reboot")!;

    private static ServiceProvider Build(IMqttCommandPublisher? publisher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Default options leave the broker session disabled: nothing here connects anywhere.
        services.AddOptions<MqttLinkOptions>();
        services.AddHmsInfrastructure(enableLiveMqtt: true, allowInMemoryDevelopment: true);
        if (publisher is not null)
            services.AddSingleton(publisher);
        // A scoped service captured by a singleton (the tracker, the registry) would pass without this.
        // ValidateOnBuild is left off: it trips on unrelated ASP.NET services (no endpoint data source here).
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task The_live_registration_gives_every_reboot_participant_the_one_tracker()
    {
        await using var provider = Build();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var transport = first.ServiceProvider.GetRequiredService<IDeviceCommandTransport>();
        Assert.IsType<MqttDeviceCommandTransport>(transport);
        // The real link is the publisher and is not connected: the transport stops at the link check.
        var plan = Plan(dryRun: false);
        var refused = await transport.SendAsync(plan, Prepared(plan));
        Assert.Equal("MQTT_NOT_CONNECTED", refused.Code);

        // Every one of these needs the tracker to be built; resolving them is the wiring check.
        foreach (var scope in new[] { first, second })
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeviceCommandResponseCorrelator>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeviceInboundMessageHandler>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ConfigureDeviceMqttBindingHandler>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<RevokeDeviceHandler>());
        }

        Assert.Same(
            first.ServiceProvider.GetRequiredService<IDeviceRebootTracker>(),
            second.ServiceProvider.GetRequiredService<IDeviceRebootTracker>());
    }

    [Fact]
    public async Task The_resolved_transport_refuses_a_reboot_while_the_shared_tracker_holds_the_device()
    {
        await using var provider = Build(new ConnectedPublisher());
        using var scope = provider.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IDeviceCommandTransport>();
        Assert.IsType<MqttDeviceCommandTransport>(transport);
        var first = Plan(dryRun: false);
        // A different scope's view of the tracker: the hold must be visible across scopes.
        using (var other = provider.CreateScope())
            Assert.Null(other.ServiceProvider.GetRequiredService<IDeviceRebootTracker>().Admit(first, DateTimeOffset.UtcNow));

        var second = Plan(dryRun: false, first.Request.DeviceId);
        var result = await transport.SendAsync(second, Prepared(second));

        Assert.Equal(CommandExecutionStatus.ContractUnavailable, result.Status);
        Assert.Equal(DeviceRebootCodes.InProgress, result.Code);
    }

    private static CommandExecutionPlan Plan(bool dryRun, DeviceId? device = null)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new DeviceCommandRequest(device ?? DeviceId.New(), new UserId(Guid.NewGuid()), "system", "reboot",
            new Dictionary<string, object?>(), Guid.NewGuid().ToString("N"), now, DryRun: dryRun);
        return new CommandExecutionPlan(request, Reboot, 1, now.AddSeconds(10));
    }

    private static PreparedDeviceCommand Prepared(CommandExecutionPlan plan) =>
        new(plan.Request.DeviceId, "lab/dev/cmd/system", "{}"u8.ToArray(), 1, true);

    private sealed class ConnectedPublisher : IMqttCommandPublisher
    {
        public bool IsConnected => true;

        public ValueTask<string?> PublishCommandAsync(string commandTopic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }
}
