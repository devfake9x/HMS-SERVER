using Hms.Server.Api.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.DeviceLink;
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

    /// <summary>A command's latest outcome and, for the user who sent it, the device's reply data.
    /// The realtime event only says the outcome changed; the data needs this read.</summary>
    [HttpGet("{correlationId}")]
    public ActionResult<CommandResultDto> Get(
        Guid deviceId,
        string correlationId,
        [FromServices] IDeviceStore devices,
        [FromServices] ICommandAuditStore audit,
        [FromServices] ICommandResponseStore responses)
    {
        var actor = AccessActorFactory.FromPrincipal(User, organizations, permissions);
        var device = devices.Find(new DeviceId(deviceId));
        if (actor is null || device is null ||
            !ScopeAuthorization.CanAccessDevice(User, device, organizations, permissions))
            return NotFound(new { code = "COMMAND_NOT_FOUND" });

        var result = audit.Find(device.Id, correlationId);
        var response = responses.Find(device.Id, correlationId);
        // Reply data was released by a permission the sender holds, which a viewer may not.
        if (response is not null && response.IssuedBy != actor.UserId && !actor.IsMasterAdmin)
            response = null;
        if (result is null && response is null)
            return NotFound(new { code = "COMMAND_NOT_FOUND" });

        Response.Headers.CacheControl = "no-store";
        return Ok(new CommandResultDto(
            correlationId,
            result,
            response is null
                ? null
                : new CommandReplyDto(response.Type, response.Action, response.Success, response.Error,
                    response.DataJson is null ? null : System.Text.Json.JsonDocument.Parse(response.DataJson).RootElement.Clone(),
                    response.ReceivedAt)));
    }
}

public sealed record CommandResultDto(
    string CorrelationId,
    CommandExecutionResult? Result,
    CommandReplyDto? Reply);

public sealed record CommandReplyDto(
    string Type,
    string Action,
    bool Success,
    string? Error,
    System.Text.Json.JsonElement? Data,
    DateTimeOffset ReceivedAt);

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
