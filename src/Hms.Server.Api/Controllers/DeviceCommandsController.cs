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

    /// <summary>The catalog actions that are open and that the caller may send on this device, so
    /// the UI can hide what the server would refuse. Not a security boundary: sending re-checks.</summary>
    [HttpGet("~/api/v1/devices/{deviceId:guid}/allowed-commands")]
    public ActionResult<AllowedCommandsDto> Allowed(
        Guid deviceId,
        [FromServices] IDeviceStore devices,
        [FromServices] ICommandCatalog catalog,
        [FromServices] IDeviceAccessResolver accessResolver)
    {
        var device = devices.Find(new DeviceId(deviceId));
        var access = device is null || device.State == DeviceLifecycleState.Revoked
            ? null
            : DeviceAccessChecks.Resolve(User, device, organizations, permissions, accessResolver);
        // Same scope rule as sending: a device the caller cannot command does not exist for them.
        if (device is null || !DeviceAccessChecks.CanSendCommands(access))
            return NotFound(new { code = "DEVICE_NOT_FOUND" });

        var actions = catalog.List()
            .Where(x => x.Availability == Domain.Commands.CommandAvailability.Enabled &&
                        // Same rule as sending (a flash command is master-admin only), so the UI never
                        // offers what the server would refuse.
                        (!CommandPermissionResolver.RequiresMasterAdmin(x) || access!.IsMasterAdmin) &&
                        StaticPermission(x) is { } permission && DeviceAccessChecks.Allows(access, device, permission))
            .Select(x => $"{x.Type}.{x.Action}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Response.Headers.CacheControl = "no-store";
        return Ok(new AllowedCommandsDto(actions));
    }

    /// <summary>A command's latest outcome and, for the user who sent it, the device's reply data.
    /// The realtime event only says the outcome changed; the data needs this read.</summary>
    [HttpGet("{correlationId}")]
    public ActionResult<CommandResultDto> Get(
        Guid deviceId,
        string correlationId,
        [FromServices] IDeviceStore devices,
        [FromServices] ICommandAuditStore audit,
        [FromServices] ICommandResponseStore responses,
        [FromServices] ICommandCatalog catalog,
        [FromServices] IDeviceAccessResolver accessResolver)
    {
        var actor = AccessActorFactory.FromPrincipal(User, organizations, permissions);
        var device = devices.Find(new DeviceId(deviceId));
        var access = actor is null || device is null ? null : accessResolver.Resolve(actor, device);
        // Same scope rule as sending: whoever may send a command may follow its outcome.
        if (actor is null || device is null || !DeviceAccessChecks.CanSendCommands(access))
            return NotFound(new { code = "COMMAND_NOT_FOUND" });

        var result = audit.Find(device.Id, correlationId);
        var response = responses.Find(device.Id, correlationId);
        // Reply data was released by a permission the sender holds, which a viewer may not.
        if (response is not null && response.IssuedBy != actor.UserId && !actor.IsMasterAdmin)
            response = null;
        // And it is released only while the sender still holds that permission on the device.
        if (response is not null && !actor.IsMasterAdmin && !MayReadReply(response, device, catalog, access))
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

    private static bool MayReadReply(
        StoredCommandResponse response,
        Device device,
        ICommandCatalog catalog,
        Domain.Authorization.DeviceAccess? access)
    {
        var definition = catalog.Find(response.Type, response.Action);
        var permission = definition is null ? null : StaticPermission(definition);
        return permission is not null && DeviceAccessChecks.Allows(access, device, permission);
    }

    // The permission POST /commands applies to a command without a payload. Actions whose
    // permission depends on the payload (config domains) have none, and are left out.
    private static string? StaticPermission(Domain.Commands.CommandDefinition definition) =>
        CommandPermissionResolver.Resolve(definition, new Dictionary<string, object?>());
}

public sealed record AllowedCommandsDto(IReadOnlyList<string> Actions);

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
