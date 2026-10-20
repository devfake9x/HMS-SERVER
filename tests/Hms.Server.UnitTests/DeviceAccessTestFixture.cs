using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Management;
using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Authorization;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Infrastructure.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Server.UnitTests;

// One small world for the device-access tests: an organization tree, devices,
// the phase-2 role/membership store behind a recording wrapper, the real
// permission store, a recording epoch store and the pinned command catalog.
public sealed class DeviceAccessTestFixture
{
    private static readonly PinnedCommandCatalog SharedCatalog = new();
    private static readonly UserId Seeder = new(Guid.Parse("00000000-0000-0000-0000-00000000005e"));

    public DeviceAccessTestFixture()
    {
        Parent = Organization.Create("Parent");
        Child = Organization.Create("Child", Parent.Id);
        Other = Organization.Create("Other");
        Organizations.Add(Parent);
        Organizations.Add(Child);
        Organizations.Add(Other);
        Device = AddDevice("line-1", Child.Id);
        SecondDevice = AddDevice("line-2", Child.Id);
        OtherDevice = AddDevice("other-1", Other.Id);
        Epochs = new RecordingEpochStore(Permissions);
        Access = new RecordingDeviceAccessStore(Inner, Permissions, Epochs, () => Devices.List());
        Resolver = new DeviceAccessResolver(Access, Access);
    }

    public InMemoryOrganizationStore Organizations { get; } = new();
    public InMemoryDeviceStore Devices { get; } = new();
    public InMemoryPermissionStore Permissions { get; } = new();
    public InMemoryDeviceAccessStore Inner { get; } = new();
    public RecordingDeviceAccessStore Access { get; }
    public RecordingEpochStore Epochs { get; }
    public TestUserScopes Users { get; } = new();
    public PinnedCommandCatalog Catalog => SharedCatalog;
    public DeviceAccessResolver Resolver { get; }

    public Organization Parent { get; }
    public Organization Child { get; }
    public Organization Other { get; }
    public Device Device { get; }
    public Device SecondDevice { get; }
    public Device OtherDevice { get; }

    public Device AddDevice(string name, OrganizationId owner)
    {
        var device = Hms.Server.Domain.Devices.Device.Create(name, owner);
        device.Activate();
        Devices.Add(device);
        return device;
    }

    // Seeds go to the inner store so they do not show up as handler writes.
    public DeviceRole SeedRole(OrganizationId organization, params string[] permissions)
    {
        var role = DeviceRole.Create(organization, $"role-{Guid.NewGuid():N}", null, permissions);
        Inner.Add(role, SeedAudit(DeviceAccessChanges.RoleCreated));
        return role;
    }

    public void SeedMembership(UserId user, Device device, DeviceRole role)
    {
        var current = Inner.Find(user, device.Id)?.RoleId;
        Inner.Upsert(new DeviceMembership(user, device.Id, role.Id, Seeder, DateTimeOffset.UtcNow), current,
            SeedAudit(current is null ? DeviceAccessChanges.MembershipAssigned : DeviceAccessChanges.MembershipChanged));
    }

    // An actor whose only scope is a membership on the device, with a fresh
    // role in the device's organization.
    public AccessActor Member(UserId user, Device device, params string[] permissions)
    {
        SeedMembership(user, device, SeedRole(device.OwnerOrganizationId, permissions));
        return new AccessActor(user, false, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);
    }

    // A non-master actor with resource scope on the organization (and its
    // descendants) and the given feature permissions, as AccessActorFactory builds it.
    public AccessActor Admin(UserId user, OrganizationId scope, params string[] features) => new(
        user,
        false,
        OrganizationScopeResolver.Expand([scope], Organizations.List()).ToFrozenSet(),
        features.ToFrozenSet(StringComparer.Ordinal));

    public AccessActor MasterAdmin(UserId user) => new(
        user,
        true,
        Organizations.List().Select(x => x.Id).ToFrozenSet(),
        FrozenSet<string>.Empty);

