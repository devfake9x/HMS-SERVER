using System.Security.Claims;
using Hms.Server.Api.Authorization;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

public sealed class AccessActorFactoryTests
{
    private readonly InMemoryOrganizationStore _organizations = new();
    private readonly InMemoryPermissionStore _permissions = new();
    private readonly Organization _root = Organization.Create("Root");
    private readonly Organization _child;
    private readonly Organization _grandchild;
    private readonly Organization _other = Organization.Create("Other");
    private readonly UserId _user = new(Guid.NewGuid());

    public AccessActorFactoryTests()
    {
        _child = Organization.Create("Child", _root.Id);
        _grandchild = Organization.Create("Grandchild", _child.Id);
        foreach (var organization in new[] { _root, _child, _grandchild, _other })
            _organizations.Add(organization);
    }

    public static TheoryData<string?> InvalidNameIdentifiers => new() { null, "not-a-guid" };

    [Theory]
    [MemberData(nameof(InvalidNameIdentifiers))]
    public void Missing_or_invalid_name_identifier_yields_no_actor(string? nameIdentifier)
    {
        var claims = new List<Claim> { new("perm", "users.manage") };
        if (nameIdentifier is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        Assert.Null(AccessActorFactory.FromPrincipal(principal, _organizations, _permissions));
    }

    [Fact]
    public void Master_admin_actor_covers_every_organization()
    {
        var actor = AccessActorFactory.FromPrincipal(
            Principal(new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole)), _organizations, _permissions);

        Assert.NotNull(actor);
        Assert.True(actor!.IsMasterAdmin);
        Assert.Equal(_user, actor.UserId);
        Assert.True(actor.EffectiveOrganizations.SetEquals([_root.Id, _child.Id, _grandchild.Id, _other.Id]));
    }

    [Fact]
    public void Master_admin_keeps_every_organization_despite_resource_denies()
    {
        Grant(_root.Id, deny: true);
        var principal = Principal(
            new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole),
            new Claim("deny-org-scope", _child.Id.Value.ToString()));

        var organizations = AccessActorFactory.EffectiveOrganizations(principal, _organizations, _permissions);
        var actor = AccessActorFactory.FromPrincipal(principal, _organizations, _permissions);

        Assert.True(organizations.SetEquals([_root.Id, _child.Id, _grandchild.Id, _other.Id]));
        Assert.True(actor!.IsMasterAdmin);
        Assert.True(actor.EffectiveOrganizations.SetEquals(organizations));
    }

    [Fact]
    public void Resource_grant_expands_to_descendant_organizations()
    {
        Grant(_root.Id);

        var actor = AccessActorFactory.FromPrincipal(Principal(), _organizations, _permissions);

        Assert.False(actor!.IsMasterAdmin);
        Assert.True(actor.EffectiveOrganizations.SetEquals([_root.Id, _child.Id, _grandchild.Id]));
    }

    [Fact]
    public void Denied_organization_is_subtracted_after_expansion()
    {
        Grant(_root.Id);
        Grant(_child.Id, deny: true);

        var actor = AccessActorFactory.FromPrincipal(Principal(), _organizations, _permissions);

        Assert.True(actor!.EffectiveOrganizations.SetEquals([_root.Id]));
    }

    [Fact]
    public void Latest_grant_wins_after_grant_deny_grant()
    {
        Grant(_root.Id);
        Grant(_root.Id, deny: true);
        Grant(_root.Id);
        Grant("users.manage");
        Grant("users.manage", deny: true);
        Grant("users.manage");
        Grant(_other.Id);
        Grant(_other.Id, deny: true);
        Grant("devices.manage");
        Grant("devices.manage", deny: true);

        var actor = AccessActorFactory.FromPrincipal(Principal(), _organizations, _permissions);

        Assert.True(actor!.EffectiveOrganizations.SetEquals([_root.Id, _child.Id, _grandchild.Id]));
        Assert.Contains("users.manage", actor.FeaturePermissions);
        Assert.DoesNotContain("devices.manage", actor.FeaturePermissions);
    }

    [Fact]
    public void Wildcard_feature_allow_is_ignored()
    {
        Grant("*");
        var actor = AccessActorFactory.FromPrincipal(Principal(new Claim("perm", "*")), _organizations, _permissions);

        Assert.Empty(actor!.FeaturePermissions);

        Grant("users.manage");
        Grant("*", deny: true);
        var denied = AccessActorFactory.FromPrincipal(Principal(), _organizations, _permissions);

        Assert.Empty(denied!.FeaturePermissions);
    }

    [Fact]
    public void Persisted_deny_removes_a_claim_allow_from_the_actor_features()
    {
        Grant("users.manage", deny: true);

        var actor = AccessActorFactory.FromPrincipal(Principal(new Claim("perm", "users.manage")), _organizations, _permissions);

        Assert.DoesNotContain("users.manage", actor!.FeaturePermissions);
    }

    private ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(
        claims.Prepend(new Claim(ClaimTypes.NameIdentifier, _user.Value.ToString())), "Test"));

    private void Grant(OrganizationId organization, bool deny = false) =>
        _permissions.GrantResource(new ResourceScopeGrant(_user, organization, deny, _permissions.Advance()));

    private void Grant(string permission, bool deny = false) =>
        _permissions.GrantFeature(new FeaturePermissionGrant(_user, permission, deny, _permissions.Advance()));
}
