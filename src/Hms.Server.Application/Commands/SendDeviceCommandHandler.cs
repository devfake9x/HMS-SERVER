using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;

namespace Hms.Server.Application.Commands;

public sealed class SendDeviceCommandHandler(
    ICommandCatalog catalog,
    ICommandProtocolAdapter protocolAdapter,
    IDeviceCommandTransport transport,
    ICommandAuditStore auditStore,
    Func<DeviceId, Device?> deviceResolver,
    IDeviceAccessResolver accessResolver,
    Microsoft.Extensions.Logging.ILogger<SendDeviceCommandHandler>? logger = null)
{
    /// <summary>Upper bound on the TTL of every command; it must stay below the server's reply
    /// timeout (30 s by default). It is not shorter because a device clock a few seconds ahead
    /// would then reject every command, including the reads used to diagnose it.</summary>
    public const int MaxCommandTtlMs = 20_000;

    /// <summary>Tighter bound for commands that move physical equipment or restart the device
    /// (relay writes, reboot): a late one is worse than a refused one. This is not a replay
    /// bound: a device with a slow clock or a broker that redelivers can still run an old command.</summary>
    public const int MaxPhysicalCommandTtlMs = 10_000;

    public async ValueTask<CommandExecutionResult> HandleAsync(
        DeviceCommandRequest request,
        AccessActor actor,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId != actor.UserId)
        {
            return Failure(request, CommandExecutionStatus.Failed, "COMMAND_ACTOR_MISMATCH");
        }

        string requestFingerprint;
        try
        {
            requestFingerprint = CommandRequestFingerprint.Compute(request);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            // A string that is not valid UTF-16 (a lone "\uD800" escape) cannot be serialized: refuse
            // it as a bad payload instead of failing the request.
            return Failure(request, CommandExecutionStatus.ContractUnavailable, "COMMAND_PAYLOAD_INVALID");
        }
        var previousPlan = auditStore.FindPlan(request.DeviceId, request.CorrelationId);
        CommandExecutionResult? cachedResult = null;
        if (previousPlan is not null)
        {
            if (!StringComparer.Ordinal.Equals(previousPlan.RequestFingerprint, requestFingerprint))
            {
                return Failure(request, CommandExecutionStatus.Failed, "CORRELATION_ID_REUSED");
            }

            var currentDevice = deviceResolver(request.DeviceId);
            if (currentDevice is null || currentDevice.KeyGeneration != previousPlan.KeyGeneration)
                return Failure(request, CommandExecutionStatus.Failed, "KEY_GENERATION_FENCE");

            // An unfinished reservation must be re-read through ReserveAsync so
            // the store can distinguish a live owner from an expired lease.
            // The latter is outcome-unknown and must remain fail-closed.
            cachedResult = auditStore.Find(request.DeviceId, request.CorrelationId);
        }

        var device = deviceResolver(request.DeviceId);
        // Out of scope reads exactly like a missing device: no catalog-stage code may
        // reveal that the device exists in another organization.
        var access = device is null ? null : accessResolver.Resolve(actor, device);
        if (device is null || access is null || (!access.IsMasterAdmin && !access.InScope))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.Failed, DeviceAccessCodes.DeviceNotFound), cancellationToken);
        }

        if (catalog is not ICommandCatalogMetadata catalogMetadata)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "CATALOG_METADATA_UNAVAILABLE"), cancellationToken);
        }

        if (!catalogMetadata.Metadata.IsTrusted)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "CATALOG_UNTRUSTED"), cancellationToken);
        }

        var hasCatalogVersion = !string.IsNullOrWhiteSpace(request.CatalogVersion);
        var hasCatalogHash = !string.IsNullOrWhiteSpace(request.CatalogHash);
        if (!hasCatalogVersion || !hasCatalogHash ||
            !string.Equals(request.CatalogVersion, catalogMetadata.Metadata.CatalogVersion, StringComparison.Ordinal) ||
            !string.Equals(request.CatalogHash, catalogMetadata.Metadata.CatalogHash, StringComparison.OrdinalIgnoreCase))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "CATALOG_STALE"), cancellationToken);
        }

        var definition = catalog.Find(request.Type, request.Action);
        if (definition is null)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "COMMAND_NOT_IN_CATALOG"), cancellationToken);
        }

        if (definition.Availability == Domain.Commands.CommandAvailability.DeniedUntilActionContract)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "DENIED_UNTIL_ACTION_CONTRACT"), cancellationToken);
        }

        // The permission is the one the catalog (spec §11) assigns to this
        // (type, action), or to the payload's config domain; none means fail closed.
        var permission = CommandPermissionResolver.Resolve(definition, request.Payload);
        if (permission is null)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "PERMISSION_UNMAPPED"), cancellationToken);
        }

        var decision = DeviceAccessPolicy.Evaluate(access, device, permission);
        if (!decision.Allowed)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.Failed, decision.Code), cancellationToken);
        }

        // A flash command (reboot) is for a master-admin only, whatever the role grants (user
        // decision 2026-11-09). Refused here, before the confirm check, the audit reservation and
        // any signing. Missing the permission is still the plain permission refusal above.
        if (CommandPermissionResolver.RequiresMasterAdmin(definition) && !access.IsMasterAdmin)
        {
            return await RecordAsync(request, null,
                Failure(request, CommandExecutionStatus.Failed, DeviceAccessOutcomeCodes.HighRiskRequiresMasterAdmin), cancellationToken);
        }

        if (definition.ConfirmTemplate is not null && string.IsNullOrWhiteSpace(request.Confirm))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.Failed, "CONFIRM_REQUIRED"), cancellationToken);
        }

        // Cached idempotent results are returned only after the current catalog,
        // device-state, access and confirmation checks.
        if (cachedResult is not null)
            return cachedResult;

        var ttl = TimeSpan.FromMilliseconds(definition.DefaultTtlMs ?? CommandDefinition.DispatcherDefaultTtlMs);
        // A command must expire on the device before the server stops waiting for its reply;
        // otherwise it can still run (and hold a slot of the device's small queue) after the
        // operator was told it timed out. Spec §3: TTL is checked when it leaves the queue.
        var maxTtl = definition.Class is Domain.Commands.CommandClass.Physical or Domain.Commands.CommandClass.Flash
            ? MaxPhysicalCommandTtlMs
            : MaxCommandTtlMs;
        if (ttl > TimeSpan.FromMilliseconds(maxTtl))
            ttl = TimeSpan.FromMilliseconds(maxTtl);
        if (ttl > TimeSpan.FromHours(24))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "TTL_EXCEEDS_FIRMWARE_LIMIT"), cancellationToken);
        }

        // A write is signed with server time. A client time up to 60 s ahead would keep it valid on
        // the device (spec §3) long after the write lane let the next one through and the operator
        // read the state back.
        if (CommandLanes.IsWrite(definition.Class))
            request = request with { RequestedAt = DateTimeOffset.UtcNow };

        if (request.RequestedAt > DateTimeOffset.UtcNow.AddSeconds(60))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.Failed, "COMMAND_TIMESTAMP_IN_FUTURE"), cancellationToken);
        }

        var expiresAt = request.RequestedAt.Add(ttl);
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.Failed, "COMMAND_EXPIRED"), cancellationToken);
        }

        var plan = new CommandExecutionPlan(request, definition, device.KeyGeneration, expiresAt);
        var reservation = await auditStore.ReserveAsync(plan, requestFingerprint, cancellationToken);
        switch (reservation.Status)
        {
            case CommandAuditReservationStatus.Completed:
                if (device.KeyGeneration != reservation.Plan.KeyGeneration)
                    return Failure(request, CommandExecutionStatus.Failed, "KEY_GENERATION_FENCE");
                return reservation.ExistingResult
                    ?? Failure(request, CommandExecutionStatus.Failed, "AUDIT_RESULT_MISSING");
            case CommandAuditReservationStatus.InFlight:
                return Failure(request, CommandExecutionStatus.Failed, "IDEMPOTENCY_IN_PROGRESS");
            case CommandAuditReservationStatus.StaleInFlight:
                // A crashed owner may have reached the transport. Never retry an
                // expired lease automatically because doing so could duplicate a
                // device side effect; require reconciliation before reuse.
                return Failure(request, CommandExecutionStatus.Failed, "IDEMPOTENCY_OUTCOME_UNKNOWN");
            case CommandAuditReservationStatus.Conflict:
                return Failure(request, CommandExecutionStatus.Failed, "CORRELATION_ID_REUSED");
            case CommandAuditReservationStatus.Claimed:
                break;
            default:
                throw new InvalidOperationException("UNKNOWN_COMMAND_RESERVATION_STATUS");
        }

        var prepared = await protocolAdapter.PrepareAsync(plan, cancellationToken);
        var result = await transport.SendAsync(plan, prepared, cancellationToken);
        return await RecordAsync(request, plan, result, cancellationToken);
    }

    private async ValueTask<CommandExecutionResult> RecordAsync(
        DeviceCommandRequest request,
        CommandExecutionPlan? plan,
        CommandExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (plan is not null)
        {
            await auditStore.RecordAsync(plan, result, cancellationToken);
        }
        else if (!result.IsSuccess && logger is not null)
        {
            // Refused before a reservation exists, so no audit row: keep a structured trace.
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                "Command {Type}.{Action} for device {DeviceId} by user {UserId} refused before audit: {Code}",
                request.Type, request.Action, request.DeviceId.Value, request.UserId.Value, result.Code);
        }

        return result;
    }

    private static CommandExecutionResult Failure(DeviceCommandRequest request, CommandExecutionStatus status, string code) =>
        new(status, code, request.CorrelationId);
}
