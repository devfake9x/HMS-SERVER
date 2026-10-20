using System.Security.Claims;
using Hms.Server.Application.Realtime;
using Hms.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.Server.Infrastructure.Identity;

/// <summary>Rebuilds a realtime authorization principal from current Identity state.</summary>
public sealed class IdentityRealtimePrincipalRefresher(
    UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    ILogger<IdentityRealtimePrincipalRefresher> logger,
    IOptions<IdentityOptions>? identityOptions = null) : IRealtimePrincipalRefresher
{
    public async ValueTask<ClaimsPrincipal?> RefreshAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(id, out var userId))
            return null;

        try
        {
            var user = await users.FindByIdAsync(userId.ToString("D"));
            if (user is null)
                return null;

            if (await users.IsLockedOutAsync(user))
                return null;

            var securityStampClaimType = (identityOptions?.Value ?? new IdentityOptions())
                .ClaimsIdentity.SecurityStampClaimType;
            var ticketStamp = principal.FindFirstValue(securityStampClaimType);
            if (!string.IsNullOrWhiteSpace(ticketStamp))
            {
                var currentStamp = await users.GetSecurityStampAsync(user);
                if (string.IsNullOrWhiteSpace(currentStamp) ||
                    !StringComparer.Ordinal.Equals(ticketStamp, currentStamp))
                    return null;
            }

            return await principalFactory.CreateAsync(user);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to refresh realtime principal for user {UserId}.", userId);
            return null;
        }
    }
}
