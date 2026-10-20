using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.Application.Authorization;

public sealed record ManagedUserDto(string Id, string Email, bool EmailConfirmed, IReadOnlyList<string> Roles);
public sealed record CreateManagedUserRequest(
    string Email,
    string Password,
    bool EmailConfirmed = false,
    OrganizationId? OrganizationId = null);
public sealed record CreateRoleRequest(string Name);
public sealed record UserManagementActor(bool IsMasterAdmin);

public interface IUserManagement
{
    Task<IReadOnlyList<ManagedUserDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<(ManagedUserDto? User, IReadOnlyList<string> Errors)> CreateAsync(
        CreateManagedUserRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> CreateRoleAsync(
        string role,
        UserManagementActor actor,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> AssignRoleAsync(
        string userId,
        string role,
        UserManagementActor actor,
        CancellationToken cancellationToken = default);
}

public interface IUserScopeStore
{
    bool Exists(UserId userId);
    OrganizationId? Organization(UserId userId);
    bool IsMasterAdmin(UserId userId);
}
