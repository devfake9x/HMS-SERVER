using System.Security.Claims;
using Hms.Server.Application.Realtime;
using Hms.Server.Api.Authorization;
using Hms.Server.Domain.Devices;
using Microsoft.AspNetCore.SignalR;

namespace Hms.Server.Api.Hubs;

/// <summary>
/// Drains authorized application events into per-connection SignalR backlogs.
/// A failed client is retried in isolation and never causes already-delivered
/// events to be replayed to other connections.
/// </summary>
public sealed class SignalRDeviceEventDeliveryService(
    IAuthorizedEventPublisher publisher,
    IHubContext<DeviceEventsHub> hub,
    SignalRSubscriptionRegistry subscriptions,
    IServiceScopeFactory scopes,
    ILogger<SignalRDeviceEventDeliveryService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SendDeadline = TimeSpan.FromSeconds(2);
    private const int MaxSendAttempts = 3;
    private readonly SignalRConnectionBacklog _backlogs = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var epochs = scope.ServiceProvider.GetRequiredService<IAuthorizationEpochStore>();
                var devices = scope.ServiceProvider.GetRequiredService<Hms.Server.Application.Management.IDeviceStore>();
                var organizations = scope.ServiceProvider.GetRequiredService<Hms.Server.Application.Management.IOrganizationStore>();
                var permissions = scope.ServiceProvider.GetRequiredService<Hms.Server.Application.Authorization.IPermissionStore>();
                var principalRefresher = scope.ServiceProvider.GetRequiredService<IRealtimePrincipalRefresher>();

                await ReconcileSubscriptionsAsync(epochs, devices, organizations, permissions, principalRefresher, stoppingToken);
                foreach (var deviceEvent in publisher.DrainForDelivery(epochs))
                    await QueueAuthorizedEventAsync(deviceEvent, epochs, devices, organizations, permissions, principalRefresher, stoppingToken);

                await DrainConnectionBacklogsAsync(epochs, devices, organizations, permissions, principalRefresher, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SignalR device event delivery failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task QueueAuthorizedEventAsync(
        AuthorizedDeviceEvent deviceEvent,
        IAuthorizationEpochStore epochs,
        Hms.Server.Application.Management.IDeviceStore devices,
        Hms.Server.Application.Management.IOrganizationStore organizations,
        Hms.Server.Application.Authorization.IPermissionStore permissions,
        IRealtimePrincipalRefresher principalRefresher,
        CancellationToken cancellationToken)
    {
        var device = devices.Find(deviceEvent.DeviceId);
        if (!RealtimeEventFence.IsCurrent(deviceEvent, epochs, permissions.PolicyVersion))
            return;

        foreach (var originalSubscription in subscriptions.ForDevice(deviceEvent.DeviceId))
        {
            var subscription = await RefreshSubscriptionAsync(originalSubscription, principalRefresher, cancellationToken);
            if (subscription is null)
                continue;

            var authorized = device is not null &&
                device.State != Hms.Server.Domain.Devices.DeviceLifecycleState.Revoked &&
                epochs.Current(deviceEvent.DeviceId) == subscription.AuthorizationEpoch &&
                ScopeAuthorization.CanAccessDevice(subscription.Principal, device, organizations, permissions);
            if (!authorized)
            {
                await RevokeAndRemoveAsync(subscription, epochs, permissions, cancellationToken);
                continue;
            }

            if (!_backlogs.TryEnqueue(subscription.ConnectionId, deviceEvent))
                await RevokeAndRemoveAsync(subscription, epochs, permissions, cancellationToken, "REALTIME_RESYNC_REQUIRED");
        }
    }

    private async Task DrainConnectionBacklogsAsync(
        IAuthorizationEpochStore epochs,
        Hms.Server.Application.Management.IDeviceStore devices,
        Hms.Server.Application.Management.IOrganizationStore organizations,
        Hms.Server.Application.Authorization.IPermissionStore permissions,
        IRealtimePrincipalRefresher principalRefresher,
        CancellationToken cancellationToken)
    {
        foreach (var connectionId in _backlogs.Connections)
        {
            if (!_backlogs.TryPeek(connectionId, out var pending) || pending is null)
            {
                _backlogs.Remove(connectionId);
                continue;
            }

            if (!TryGetConnectionSubscription(connectionId, pending.Event.DeviceId, out var originalSubscription))
            {
                _backlogs.RemoveDevice(connectionId, pending.Event.DeviceId);
                continue;
            }

            var subscription = await RefreshSubscriptionAsync(originalSubscription!, principalRefresher, cancellationToken);
            if (subscription is null)
            {
                _backlogs.Remove(connectionId);
                continue;
            }

            if (_backlogs.RequiresResynchronization(connectionId, pending.Event.DeviceId))
            {
                await RevokeAndRemoveAsync(subscription, epochs, permissions, cancellationToken, "REALTIME_RESYNC_REQUIRED");
                continue;
            }

            var device = devices.Find(subscription.DeviceId);
            if (device is null ||
                device.State == Hms.Server.Domain.Devices.DeviceLifecycleState.Revoked ||
                epochs.Current(subscription.DeviceId) != subscription.AuthorizationEpoch ||
                !ScopeAuthorization.CanAccessDevice(subscription.Principal, device, organizations, permissions) ||
                !RealtimeEventFence.IsCurrent(pending.Event, epochs, permissions.PolicyVersion))
            {
                await RevokeAndRemoveAsync(subscription, epochs, permissions, cancellationToken);
                continue;
            }

            try
            {
                await SendWithDeadlineAsync(connectionId, "DeviceEvent", pending.Event, cancellationToken);
                _backlogs.MarkDelivered(connectionId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Unable to deliver SignalR device event to {ConnectionId}.", connectionId);
                if (!_backlogs.MarkRetry(connectionId, MaxSendAttempts))
                    await RevokeAndRemoveAsync(subscription, epochs, permissions, cancellationToken, "REALTIME_RESYNC_REQUIRED");
            }
        }
    }

    private bool TryGetConnectionSubscription(string connectionId, DeviceId deviceId, out DeviceSubscription? subscription)
    {
        subscription = subscriptions.Snapshot().FirstOrDefault(x =>
            x.ConnectionId == connectionId && x.DeviceId == deviceId);
        return subscription is not null;
    }

    private async Task ReconcileSubscriptionsAsync(
        IAuthorizationEpochStore epochs,
        Hms.Server.Application.Management.IDeviceStore devices,
        Hms.Server.Application.Management.IOrganizationStore organizations,
        Hms.Server.Application.Authorization.IPermissionStore permissions,
        IRealtimePrincipalRefresher principalRefresher,
        CancellationToken cancellationToken)
    {
        foreach (var originalSubscription in subscriptions.Snapshot())
        {
            var refreshed = await RefreshSubscriptionAsync(originalSubscription, principalRefresher, cancellationToken);
            if (refreshed is null)
                continue;

            var device = devices.Find(refreshed.DeviceId);
            var authorized = device is not null &&
                device.State != Hms.Server.Domain.Devices.DeviceLifecycleState.Revoked &&
                epochs.Current(refreshed.DeviceId) == refreshed.AuthorizationEpoch &&
                ScopeAuthorization.CanAccessDevice(refreshed.Principal, device, organizations, permissions);
            if (!authorized)
                await RevokeAndRemoveAsync(refreshed, epochs, permissions, cancellationToken);
        }
    }

    private async Task<DeviceSubscription?> RefreshSubscriptionAsync(
        DeviceSubscription subscription,
        IRealtimePrincipalRefresher principalRefresher,
        CancellationToken cancellationToken)
    {
        ClaimsPrincipal? refreshed;
        try
        {
            refreshed = await principalRefresher.RefreshAsync(subscription.Principal, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to refresh SignalR principal for {ConnectionId}.", subscription.ConnectionId);
            refreshed = null;
        }
        if (refreshed is null)
        {
            _backlogs.Remove(subscription.ConnectionId);
            await RemoveMembershipFailClosedAsync(subscription);
            return null;
        }

        subscriptions.RefreshPrincipal(subscription.ConnectionId, subscription.DeviceId, refreshed);
        return subscription with { Principal = refreshed };
    }

    private async Task RevokeAndRemoveAsync(
        DeviceSubscription subscription,
        IAuthorizationEpochStore epochs,
        Hms.Server.Application.Authorization.IPermissionStore permissions,
        CancellationToken cancellationToken,
        string reason = "AUTHORIZATION_REVOKED")
    {
        try
        {
            await SendWithDeadlineAsync(
                subscription.ConnectionId,
                "DeviceRevoked",
                new DeviceRevokedNotification(
                    subscription.DeviceId.Value,
                    epochs.Current(subscription.DeviceId),
                    permissions.PolicyVersion,
                    reason),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to notify revoked SignalR connection {ConnectionId}.", subscription.ConnectionId);
        }
        finally
        {
            // A connection may be subscribed to several devices. Removing one
            // revoked device must not discard queued events for its siblings.
            _backlogs.RemoveDevice(subscription.ConnectionId, subscription.DeviceId);
            await RemoveMembershipFailClosedAsync(subscription);
        }
    }

    private async Task SendWithDeadlineAsync(
        string connectionId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SendDeadline);
        await hub.Clients.Client(connectionId).SendAsync(method, payload, deadline.Token);
    }

    private async Task RemoveMembershipFailClosedAsync(DeviceSubscription subscription)
    {
        try
        {
            await hub.Groups.RemoveFromGroupAsync(
                subscription.ConnectionId,
                DeviceEventsHub.GroupName(subscription.DeviceId.Value));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Registry removal remains authoritative if the adapter is already gone.
            logger.LogWarning(exception, "Unable to remove SignalR group membership for {ConnectionId}.", subscription.ConnectionId);
        }
        finally
        {
            subscriptions.Remove(subscription.ConnectionId, subscription.DeviceId);
        }
    }
}
