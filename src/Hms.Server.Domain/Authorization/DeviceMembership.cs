using Hms.Server.Domain.Devices;

namespace Hms.Server.Domain.Authorization;

public sealed record DeviceMembership(
    UserId UserId,
    DeviceId DeviceId,
    DeviceRoleId RoleId,
    UserId GrantedBy,
    DateTimeOffset GrantedAt);
