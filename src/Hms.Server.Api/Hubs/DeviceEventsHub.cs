using System.Security.Claims;
using Hms.Server.Application.Realtime;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Devices;
using Hms.Server.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hms.Server.Api.Hubs;

[Authorize]
public sealed class DeviceEventsHub(
    IAuthorizationEpochStore epochs,
    IDeviceStore devices,
    IOrganizationStore organizations,
    Hms.Server.Application.Authorization.IPermissionStore permissions,
    SignalRSubscriptionRegistry subscriptions,
    IRealtimePrincipalRefresher principalRefresher) : Hub
{
    public async Task JoinDevice(Guid deviceId, long authorizationEpoch)
    {
        var principal = await RefreshPrincipalAsync();
        EnsureAuthorized(principal, deviceId, authorizationEpoch);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(deviceId));
        subscriptions.Register(Context.ConnectionId, principal, new DeviceId(deviceId), authorizationEpoch);
    }

    public async Task RevalidateDevice(Guid deviceId, long authorizationEpoch)
    {
        try
        {
            var principal = await RefreshPrincipalAsync();
            EnsureAuthorized(principal, deviceId, authorizationEpoch);
            // Re-establish the actual SignalR group before publishing the
            // authorization record. This keeps registry state from claiming
            // membership if a reconnect has lost its group assignment.
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(deviceId));
            subscriptions.Register(Context.ConnectionId, principal, new DeviceId(deviceId), authorizationEpoch);
        }
        catch
        {
            subscriptions.Remove(Context.ConnectionId, new DeviceId(deviceId));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(deviceId));
            throw new HubException("DEVICE_REAUTHORIZATION_REQUIRED");
        }

        // Keep a successful revalidation idempotent while ensuring a stale
        // group membership cannot survive a failed authorization check.
        await Task.CompletedTask;
    }

    public async Task LeaveDevice(Guid deviceId)
    {
        subscriptions.Remove(Context.ConnectionId, new DeviceId(deviceId));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(deviceId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(Guid deviceId) => $"device:{deviceId:N}";

    private async Task<ClaimsPrincipal> RefreshPrincipalAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
            throw new HubException("AUTHENTICATION_REQUIRED");

        var refreshed = await principalRefresher.RefreshAsync(Context.User);
        return refreshed ?? throw new HubException("AUTHENTICATION_REQUIRED");
    }

    private void EnsureAuthorized(ClaimsPrincipal principal, Guid deviceId, long authorizationEpoch)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new HubException("AUTHENTICATION_REQUIRED");

        var device = devices.Find(new DeviceId(deviceId));
        if (device is null)
            throw new HubException("DEVICE_NOT_FOUND");
        if (!ScopeAuthorization.CanAccessDevice(principal, device, organizations, permissions))
            throw new HubException("DEVICE_SCOPE_DENIED");
        if (device.State == DeviceLifecycleState.Revoked)
            throw new HubException("DEVICE_REVOKED");

        var current = epochs.Current(new DeviceId(deviceId));
        if (authorizationEpoch != current)
            throw new HubException("STALE_AUTHORIZATION_EPOCH");
    }
}