    public CreateDeviceRoleHandler CreateRole() => new(
        Access, Organizations, Catalog, Permissions, NullLogger<CreateDeviceRoleHandler>.Instance);

    public UpdateDeviceRoleHandler UpdateRole() => new(
        Access, Access, Organizations, Devices, Resolver, Catalog, Users, Permissions, Epochs,
        NullLogger<UpdateDeviceRoleHandler>.Instance);

    public DeleteDeviceRoleHandler DeleteRole() => new(
        Access, Organizations, Permissions, NullLogger<DeleteDeviceRoleHandler>.Instance);

    public AssignDeviceMembershipHandler Assign() => new(
        Access, Access, Devices, Resolver, Users, Permissions, Epochs,
        NullLogger<AssignDeviceMembershipHandler>.Instance);

    public RemoveDeviceMembershipHandler Remove() => new(
        Access, Access, Devices, Resolver, Users, Permissions, Epochs,
        NullLogger<RemoveDeviceMembershipHandler>.Instance);

    public GrantFeaturePermissionHandler GrantFeature() => new(Permissions, Users, Organizations);

    public GrantResourceScopeHandler GrantResource() => new(Permissions, Users, Organizations);

    private static DeviceAccessAuditEntry SeedAudit(string change) =>
        new(Guid.NewGuid(), Seeder, change, null, null, null, null, null, null, DateTimeOffset.UtcNow);
}

public sealed record DeviceAccessWrite(
    string Operation,
    DeviceAccessAuditEntry Audit,
    long PolicyVersionAtWrite,
    IReadOnlyDictionary<DeviceId, long> EpochsAtWrite);

// Forwards to the phase-2 store and records every write the handlers make,
// with the policy version and every device epoch at the moment of the write.
// BeforeWrite runs before each write reaches the store, to inject a concurrent
// change after the handler's pre-checks.
public sealed class RecordingDeviceAccessStore(
    InMemoryDeviceAccessStore inner,
    IPermissionStore permissions,
    IAuthorizationEpochStore epochs,
    Func<IReadOnlyList<Device>> devices) : IDeviceRoleStore, IDeviceMembershipStore
{
    public List<DeviceAccessWrite> Writes { get; } = [];
    public List<string> Attempts { get; } = [];
    public Action? BeforeWrite { get; set; }
    // Roles that read back as missing, as if lost between a membership read and a role read.
    public HashSet<DeviceRoleId> Unreadable { get; } = [];

    public DeviceRole? Find(DeviceRoleId id) => Unreadable.Contains(id) ? null : inner.Find(id);
    public IReadOnlyList<DeviceRole> ListByOrganization(OrganizationId organizationId) => inner.ListByOrganization(organizationId);
    public DeviceMembership? Find(UserId userId, DeviceId deviceId) => inner.Find(userId, deviceId);
    public IReadOnlyList<DeviceMembership> ListByDevice(DeviceId deviceId) => inner.ListByDevice(deviceId);
    public IReadOnlyList<DeviceMembership> ListByRole(DeviceRoleId roleId) => inner.ListByRole(roleId);
    public IReadOnlyList<DeviceMembership> ListByUser(UserId userId) => inner.ListByUser(userId);

    public void Add(DeviceRole role, DeviceAccessAuditEntry audit) =>
        Write(nameof(Add), audit, () => inner.Add(role, audit));

    public void Update(DeviceRole role, DeviceAccessAuditEntry audit) =>
        Write(nameof(Update), audit, () => inner.Update(role, audit));

    public void Remove(DeviceRoleId id, int expectedVersion, DeviceAccessAuditEntry audit) =>
        Write("RemoveRole", audit, () => inner.Remove(id, expectedVersion, audit));

    public void Upsert(DeviceMembership membership, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit) =>
        Write(nameof(Upsert), audit, () => inner.Upsert(membership, expectedRoleId, audit));

    public bool Remove(UserId userId, DeviceId deviceId, DeviceRoleId? expectedRoleId, DeviceAccessAuditEntry audit)
    {
        var removed = false;
        Write("RemoveMembership", audit, () => removed = inner.Remove(userId, deviceId, expectedRoleId, audit));
        return removed;
    }

    private void Write(string operation, DeviceAccessAuditEntry audit, Action write)
    {
        Attempts.Add(operation);
        BeforeWrite?.Invoke();
        var policyVersion = permissions.PolicyVersion;
        var epochsAtWrite = devices().ToDictionary(x => x.Id, x => epochs.Current(x.Id));
        write();
        Writes.Add(new DeviceAccessWrite(operation, audit, policyVersion, epochsAtWrite));
    }
}

