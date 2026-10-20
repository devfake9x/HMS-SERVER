using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Hms.Server.Api.Authorization;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/areas")]
public sealed class AreasController(
    CreateAreaHandler handler,
    UpdateAreaHandler updater,
    IAreaStore store,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<AreaDto>> List() => Ok(
        store.List()
            .Where(x => ScopeAuthorization.CanAccessOrganization(User, x.OrganizationId, organizations, permissions))
            .Select(x => new AreaDto(x.Id.Value, x.Name, x.OrganizationId.Value))
            .ToArray());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public ActionResult<AreaDto> Create(CreateAreaRequest request)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "areas.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, request.OrganizationId, organizations, permissions))
            return Forbid();

        try { return Ok(handler.Handle(request)); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = ex.Message }); }
    }

    [HttpPut("{areaId:guid}")]
    [ValidateAntiForgeryToken]
    public ActionResult<AreaDto> Update(Guid areaId, UpdateAreaRequest request)
    {
        var item = store.Find(new Hms.Server.Domain.Organizations.AreaId(areaId));
        if (item is null) return NotFound(new { code = "AREA_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "areas.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, item.OrganizationId, organizations, permissions)) return Forbid();
        try { return Ok(updater.Handle(item.Id, request)); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message }); }
    }
}
