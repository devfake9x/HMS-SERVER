using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Organizations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Hms.Server.Api.Authorization;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/organizations")]
public sealed class OrganizationsController(
    CreateOrganizationHandler handler,
    UpdateOrganizationHandler updater,
    IOrganizationStore store,
    IPermissionStore permissions) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<OrganizationDto>> List() => Ok(
        store.List()
            .Where(x => ScopeAuthorization.EffectiveOrganizations(User, store, permissions).Contains(x.Id))
            .Select(x => new OrganizationDto(x.Id.Value, x.Name, x.ParentId?.Value))
            .ToArray());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public ActionResult<OrganizationDto> Create(CreateOrganizationRequest request)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "organizations.manage", permissions))
            return Forbid();
        if (request.ParentId is null && !User.IsInRole("master-admin"))
            return Forbid();
        if (request.ParentId is { } parentId &&
            !ScopeAuthorization.CanAccessOrganization(User, parentId, store, permissions))
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

    [HttpPut("{organizationId:guid}")]
    [ValidateAntiForgeryToken]
    public ActionResult<OrganizationDto> Update(Guid organizationId, UpdateOrganizationRequest request)
    {
        var item = store.Find(new OrganizationId(organizationId));
        if (item is null) return NotFound(new { code = "ORGANIZATION_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "organizations.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, item.Id, store, permissions)) return Forbid();
        try { return Ok(updater.Handle(item.Id, request)); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message }); }
    }
}
