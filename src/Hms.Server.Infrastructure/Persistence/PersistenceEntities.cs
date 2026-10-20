namespace Hms.Server.Infrastructure.Persistence;

public sealed class OrganizationRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public OrganizationRecord? Parent { get; set; }
    public List<OrganizationRecord> Children { get; set; } = [];
}

public sealed class DeviceRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OwnerOrganizationId { get; set; }
    public OrganizationRecord OwnerOrganization { get; set; } = null!;
    public Guid? ProjectId { get; set; }
    public Guid? PackageId { get; set; }
    public Guid? AreaId { get; set; }
    public string State { get; set; } = "Provisioning";
    public int KeyGeneration { get; set; } = 1;
}

public sealed class DeviceAuthorizationEpochRecord
{
    public Guid DeviceId { get; set; }
    public long Epoch { get; set; }
}

public sealed class DeviceKeysetRecord
{
    public Guid DeviceId { get; set; }
    public int Generation { get; set; }
    public byte[] ProtectedPayload { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PolicyVersionRecord
{
    public int Id { get; set; }
    public long Version { get; set; }
}

public sealed class ProjectRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
}

public sealed class PackageRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
}

public sealed class AreaRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
}

public sealed class CommandAuditRecord
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public int KeyGeneration { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PermissionGrantRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Permission { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? DeviceId { get; set; }
    public string? Action { get; set; }
    public bool Deny { get; set; }
    public long PolicyVersion { get; set; }
    public Guid? GrantedBy { get; set; }
    public bool GrantedByMasterAdmin { get; set; }
}

public sealed class DeviceRoleRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<DeviceRolePermissionRecord> Permissions { get; set; } = [];
}

public sealed class DeviceRolePermissionRecord
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;
}

public sealed class DeviceMembershipRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid RoleId { get; set; }
    public Guid GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

public sealed class DeviceAccessAuditRecord
{
    public Guid AuditId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? RoleId { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class LicenseTrustRecord
{
    public string Kid { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
}

public sealed class LicenseTrustAuditRecord
{
    public Guid AuditId { get; set; }
    public string Kid { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class LicenseRecord
{
    public Guid LicenseId { get; set; }
    public Guid DeviceId { get; set; }
    public string Kid { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public int Generation { get; set; }
    public byte[] RawEnvelope { get; set; } = [];
    public string RawSha256 { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LicenseApplyJournalRecord
{
    public Guid JournalId { get; set; }
    public Guid DeviceId { get; set; }
    public int PriorGeneration { get; set; }
    public string? PriorRawSha256 { get; set; }
    public int TargetGeneration { get; set; }
    public string TargetRawSha256 { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProvisioningBundleRecord
{
    public Guid BundleId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public byte[] ProtectedPayload { get; set; } = [];
}
