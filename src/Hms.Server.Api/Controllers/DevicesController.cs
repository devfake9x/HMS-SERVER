using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices")]
public sealed class DevicesController(
    CreateDeviceHandler handler,
    UpdateDeviceHandler updater,
    IDeviceStore store,
    RevokeDeviceHandler revoke,
    IOrganizationStore organizations,
    IPermissionStore permissions,
    Hms.Server.Application.Realtime.IAuthorizationEpochStore authorizationEpochs,
    Hms.Server.Application.Realtime.IDeviceTelemetryStore telemetryStore,
    Hms.Server.Application.Realtime.IAuthorizedEventPublisher eventPublisher) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<DeviceDto>> List(
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? packageId,
        [FromQuery] Guid? areaId,
        [FromQuery] DeviceLifecycleState? state,
        [FromQuery] string? q)
    {
        var scopedOrganizations = ScopeAuthorization.EffectiveOrganizations(User, organizations, permissions);
        var search = q?.Trim();
        var devices = store.List().Where(device =>
            scopedOrganizations.Contains(device.OwnerOrganizationId) &&
            (!organizationId.HasValue || device.OwnerOrganizationId.Value == organizationId.Value) &&
            (!projectId.HasValue || device.ProjectId == projectId) &&
            (!packageId.HasValue || device.PackageId == packageId) &&
            (!areaId.HasValue || device.AreaId == areaId) &&
            (!state.HasValue || device.State == state.Value) &&
            (string.IsNullOrWhiteSpace(search) ||
             device.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             device.Id.Value.ToString("D").Contains(search, StringComparison.OrdinalIgnoreCase)));
        return Ok(devices.Select(CreateDeviceHandler.ToDto).ToArray());
    }

    [HttpPut("{deviceId:guid}")]
    [ValidateAntiForgeryToken]
    public ActionResult<DeviceDto> Update(Guid deviceId, UpdateDeviceRequest request)
    {
        var item = store.Find(new DeviceId(deviceId));
        if (item is null) return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "devices.manage", permissions) ||
            !ScopeAuthorization.CanAccessDevice(User, item, organizations, permissions)) return Forbid();
        try { return Ok(updater.Handle(item.Id, request)); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message }); }
    }

    [HttpGet("{deviceId:guid}/realtime-authorization")]
    public ActionResult<RealtimeAuthorizationDto> RealtimeAuthorization(Guid deviceId)
    {
        var device = store.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (device.State == DeviceLifecycleState.Revoked ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        Response.Headers.CacheControl = "no-store";
        return Ok(new RealtimeAuthorizationDto(
            device.Id.Value,
            authorizationEpochs.Current(device.Id),
            permissions.PolicyVersion));
    }

    [HttpGet("{deviceId:guid}/telemetry")]
    public ActionResult<Hms.Server.Application.Realtime.DeviceTelemetryDto> GetTelemetry(Guid deviceId)
    {
        var device = store.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (device.State == DeviceLifecycleState.Revoked ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        var telemetry = telemetryStore.GetLatest(device.Id);
        if (telemetry is null)
            return NotFound(new { code = "TELEMETRY_NOT_FOUND" });

        return Ok(telemetry);
    }

    [HttpPost("{deviceId:guid}/telemetry")]
    public async Task<IActionResult> IngestTelemetry(
        Guid deviceId,
        [FromBody] Dictionary<string, object?> data,
        CancellationToken cancellationToken)
    {
        var device = store.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (device.State == DeviceLifecycleState.Revoked)
            return Forbid();
        if (!User.IsInRole("master-admin") && !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        if (device.State == DeviceLifecycleState.Provisioning)
        {
            device.Activate();
            store.Update(device);
        }

        telemetryStore.Record(device.Id, data);

        var epoch = authorizationEpochs.Current(device.Id);
        await eventPublisher.PublishAsync(
            new Hms.Server.Application.Realtime.AuthorizedDeviceEvent(
                device.Id,
                epoch,
                permissions.PolicyVersion,
                "status",
                data),
            cancellationToken);

        return Ok(new { success = true, deviceId, state = device.State.ToString() });
    }

    [HttpPost("{deviceId:guid}/revoke")]
    [ValidateAntiForgeryToken]
    public IActionResult Revoke(Guid deviceId)
    {
        var device = store.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "devices.manage", permissions) ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        try
        {
            revoke.Handle(device.Id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { code = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public ActionResult<DeviceDto> Create(CreateDeviceRequest request)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "devices.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, request.OwnerOrganizationId, organizations, permissions))
            return Forbid();

        try
        {
            return CreatedAtAction(nameof(List), handler.Handle(request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = ex.Message });
        }
    }
}

public sealed record RealtimeAuthorizationDto(
    Guid DeviceId,
    long AuthorizationEpoch,
    long PolicyVersion);
