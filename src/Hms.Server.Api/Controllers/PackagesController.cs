using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Hms.Server.Api.Authorization;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/packages")]
public sealed class PackagesController(
    CreatePackageHandler handler,
    UpdatePackageHandler updater,
    IPackageStore store,
    IProjectStore projects,
    IOrganizationStore organizations,
    IPermissionStore permissions) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<PackageDto>> List() => Ok(
        store.List()
            .Where(x => projects.Find(x.ProjectId) is { } project &&
                        ScopeAuthorization.CanAccessOrganization(User, project.OrganizationId, organizations, permissions))
            .Select(x => new PackageDto(x.Id.Value, x.Name, x.ProjectId.Value))
            .ToArray());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public ActionResult<PackageDto> Create(CreatePackageRequest request)
    {
        var project = projects.Find(request.ProjectId);
        if (project is null)
            return BadRequest(new { code = "PROJECT_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "packages.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, project.OrganizationId, organizations, permissions))
            return Forbid();

        try { return Ok(handler.Handle(request)); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = ex.Message }); }
    }

    [HttpPut("{packageId:guid}")]
    [ValidateAntiForgeryToken]
    public ActionResult<PackageDto> Update(Guid packageId, UpdatePackageRequest request)
    {
        var item = store.Find(new Hms.Server.Domain.Organizations.PackageId(packageId));
        if (item is null) return NotFound(new { code = "PACKAGE_NOT_FOUND" });
        var project = projects.Find(item.ProjectId);
        if (project is null) return BadRequest(new { code = "PROJECT_NOT_FOUND" });
        if (!ScopeAuthorization.HasFeaturePermission(User, "packages.manage", permissions) ||
            !ScopeAuthorization.CanAccessOrganization(User, project.OrganizationId, organizations, permissions)) return Forbid();
        try { return Ok(updater.Handle(item.Id, request)); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message }); }
    }
}
