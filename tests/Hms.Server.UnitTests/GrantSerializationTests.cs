using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Organizations;

namespace Hms.Server.UnitTests;

// The check and the write of one grant decision run as one step per target user,
// so a decision never rests on a view that another decision for that user changes
// before it writes.
public sealed class GrantSerializationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);
    private readonly DeviceAccessTestFixture _world = new();

    [Fact]
    public async Task Concurrent_feature_and_scope_grants_cannot_combine_beyond_either_grantor()
    {
        var target = NewUser(_world.Child.Id);
        var childAdmin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var parentAdmin = _world.Admin(NewUser(_world.Parent.Id), _world.Parent.Id, "permissions.manage");
        var pausing = new PausingOrganizationStore(_world.Organizations);
        var features = new GrantFeaturePermissionHandler(_world.Permissions, _world.Users, pausing);

        // The feature grant pauses inside its check, while it holds the target user.
        var featureGrant = Task.Run(() => features.Handle(childAdmin, target, "devices.manage"));
        try
        {
            await pausing.Paused.WaitAsync(Timeout);
            var scopeGrant = Task.Run(() => _world.GrantResource().Handle(parentAdmin, target, _world.Parent.Id));

            Assert.False(scopeGrant.Wait(Settle));
            pausing.Resume();

            Assert.Equal("OK", (await featureGrant.WaitAsync(Timeout)).Code);
            // Neither admin alone could give the target devices.manage over Parent.
            Assert.Equal("GRANT_EXCEEDS_OWN_PERMISSIONS", (await scopeGrant.WaitAsync(Timeout)).Code);
        }
        finally
        {
            pausing.Resume();
        }
    }

    [Fact]
    public async Task Master_deny_is_not_overtaken_by_a_concurrent_non_master_allow()
    {
        var target = NewUser(_world.Child.Id);
        var admin = _world.Admin(NewUser(_world.Child.Id), _world.Child.Id, "permissions.manage", "devices.manage");
        var resume = new ManualResetEventSlim();
        var paused = new SemaphoreSlim(0);
        // Only a deny asks whether the target is a master-admin, so only the master deny pauses.
        _world.Users.OnIsMasterAdmin = _ =>
        {
            paused.Release();
            resume.Wait(Timeout);
        };

        var masterDeny = Task.Run(() =>
            _world.GrantFeature().Handle(_world.MasterAdmin(new UserId(Guid.NewGuid())), target, "devices.manage", deny: true));
        try
        {
            await paused.WaitAsync(Timeout);
            _world.Users.OnIsMasterAdmin = null;
            var adminAllow = Task.Run(() => _world.GrantFeature().Handle(admin, target, "devices.manage"));

            Assert.False(adminAllow.Wait(Settle));
            resume.Set();

            Assert.Equal("OK", (await masterDeny.WaitAsync(Timeout)).Code);
            Assert.Equal("GRANT_OVERRIDES_MASTER_DECISION", (await adminAllow.WaitAsync(Timeout)).Code);
            Assert.False(PermissionGrantResolver.Features([], [], _world.Permissions.FeatureGrants(target))
                .Has("devices.manage", isMasterAdmin: false));
        }
        finally
        {
            resume.Set();
        }
    }

    private UserId NewUser(OrganizationId organization)
    {
        var user = new UserId(Guid.NewGuid());
        _world.Users.Add(user, organization);
        return user;
    }

    // Forwards to the real store; the first List call signals Paused and waits for Resume.
    private sealed class PausingOrganizationStore(IOrganizationStore inner) : IOrganizationStore
    {
        private readonly ManualResetEventSlim _resume = new();
        private int _calls;

        public SemaphoreSlim Paused { get; } = new(0);

        public void Resume() => _resume.Set();

        public Organization? Find(OrganizationId id) => inner.Find(id);
        public void Add(Organization organization) => inner.Add(organization);
        public void Update(Organization organization) => inner.Update(organization);

        public IReadOnlyList<Organization> List()
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Paused.Release();
                _resume.Wait(Timeout);
            }

            return inner.List();
        }
    }
}
