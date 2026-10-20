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
    IDeviceAccessResolver accessResolver)
{
    public async ValueTask<CommandExecutionResult> HandleAsync(
        DeviceCommandRequest request,
        AccessActor actor,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId != actor.UserId)
        {
            return Failure(request, CommandExecutionStatus.Failed, "COMMAND_ACTOR_MISMATCH");
        }

        var requestFingerprint = CommandRequestFingerprint.Compute(request);
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

        if (definition.ConfirmTemplate is not null && string.IsNullOrWhiteSpace(request.Confirm))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.Failed, "CONFIRM_REQUIRED"), cancellationToken);
        }

        // Cached idempotent results are returned only after the current catalog,
        // device-state, access and confirmation checks.
        if (cachedResult is not null)
            return cachedResult;

        var ttl = TimeSpan.FromMilliseconds(definition.DefaultTtlMs ?? CommandDefinition.DispatcherDefaultTtlMs);
        if (ttl > TimeSpan.FromHours(24))
        {
            return await RecordAsync(request, null, Failure(request, CommandExecutionStatus.ContractUnavailable, "TTL_EXCEEDS_FIRMWARE_LIMIT"), cancellationToken);
        }

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

        return result;
    }

    private static CommandExecutionResult Failure(DeviceCommandRequest request, CommandExecutionStatus status, string code) =>
        new(status, code, request.CorrelationId);
}
