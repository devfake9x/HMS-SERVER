using System.Security.Claims;
using Hms.Server.Infrastructure.Identity;
using Hms.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hms.Server.UnitTests;

public sealed class IdentityRealtimePrincipalRefresherTests
{
    [Fact]
    public async Task Missing_identity_identifier_fails_closed_without_resolving_user()
    {
        var refresher = new IdentityRealtimePrincipalRefresher(null!, null!, NullLogger<IdentityRealtimePrincipalRefresher>.Instance);

        var refreshed = await refresher.RefreshAsync(new ClaimsPrincipal(new ClaimsIdentity("cookie")));

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Malformed_identity_identifier_fails_closed()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], "cookie"));
        var refresher = new IdentityRealtimePrincipalRefresher(null!, null!, NullLogger<IdentityRealtimePrincipalRefresher>.Instance);

        var refreshed = await refresher.RefreshAsync(principal);

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Refresh_rebuilds_claims_from_current_identity_state()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "stamp-1" };
        var manager = new TestUserManager(user);
        var principalFactory = new TestPrincipalFactory();
        var principal = Principal(user.Id, "stamp-1", new Claim("perm", "stale"));
        var refresher = new IdentityRealtimePrincipalRefresher(
            manager,
            principalFactory,
            NullLogger<IdentityRealtimePrincipalRefresher>.Instance);

        var refreshed = await refresher.RefreshAsync(principal);

        Assert.NotNull(refreshed);
        Assert.Contains(refreshed!.Claims, claim => claim.Type == "perm" && claim.Value == "fresh");
        Assert.DoesNotContain(refreshed.Claims, claim => claim.Type == "perm" && claim.Value == "stale");
        Assert.Equal(user.Id.ToString("D"), principalFactory.LastUserId);
    }

    [Fact]
    public async Task Locked_identity_fails_closed()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "stamp-1" };
        var manager = new TestUserManager(user) { LockedOut = true };
        var refresher = new IdentityRealtimePrincipalRefresher(
            manager,
            new TestPrincipalFactory(),
            NullLogger<IdentityRealtimePrincipalRefresher>.Instance);

        var refreshed = await refresher.RefreshAsync(Principal(user.Id, "stamp-1"));

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Changed_security_stamp_fails_closed_when_ticket_has_stamp()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "stamp-2" };
        var refresher = new IdentityRealtimePrincipalRefresher(
            new TestUserManager(user),
            new TestPrincipalFactory(),
            NullLogger<IdentityRealtimePrincipalRefresher>.Instance);

        var refreshed = await refresher.RefreshAsync(Principal(user.Id, "stamp-1"));

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Identity_lookup_failure_fails_closed()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "stamp-1" };
        var manager = new TestUserManager(user) { ThrowOnFind = true };
        var refresher = new IdentityRealtimePrincipalRefresher(
            manager,
            new TestPrincipalFactory(),
            NullLogger<IdentityRealtimePrincipalRefresher>.Instance);

        var refreshed = await refresher.RefreshAsync(Principal(user.Id, "stamp-1"));

        Assert.Null(refreshed);
    }

    private static ClaimsPrincipal Principal(Guid userId, string? securityStamp, params Claim[] extraClaims)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString("D")) };
        if (securityStamp is not null)
            claims.Add(new(Microsoft.Extensions.Options.Options.Create(new IdentityOptions()).Value.ClaimsIdentity.SecurityStampClaimType, securityStamp));
        claims.AddRange(extraClaims);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie"));
    }

    private sealed class TestPrincipalFactory : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public string? LastUserId { get; private set; }

        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
        {
            LastUserId = user.Id.ToString("D");
            return Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, LastUserId),
                    new Claim("perm", "fresh")
                ],
                "refreshed")));
        }
    }

    private sealed class TestUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser _user;

        public TestUserManager(ApplicationUser user) : base(
            new TestUserStore(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _user = user;
        }

        public bool LockedOut { get; init; }

        public bool ThrowOnFind { get; init; }

        public override Task<ApplicationUser?> FindByIdAsync(string userId) =>
            ThrowOnFind
                ? throw new InvalidOperationException("identity lookup unavailable")
                :
            Guid.TryParse(userId, out var id) && id == _user.Id
                ? Task.FromResult<ApplicationUser?>(_user)
                : Task.FromResult<ApplicationUser?>(null);

        public override Task<bool> IsLockedOutAsync(ApplicationUser user) => Task.FromResult(LockedOut);

        public override Task<string> GetSecurityStampAsync(ApplicationUser user) =>
            Task.FromResult(_user.SecurityStamp ?? string.Empty);
    }

    private sealed class TestUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Dispose() { }
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString("D"));
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
