using Hms.Server.Application.Provisioning;
using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Hms.Server.Api.Authorization;
using System.ComponentModel.DataAnnotations;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices/{deviceId:guid}/provisioning-bundles")]
public sealed class ProvisioningController(
    CreateProvisioningBundleHandler handler,
    IProvisioningBundleStore bundles,
    IDeviceStore devices,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    [ValidateAntiForgeryToken]
    public ActionResult<ProvisioningBundleMetadata> Create(Guid deviceId, [FromBody] CreateProvisioningRequest request)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "provisioning.manage", permissions) ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        try
        {
            var metadata = handler.Handle(new DeviceId(deviceId), request.BrokerHost, request.BrokerPort, request.BrokerMode);
            return Accepted(metadata);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = ex.Message });
        }
    }

    [HttpPost("{bundleId:guid}/download")]
    [ValidateAntiForgeryToken]
    public IActionResult Download(Guid deviceId, Guid bundleId)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "provisioning.manage", permissions) ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        var bundle = bundles.Consume(new DeviceId(deviceId), bundleId);
        if (bundle is null)
        {
            return NotFound(new { code = "BUNDLE_NOT_FOUND_OR_USED" });
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return File(JsonSerializer.SerializeToUtf8Bytes(bundle), "application/json", $"hms-{deviceId:N}.bundle.json");
    }
}

public sealed record CreateProvisioningRequest(
    [param: Required, StringLength(253)]
    string BrokerHost,
    [param: Range(1, 65535)]
    int BrokerPort = 1883,
    [param: Required, StringLength(32)]
    string BrokerMode = "PRIMARY_ONLY");
