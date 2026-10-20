using System.Security.Claims;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(
    IPermissionStore permissions,
    ICommandCatalog catalog) : ControllerBase
{
    [HttpGet("permissions")]
    public ActionResult<PermissionSnapshotDto> Permissions()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Forbid();

        var identity = new UserId(userId);
        var feature = permissions.FeatureGrants(identity);
        var resources = permissions.ResourceGrants(identity);
        var actions = permissions.DeviceActionGrants(identity);
        var allowed = feature.Where(x => !x.Deny).Select(x => x.Permission)
            .Concat(resources.Where(x => !x.Deny).Select(x => $"organization:{x.OrganizationId.Value:N}"))
            .Concat(actions.Where(x => !x.Deny).Select(x => $"device:{x.DeviceId.Value:N}:action:{x.Action}"))
            .Concat(User.FindAll("perm").Select(x => x.Value));
        var denied = feature.Where(x => x.Deny).Select(x => x.Permission)
            .Concat(resources.Where(x => x.Deny).Select(x => $"organization:{x.OrganizationId.Value:N}"))
            .Concat(actions.Where(x => x.Deny).Select(x => $"device:{x.DeviceId.Value:N}:action:{x.Action}"))
            .Concat(User.FindAll("deny-perm").Select(x => x.Value));
        if (User.IsInRole("master-admin"))
            allowed = allowed.Append("*");

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(new PermissionSnapshotDto(
            permissions.PolicyVersion,
            DateTimeOffset.UtcNow.AddMinutes(5),
            allowed.Distinct(StringComparer.Ordinal).ToArray(),
            denied.Distinct(StringComparer.Ordinal).ToArray()));
    }

    [HttpGet("command-catalog")]
    public ActionResult<CommandCatalogSnapshotDto> CommandCatalog()
    {
        if (catalog is not ICommandCatalogMetadata metadata)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "COMMAND_CATALOG_METADATA_UNAVAILABLE" });

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(new CommandCatalogSnapshotDto(
            metadata.Metadata.CatalogVersion,
            metadata.Metadata.CatalogHash,
            metadata.Metadata.IsTrusted,
            catalog.List().Select(x => new CommandCatalogActionDto(
                x.Type,
                x.Action,
                Available: x.Availability == CommandAvailability.Enabled,
                Class: x.Class,
                RequiredPermission: x.RequiredPermission,
                PermissionByDomain: CommandPermissionResolver.DomainPermissions(x),
                RequiresConfirmation: x.ConfirmTemplate is not null,
                DryRunSupported: x.DryRunSupported,
                TtlMs: x.DefaultTtlMs ?? CommandDefinition.DispatcherDefaultTtlMs,
                HighRisk: CommandPermissionResolver.IsHighRisk(x))).ToArray()));
    }
}

public sealed record PermissionSnapshotDto(
    long PolicyVersion,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> DeniedPermissions);

public sealed record CommandCatalogSnapshotDto(
    string CatalogVersion,
    string CatalogHash,
    bool Trusted,
    IReadOnlyList<CommandCatalogActionDto> Actions);

// PermissionByDomain is null for fixed-permission rows; otherwise domain → permission already
// resolved for this action's class.
public sealed record CommandCatalogActionDto(
    string Type,
    string Action,
    bool Available,
    CommandClass Class,
    string? RequiredPermission,
    IReadOnlyDictionary<string, string>? PermissionByDomain,
    bool RequiresConfirmation,
    bool DryRunSupported,
    int TtlMs,
    bool HighRisk);
