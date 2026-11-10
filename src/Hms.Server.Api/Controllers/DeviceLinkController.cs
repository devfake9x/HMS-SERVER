using Hms.Server.Api.Authorization;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Application.Management;
using Hms.Server.Application.Provisioning;
using Hms.Server.Domain.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

/// <summary>How the server reaches one device over MQTT: its topic base, command key state and
/// what the broker last heard from it.</summary>
[ApiController]
[Authorize]
[Route("api/v1/devices/{deviceId:guid}/link")]
public sealed class DeviceLinkController(
    IDeviceStore devices,
    IOrganizationStore organizations,
    IPermissionStore permissions,
    IDeviceMqttBindingStore bindings,
    IDeviceLinkStateStore linkState,
    IDeviceLinkTransportStatus transport,
    IProvisioningSecretGenerator secrets,
    IDeviceAccessResolver accessResolver) : ControllerBase
{
    // Heartbeat period is 3 s (spec §2); a few missed beats mean the device is not reachable now.
    private static readonly TimeSpan HeartbeatFreshness = TimeSpan.FromSeconds(15);
    private const string OutputWritePermission = "gpio.do_write";
    private const string OutputViewPermission = "gpio.do_view";

    // The same device-role check the command handler applies, so the UI shows what will pass.
    private Domain.Authorization.DeviceAccess? ResolveAccess(Device device) =>
        DeviceAccessChecks.Resolve(User, device, organizations, permissions, accessResolver);

    /// <summary>Relay output state from the last verified <c>control.get_status</c> reply. Needs
    /// <c>gpio.do_view</c> on the device, like the reply itself. Channel fields are what the device
    /// reported; null when it did not.</summary>
    [HttpGet("~/api/v1/devices/{deviceId:guid}/outputs")]
    public ActionResult<DeviceOutputsDto> Outputs(Guid deviceId, [FromServices] IDeviceOutputStateStore outputs)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null || device.State == DeviceLifecycleState.Revoked ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!DeviceAccessChecks.Allows(ResolveAccess(device), device, OutputViewPermission))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "DEVICE_PERMISSION_REQUIRED" });

        var state = outputs.Get(device.Id);
        if (state is null)
            return NotFound(new { code = "OUTPUTS_NOT_READ" });
        Response.Headers.CacheControl = "no-store";
        return Ok(new DeviceOutputsDto(
            state.Channels.Select(x => new OutputChannelDto(
                x.Channel, x.State, x.Mode, x.PulseActive, x.Enabled, x.GpioOk, x.Forced, x.Owner, x.OwnerId)).ToArray(),
            state.ReadAt, state.IsStale, state.StaleReason));
    }

    [HttpGet]
    public ActionResult<DeviceLinkDto> Get(Guid deviceId)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null || device.State == DeviceLifecycleState.Revoked ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });

        var binding = bindings.Find(device.Id);
        var snapshot = linkState.Get(device.Id);
        var heartbeatFresh = snapshot?.LastHeartbeatAt is { } beat && DateTimeOffset.UtcNow - beat <= HeartbeatFreshness;
        var access = ResolveAccess(device);
        Response.Headers.CacheControl = "no-store";
        return Ok(new DeviceLinkDto(
            binding is null ? null : new DeviceMqttBindingDto(binding.TopicPrefix, binding.MqttDeviceId, binding.KeyVersion),
            device.KeyGeneration,
            secrets.Find(device.Id, device.KeyGeneration) is not null,
            snapshot?.Connectivity,
            snapshot?.ConnectivityAt,
            snapshot?.LastHeartbeatAt,
            Online: heartbeatFresh && snapshot?.Connectivity != "offline",
            snapshot?.CapabilitiesAt,
            snapshot?.CapabilityCount ?? 0,
            transport.Current.Enabled && transport.Current.Connected,
            binding?.AllowedOutputChannels ?? [],
            DeviceAccessChecks.Allows(access, device, OutputWritePermission),
            DeviceAccessChecks.Allows(access, device, OutputViewPermission)));
    }

    /// <summary>Sets the output channels the server may drive (empty = none). Master-admin only:
    /// this list is the interlock between a command and a real relay.</summary>
    [HttpPut("output-channels")]
    [ValidateAntiForgeryToken]
    public IActionResult SetOutputChannels(
        Guid deviceId,
        [FromBody] SetOutputChannelsRequest request,
        [FromServices] SetDeviceOutputChannelsHandler handler,
        [FromServices] ILogger<DeviceLinkController> logger)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null || !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole))
            return Forbid();

        try
        {
            var change = handler.Handle(device.Id, request.Channels ?? []);
            logger.LogWarning(
                "Output channel allowlist of device {DeviceId} changed by {User}: [{Before}] -> [{After}]",
                device.Id.Value, User.Identity?.Name, string.Join(',', change.Before ?? []), string.Join(',', change.After));
            return Ok(new { allowedOutputChannels = change.After });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message.Split(' ')[0] });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { code = ex.Message });
        }
    }

    [HttpPut]
    [ValidateAntiForgeryToken]
    public ActionResult<DeviceMqttBindingDto> Configure(
        Guid deviceId,
        [FromBody] ConfigureDeviceMqttBindingRequest request,
        [FromServices] ConfigureDeviceMqttBindingHandler handler)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null || !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "devices.manage", permissions))
            return Forbid();

        try
        {
            var binding = handler.Handle(device.Id, request.TopicPrefix, request.MqttDeviceId, request.KeyVersion);
            return Ok(new DeviceMqttBindingDto(binding.TopicPrefix, binding.MqttDeviceId, binding.KeyVersion));
        }
        catch (ArgumentException ex)
        {
            // The handler's code is the message before the " (Parameter ...)" suffix.
            return BadRequest(new { code = ex.Message.Split(' ')[0] });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { code = ex.Message });
        }
    }

    /// <summary>Registers the command key already provisioned onto the device. Master-admin only:
    /// the key gives owner rights on the device (spec R11). The key is never returned.</summary>
    [HttpPut("command-key")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4 * 1024)]
    public IActionResult ImportCommandKey(
        Guid deviceId,
        [FromBody] ImportCommandKeyRequest request,
        [FromServices] ImportDeviceCommandKeyHandler handler)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null || !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!User.IsInRole(RoleAuthorizationPolicy.MasterAdminRole))
            return Forbid();

        try
        {
            var generation = handler.Handle(device.Id, request.ValueHex);
            return Ok(new { keyGeneration = generation });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { code = "COMMAND_KEY_INVALID" });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { code = ex.Message });
        }
    }

    /// <summary>The device's retained capabilities document (spec §2).</summary>
    [HttpGet("capabilities")]
    public IActionResult Capabilities(Guid deviceId)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null || device.State == DeviceLifecycleState.Revoked ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });

        var capabilities = linkState.GetCapabilities(device.Id);
        if (capabilities is null)
            return NotFound(new { code = "CAPABILITIES_NOT_RECEIVED" });
        Response.Headers.CacheControl = "no-store";
        return Content(capabilities.RawJson, "application/json");
    }
}

