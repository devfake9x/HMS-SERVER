using System.Collections.Concurrent;
using System.Security.Claims;
using Hms.Server.Api.Hubs;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Hms.Server.UnitTests;

public sealed class DeviceEventsHubTests
{
    [Fact]
    public async Task Revalidate_adds_group_before_registering_subscription()
    {
        var device = Device.Create("sensor", OrganizationId.New());
        var devices = new InMemoryDeviceStore();
        devices.Add(device);
        var organizations = new InMemoryOrganizationStore();
        organizations.Add(Organization.Rehydrate(device.OwnerOrganizationId.Value, "org", null));
        var epochs = new InMemoryAuthorizationEpochStore();
        var epoch = epochs.Advance(device.Id);
        var groups = new RecordingGroups();
        var registry = new SignalRSubscriptionRegistry();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("org-scope", device.OwnerOrganizationId.Value.ToString())], "test"));
        var hub = new DeviceEventsHub(epochs, devices, organizations, new InMemoryPermissionStore(),
            registry, new StaticRefresher(principal))
        {
            Context = new TestContext("connection-1", principal),
            Groups = groups
        };

        await hub.RevalidateDevice(device.Id.Value, epoch);

        Assert.Equal(["add:connection-1:" + DeviceEventsHub.GroupName(device.Id.Value)], groups.Calls);
        Assert.True(registry.TryGet("connection-1", device.Id, out _));
    }

    private sealed class StaticRefresher(ClaimsPrincipal principal) : IRealtimePrincipalRefresher
    {
        public ValueTask<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal _, CancellationToken __ = default) =>
            ValueTask.FromResult<ClaimsPrincipal?>(principal);
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public List<string> Calls { get; } = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken _) {
            Calls.Add($"add:{connectionId}:{groupName}");
            return Task.CompletedTask;
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken _) {
            Calls.Add($"remove:{connectionId}:{groupName}");
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext(string connectionId, ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User => user;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override IDictionary<object, object?> Items { get; } = new ConcurrentDictionary<object, object?>();
        public override void Abort() { }
    }
}
