using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Organizations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Hms.Server.Api.Authorization;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(
    CreateProjectHandler handler,
    UpdateProjectHandler updater,
    IProjectStore store,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<ProjectDto>> List() => Ok(
        store.List()
            .Where(x => ScopeAuthorization.CanAccessOrganization(User, x.OrganizationId, organizations, permissions))
            .Select(x => new ProjectDto(x.Id.Value, x.Name, x.OrganizationId.Value))
            .ToArray());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public ActionResult<ProjectDto> Create(CreateProjectRequest request)
    {
        if (!ScopeAuthorization.HasFeaturePermission(User, "projects.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, request.OrganizationId, organizations, permissions))
            return Forbid();

        try { return Ok(handler.Handle(request)); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = ex.Message }); }
    }

    [HttpPut("{projectId:guid}")]
    [ValidateAntiForgeryToken]
    public ActionResult<ProjectDto> Update(Guid projectId, UpdateProjectRequest request)
    {
        var item = store.Find(new ProjectId(projectId));
        if (item is null) return NotFound(new { code = "PROJECT_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "projects.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, item.OrganizationId, organizations, permissions)) return Forbid();
        try { return Ok(updater.Handle(item.Id, request)); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message }); }
    }
}
