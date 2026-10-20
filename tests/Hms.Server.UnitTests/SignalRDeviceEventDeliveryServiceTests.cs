using System.Collections.Concurrent;
using System.Security.Claims;
using Hms.Server.Api.Hubs;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Server.UnitTests;

public sealed class SignalRDeviceEventDeliveryServiceTests
{
    [Fact]
    public async Task Failed_connection_is_retried_without_replaying_to_a_successful_connection()
    {
        var device = Device.Create("sensor", OrganizationId.New());
        var devices = new InMemoryDeviceStore();
        devices.Add(device);
        var organizations = new InMemoryOrganizationStore();
        organizations.Add(Organization.Rehydrate(device.OwnerOrganizationId.Value, "org", null));
        var epochs = new InMemoryAuthorizationEpochStore();
        var epoch = epochs.Advance(device.Id);
        var permissions = new InMemoryPermissionStore();
        var publisher = new BoundedDeviceEventPublisher(capacity: 8);
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("org-scope", device.OwnerOrganizationId.Value.ToString())], "test"));
        registry.Register("failed", principal, device.Id, epoch);
        registry.Register("successful", principal, device.Id, epoch);

        var clients = new RecordingHubClients();
        clients.GetClient("failed").FailuresRemaining = 1;
        var hub = new RecordingHubContext(clients);
        using var provider = new ServiceCollection()
            .AddSingleton<IAuthorizationEpochStore>(epochs)
            .AddSingleton<IDeviceStore>(devices)
            .AddSingleton<IOrganizationStore>(organizations)
            .AddSingleton<IPermissionStore>(permissions)
            .AddSingleton<IRealtimePrincipalRefresher>(new StaticPrincipalRefresher(principal))
            .BuildServiceProvider();
        var service = new SignalRDeviceEventDeliveryService(
            publisher,
            hub,
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRDeviceEventDeliveryService>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StartAsync(cancellation.Token);
        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            device.Id, epoch, permissions.PolicyVersion, "command",
            new Dictionary<string, object?> { ["id"] = "event-1" }));

        await EventuallyAsync(() => clients.GetClient("successful").Sent.Count == 1, cancellation.Token);
        Assert.Empty(clients.GetClient("failed").Sent);

        await EventuallyAsync(() => clients.GetClient("failed").Sent.Count == 1, cancellation.Token);
        Assert.Single(clients.GetClient("successful").Sent);
        Assert.Equal("DeviceEvent", clients.GetClient("successful").Sent[0].Method);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Principal_refresh_exception_removes_membership_fail_closed()
    {
        var device = Device.Create("sensor", OrganizationId.New());
        var devices = new InMemoryDeviceStore();
        devices.Add(device);
        var organizations = new InMemoryOrganizationStore();
        organizations.Add(Organization.Rehydrate(device.OwnerOrganizationId.Value, "org", null));
        var epochs = new InMemoryAuthorizationEpochStore();
        var epoch = epochs.Advance(device.Id);
        var permissions = new InMemoryPermissionStore();
        var publisher = new BoundedDeviceEventPublisher(capacity: 8);
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("org-scope", device.OwnerOrganizationId.Value.ToString())], "test"));
        registry.Register("connection", principal, device.Id, epoch);
        var clients = new RecordingHubClients();
        using var provider = new ServiceCollection()
            .AddSingleton<IAuthorizationEpochStore>(epochs)
            .AddSingleton<IDeviceStore>(devices)
            .AddSingleton<IOrganizationStore>(organizations)
            .AddSingleton<IPermissionStore>(permissions)
            .AddSingleton<IRealtimePrincipalRefresher>(new ThrowingPrincipalRefresher())
            .BuildServiceProvider();
        var service = new SignalRDeviceEventDeliveryService(
            publisher,
            new RecordingHubContext(clients),
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRDeviceEventDeliveryService>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StartAsync(cancellation.Token);
        await EventuallyAsync(() => registry.Count == 0, cancellation.Token);
        Assert.Empty(clients.GetClient("connection").Sent);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Revoking_one_subscription_preserves_sibling_device_backlog()
    {
        var organization = OrganizationId.New();
        var first = Device.Create("first", organization);
        var second = Device.Create("second", organization);
        var devices = new InMemoryDeviceStore();
        devices.Add(first);
        devices.Add(second);
        var organizations = new InMemoryOrganizationStore();
        organizations.Add(Organization.Rehydrate(organization.Value, "org", null));
        var epochs = new InMemoryAuthorizationEpochStore();
        var firstEpoch = epochs.Advance(first.Id);
        var secondEpoch = epochs.Advance(second.Id);
        var permissions = new InMemoryPermissionStore();
        var publisher = new BoundedDeviceEventPublisher(capacity: 8);
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("org-scope", organization.Value.ToString())], "test"));
        registry.Register("shared", principal, first.Id, firstEpoch);
        registry.Register("shared", principal, second.Id, secondEpoch);

        var clients = new RecordingHubClients();
        clients.GetClient("shared").FailuresRemaining = 3;
        using var provider = new ServiceCollection()
            .AddSingleton<IAuthorizationEpochStore>(epochs)
            .AddSingleton<IDeviceStore>(devices)
            .AddSingleton<IOrganizationStore>(organizations)
            .AddSingleton<IPermissionStore>(permissions)
            .AddSingleton<IRealtimePrincipalRefresher>(new StaticPrincipalRefresher(principal))
            .BuildServiceProvider();
        var service = new SignalRDeviceEventDeliveryService(
            publisher,
            new RecordingHubContext(clients),
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRDeviceEventDeliveryService>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await service.StartAsync(cancellation.Token);
        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            first.Id, firstEpoch, permissions.PolicyVersion, "command",
            new Dictionary<string, object?> { ["id"] = "first" }));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            second.Id, secondEpoch, permissions.PolicyVersion, "command",
            new Dictionary<string, object?> { ["id"] = "second" }));

        await EventuallyAsync(() => clients.GetClient("shared").Sent.Any(x =>
            x.Method == "DeviceEvent" &&
            x.Args.Length == 1 &&
            x.Args[0] is AuthorizedDeviceEvent item && item.DeviceId == second.Id), cancellation.Token);

        Assert.Contains(clients.GetClient("shared").Sent, x => x.Method == "DeviceRevoked");
        Assert.Contains(registry.Snapshot(), x => x.DeviceId == second.Id);
        Assert.DoesNotContain(registry.Snapshot(), x => x.DeviceId == first.Id);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Leaving_head_device_does_not_drop_sibling_device_backlog()
    {
        var organization = OrganizationId.New();
        var first = Device.Create("first", organization);
        var second = Device.Create("second", organization);
        var devices = new InMemoryDeviceStore();
        devices.Add(first);
        devices.Add(second);
        var organizations = new InMemoryOrganizationStore();
        organizations.Add(Organization.Rehydrate(organization.Value, "org", null));
        var epochs = new InMemoryAuthorizationEpochStore();
        var firstEpoch = epochs.Advance(first.Id);
        var secondEpoch = epochs.Advance(second.Id);
        var permissions = new InMemoryPermissionStore();
        var publisher = new BoundedDeviceEventPublisher(capacity: 8);
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("org-scope", organization.Value.ToString())], "test"));
        registry.Register("shared", principal, first.Id, firstEpoch);
        registry.Register("shared", principal, second.Id, secondEpoch);

        var clients = new RecordingHubClients();
        clients.GetClient("shared").FailuresRemaining = 1;
        using var provider = new ServiceCollection()
            .AddSingleton<IAuthorizationEpochStore>(epochs)
            .AddSingleton<IDeviceStore>(devices)
            .AddSingleton<IOrganizationStore>(organizations)
            .AddSingleton<IPermissionStore>(permissions)
            .AddSingleton<IRealtimePrincipalRefresher>(new StaticPrincipalRefresher(principal))
            .BuildServiceProvider();
        var service = new SignalRDeviceEventDeliveryService(
            publisher,
            new RecordingHubContext(clients),
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRDeviceEventDeliveryService>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await service.StartAsync(cancellation.Token);
        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            first.Id, firstEpoch, permissions.PolicyVersion, "command",
            new Dictionary<string, object?> { ["id"] = "first" }));
        await publisher.PublishAsync(new AuthorizedDeviceEvent(
            second.Id, secondEpoch, permissions.PolicyVersion, "command",
            new Dictionary<string, object?> { ["id"] = "second" }));

        await clients.GetClient("shared").FirstFailure.Task.WaitAsync(cancellation.Token);
        registry.Remove("shared", first.Id);

        await EventuallyAsync(() => clients.GetClient("shared").Sent.Any(x =>
            x.Method == "DeviceEvent" &&
            x.Args.Length == 1 &&
            x.Args[0] is AuthorizedDeviceEvent item && item.DeviceId == second.Id), cancellation.Token);

        Assert.DoesNotContain(clients.GetClient("shared").Sent, x =>
            x.Method == "DeviceEvent" &&
            x.Args.Length == 1 &&
            x.Args[0] is AuthorizedDeviceEvent item && item.DeviceId == first.Id);
        await service.StopAsync(CancellationToken.None);
    }

    private static async Task EventuallyAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken);
        }
    }

    private sealed class StaticPrincipalRefresher(ClaimsPrincipal principal) : IRealtimePrincipalRefresher
    {
        public ValueTask<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal _, CancellationToken __ = default) =>
            ValueTask.FromResult<ClaimsPrincipal?>(principal);
    }

    private sealed class ThrowingPrincipalRefresher : IRealtimePrincipalRefresher
    {
        public ValueTask<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal _, CancellationToken __ = default) =>
            throw new InvalidOperationException("refresh failed");
    }

    private sealed class RecordingHubContext(RecordingHubClients clients) : IHubContext<DeviceEventsHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = new RecordingGroups();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly ConcurrentDictionary<string, RecordingClientProxy> _clients = new();

        public RecordingClientProxy GetClient(string connectionId) => _clients.GetOrAdd(connectionId, _ => new RecordingClientProxy());
        public IClientProxy Client(string connectionId) => GetClient(connectionId);
        public IClientProxy All => Client("all");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Client("all-except");
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Client("clients");
        public IClientProxy Group(string groupName) => Client("group:" + groupName);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Client("group-except:" + groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Client("groups");
        public IClientProxy User(string userId) => Client("user:" + userId);
        public IClientProxy Users(IReadOnlyList<string> userIds) => Client("users");

    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public int FailuresRemaining { get; set; }
        public TaskCompletionSource<bool> FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(string Method, object?[] Args)> Sent { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                FirstFailure.TrySetResult(true);
                throw new InvalidOperationException("client unavailable");
            }
            Sent.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
