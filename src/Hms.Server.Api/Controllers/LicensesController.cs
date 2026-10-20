using Hms.Server.Application.Licensing;
using Hms.Server.Domain.Devices;
using Hms.Server.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Hms.Server.Api.Authorization;
using Hms.Server.Application.Management;
using System.ComponentModel.DataAnnotations;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices/{deviceId:guid}/licenses")]
public sealed class LicensesController(
    ApplyLicenseHandler handler,
    IDeviceStore devices,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpPost("apply")]
    [RequestSizeLimit(64 * 1024)]
    [ValidateAntiForgeryToken]
    public ActionResult<LicenseApplyResult> Apply(Guid deviceId, [FromBody] LicenseApplyRequestBody body)
    {
        var device = devices.Find(new DeviceId(deviceId));
        if (device is null)
            return NotFound(new { code = "DEVICE_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "licenses.manage", permissions) ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return Forbid();

        try
        {
            var result = handler.Handle(new LicenseApplyRequest(
                new DeviceId(deviceId),
                body.Kid,
                body.Issuer,
                body.Generation,
                body.RawEnvelope));
            if (result.State == LicenseLifecycleState.Rejected)
                return BadRequest(result);
            if (result.State == LicenseLifecycleState.RecoveryRequired)
                return Conflict(result);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = ex.Message });
        }
    }
}

public sealed record LicenseApplyRequestBody(
    [property: Required, StringLength(128)]
    string Kid,
    [property: Required, StringLength(128)]
    string Issuer,
    [property: Range(1, int.MaxValue)]
    int Generation,
    [property: Required, MaxLength(2048)]
    byte[] RawEnvelope);