public sealed record DeviceMqttBindingDto(string TopicPrefix, string MqttDeviceId, uint? KeyVersion);

public sealed record DeviceLinkDto(
    DeviceMqttBindingDto? Binding,
    int KeyGeneration,
    bool CommandKeyProvisioned,
    string? Connectivity,
    DateTimeOffset? ConnectivityAt,
    DateTimeOffset? LastHeartbeatAt,
    bool Online,
    DateTimeOffset? CapabilitiesAt,
    int CapabilityCount,
    bool BrokerConnected,
    IReadOnlyList<int> AllowedOutputChannels,
    bool CanControlOutputs,
    bool CanViewOutputs);

public sealed record OutputChannelDto(
    int Channel, bool State, string? Mode, bool? PulseActive,
    bool? Enabled, bool? GpioOk, bool? Forced, string? Owner, string? OwnerId);

public sealed record DeviceOutputsDto(
    IReadOnlyList<OutputChannelDto> Channels,
    DateTimeOffset ReadAt,
    bool Stale,
    string? StaleReason);

public sealed record SetOutputChannelsRequest(IReadOnlyList<int>? Channels);

public sealed record ConfigureDeviceMqttBindingRequest(string TopicPrefix, string MqttDeviceId, uint? KeyVersion = null);

public sealed record ImportCommandKeyRequest(string ValueHex);
