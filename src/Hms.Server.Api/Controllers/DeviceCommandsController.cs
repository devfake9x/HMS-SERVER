using Hms.Server.Api.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices/{deviceId:guid}/commands")]
public sealed class DeviceCommandsController(
    SendDeviceCommandHandler handler,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CommandExecutionResult>> Send(
        Guid deviceId,
        [FromBody] SendCommandRequest request,
        CancellationToken cancellationToken)
    {
        var actor = AccessActorFactory.FromPrincipal(User, organizations, permissions);
        if (actor is null)
        {
            return Forbid();
        }

        var command = new DeviceCommandRequest(
            new DeviceId(deviceId),
            actor.UserId,
            request.Type,
            request.Action,
            request.Payload ?? new Dictionary<string, object?>(),
            request.CorrelationId ?? Guid.NewGuid().ToString("N"),
            request.RequestedAt ?? DateTimeOffset.UtcNow,
            request.DryRun,
            request.CatalogVersion,
            request.CatalogHash,
            request.Confirm);

        return Ok(await handler.HandleAsync(command, actor, cancellationToken));
    }
}

public sealed record SendCommandRequest(
    string Type,
    string Action,
    IReadOnlyDictionary<string, object?>? Payload = null,
    string? CorrelationId = null,
    DateTimeOffset? RequestedAt = null,
    bool DryRun = false,
    string? CatalogVersion = null,
    string? CatalogHash = null,
    string? Confirm = null);
