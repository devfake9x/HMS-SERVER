using Hms.Server.Application.Authorization;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Identity;

public sealed class IdentityUserManagement(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    IPermissionStore permissions,
    ApplicationDbContext db) : IUserManagement, IUserScopeStore
{
    public async Task<IReadOnlyList<ManagedUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ManagedUserDto>();
        await foreach (var user in users.Users.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            result.Add(new(user.Id.ToString("D"), user.Email ?? string.Empty, user.EmailConfirmed,
                (await users.GetRolesAsync(user)).ToArray()));
        }

        return result;
    }

    public async Task<(ManagedUserDto? User, IReadOnlyList<string> Errors)> CreateAsync(
        CreateManagedUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = request.EmailConfirmed,
            OrganizationId = request.OrganizationId?.Value
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return (null, result.Errors.Select(x => x.Code).ToArray());

        permissions.Advance();

        return (new(user.Id.ToString("D"), user.Email ?? string.Empty, user.EmailConfirmed, []), []);
    }

    public bool Exists(UserId userId) => users.Users.Any(x => x.Id == userId.Value);

    public OrganizationId? Organization(UserId userId) => users.Users
        .Where(x => x.Id == userId.Value)
        .Select(x => x.OrganizationId)
        .AsEnumerable()
        .Select(x => x.HasValue ? new OrganizationId(x.Value) : (OrganizationId?)null)
        .SingleOrDefault();

    // The role manager's own normalizer decides which role a name reaches, so it has
    // the last word on whether the name is the reserved one.
    private bool IsReserved(string role) =>
        RoleAuthorizationPolicy.IsReserved(role) ||
        roles.NormalizeKey(role.Trim()) == roles.NormalizeKey(RoleAuthorizationPolicy.MasterAdminRole);

    public bool IsMasterAdmin(UserId userId)
    {
        var masterAdmin = roles.NormalizeKey(RoleAuthorizationPolicy.MasterAdminRole);
        return db.UserRoles.Any(userRole =>
            userRole.UserId == userId.Value &&
            db.Roles.Any(role => role.Id == userRole.RoleId && role.NormalizedName == masterAdmin));
    }

    public async Task<IReadOnlyList<string>> CreateRoleAsync(
        string role,
        UserManagementActor actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role)) return ["ROLE_REQUIRED"];
        if (IsReserved(role) && !actor.IsMasterAdmin)
            return ["RESERVED_ROLE_FORBIDDEN"];
        var result = await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = role.Trim() });
        if (result.Succeeded)
            permissions.Advance();
        return result.Succeeded ? [] : result.Errors.Select(x => x.Code).ToArray();
    }

    public async Task<IReadOnlyList<string>> AssignRoleAsync(
        string userId,
        string role,
        UserManagementActor actor,
        CancellationToken cancellationToken = default)
    {
        if (IsReserved(role) && !actor.IsMasterAdmin)
            return ["RESERVED_ROLE_FORBIDDEN"];
        if (!Guid.TryParse(userId, out var id)) return ["USER_ID_INVALID"];
        var user = await users.FindByIdAsync(id.ToString("D"));
        if (user is null) return ["USER_NOT_FOUND"];
        if (!await roles.RoleExistsAsync(role)) return ["ROLE_NOT_FOUND"];
        var result = await users.AddToRoleAsync(user, role);
        if (result.Succeeded)
            permissions.Advance();
        return result.Succeeded ? [] : result.Errors.Select(x => x.Code).ToArray();
    }
}