public sealed record EpochAdvance(IReadOnlyList<DeviceId> Devices, long PolicyVersionAtAdvance, bool Batch);

// Forwards to the in-memory epoch store and records each Advance and each
// AdvanceMany call with the policy version at that moment. ThrowOnAdvance
// simulates a failing post-commit signal.
public sealed class RecordingEpochStore(IPermissionStore permissions) : IAuthorizationEpochStore
{
    private readonly InMemoryAuthorizationEpochStore _inner = new();

    public List<EpochAdvance> Advances { get; } = [];
    public bool ThrowOnAdvance { get; set; }

    public long Current(DeviceId deviceId) => _inner.Current(deviceId);

    public long Advance(DeviceId deviceId)
    {
        if (ThrowOnAdvance)
            throw new InvalidOperationException("EPOCH_STORE_UNAVAILABLE");
        Advances.Add(new EpochAdvance([deviceId], permissions.PolicyVersion, Batch: false));
        return _inner.Advance(deviceId);
    }

    public void AdvanceMany(IReadOnlyCollection<DeviceId> deviceIds)
    {
        if (ThrowOnAdvance)
            throw new InvalidOperationException("EPOCH_STORE_UNAVAILABLE");
        Advances.Add(new EpochAdvance(deviceIds.ToArray(), permissions.PolicyVersion, Batch: true));
        _inner.AdvanceMany(deviceIds);
    }
}

public sealed class TestUserScopes : IUserScopeStore
{
    private readonly Dictionary<UserId, OrganizationId?> _users = [];
    private readonly HashSet<UserId> _masterAdmins = [];

    public void Add(UserId user, OrganizationId? organization) => _users[user] = organization;

    public void AddMasterAdmin(UserId user, OrganizationId? organization = null)
    {
        _users[user] = organization;
        _masterAdmins.Add(user);
    }

    public void Delete(UserId user)
    {
        _users.Remove(user);
        _masterAdmins.Remove(user);
    }

    // Runs inside IsMasterAdmin, to hold a grant decision at that point.
    public Action<UserId>? OnIsMasterAdmin { get; set; }

    public bool Exists(UserId userId) => _users.ContainsKey(userId);
    public OrganizationId? Organization(UserId userId) => _users.GetValueOrDefault(userId);

    public bool IsMasterAdmin(UserId userId)
    {
        OnIsMasterAdmin?.Invoke(userId);
        return _masterAdmins.Contains(userId);
    }
}

// Forwards every IPermissionStore call to the real store and fails Advance on
// demand. A runtime proxy rather than a test implementer, so store members can
// change without touching the tests.
public class AdvanceFailingPermissionStore : DispatchProxy
{
    private IPermissionStore _inner = null!;

    public bool ThrowOnAdvance { get; set; }

    public static (IPermissionStore Store, AdvanceFailingPermissionStore Control) Wrap(IPermissionStore inner)
    {
        var store = Create<IPermissionStore, AdvanceFailingPermissionStore>();
        var control = (AdvanceFailingPermissionStore)(object)store;
        control._inner = inner;
        return (store, control);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (ThrowOnAdvance && targetMethod!.Name == nameof(IPermissionStore.Advance))
            throw new InvalidOperationException("POLICY_VERSION_UNAVAILABLE");
        try
        {
            return targetMethod!.Invoke(_inner, args);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(error.InnerException);
            throw;
        }
    }
}
