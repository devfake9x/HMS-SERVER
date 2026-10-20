using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using Hms.Server.Domain.Devices;
using Hms.Server.Application.Management;

namespace Hms.Server.Application.Provisioning;

public sealed record ProvisioningSecrets(
    string Username,
    string Password,
    byte[] CommandKey,
    int KeyGeneration);

public sealed record BrokerCredentialSet(string Username, string Password);

// Infrastructure may replace this with a broker/API-backed implementation without
// changing the provisioning use case.
public interface IBrokerCredentialProvider
{
    BrokerCredentialSet CreatePrimary(DeviceId deviceId, int generation);
}

public sealed record ProvisioningBundle(
    Guid BundleId,
    DeviceId DeviceId,
    string DeviceName,
    string BrokerMode,
    string BrokerHost,
    int BrokerPort,
    string Username,
    string Password,
    string CommandKeyBase64,
    int KeyGeneration,
    DateTimeOffset ExpiresAt,
    string BundleVersion = "hms-provisioning-v1",
    string ImportNonce = "",
    string BundleHash = "");

public sealed record ProvisioningBundleMetadata(
    Guid BundleId,
    DeviceId DeviceId,
    DateTimeOffset ExpiresAt,
    bool Used);

public interface IProvisioningSecretGenerator
{
    ProvisioningSecrets Generate(DeviceId deviceId, int generation);
}

public interface IProvisioningBundleStore
{
    ProvisioningBundleMetadata Save(ProvisioningBundle bundle);
    ProvisioningBundle? Consume(Guid bundleId);
    ProvisioningBundle? Consume(DeviceId expectedDeviceId, Guid bundleId);
}

public static class ProvisioningBundleIntegrity
{
    public static string ComputeHash(ProvisioningBundle bundle)
    {
        var unsigned = bundle with { BundleHash = string.Empty };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(unsigned);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static bool IsValid(ProvisioningBundle bundle) =>
        bundle.BundleHash.Equals(ComputeHash(bundle), StringComparison.OrdinalIgnoreCase);
}

public sealed class CreateProvisioningBundleHandler(
    IDeviceStore devices,
    IProvisioningSecretGenerator secrets,
    IProvisioningBundleStore bundles)
{
    public const int MaxBrokerHostLength = 256;
    public const int MaxSerializedBundleBytes = 16 * 1024;

    public ProvisioningBundleMetadata Handle(
        DeviceId deviceId,
        string brokerHost,
        int brokerPort,
        string brokerMode = "PRIMARY_ONLY")
    {
        if (!brokerMode.Equals("PRIMARY_ONLY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UNSUPPORTED_BROKER_MODE");
        if (string.IsNullOrWhiteSpace(brokerHost) || brokerHost.Length > MaxBrokerHostLength ||
            brokerHost.Any(char.IsControl) || brokerPort is < 1 or > 65535)
            throw new InvalidOperationException("BROKER_ENDPOINT_INVALID");

        var device = devices.Find(deviceId) ?? throw new InvalidOperationException("DEVICE_NOT_FOUND");
        if (device.State == DeviceLifecycleState.Revoked)
            throw new InvalidOperationException("DEVICE_REVOKED");

        var generated = secrets.Generate(device.Id, device.KeyGeneration);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var importNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var unsignedBundle = new ProvisioningBundle(
            Guid.NewGuid(),
            device.Id,
            device.Name,
            "PRIMARY_ONLY",
            brokerHost,
            brokerPort,
            generated.Username,
            generated.Password,
            Convert.ToBase64String(generated.CommandKey),
            generated.KeyGeneration,
            expiresAt,
            ImportNonce: importNonce);
        var bundle = unsignedBundle with { BundleHash = ProvisioningBundleIntegrity.ComputeHash(unsignedBundle) };
        if (JsonSerializer.SerializeToUtf8Bytes(bundle).Length > MaxSerializedBundleBytes)
            throw new InvalidOperationException("PROVISIONING_BUNDLE_TOO_LARGE");

        return bundles.Save(bundle);
    }
}

public sealed class RandomProvisioningSecretGenerator(
    IBrokerCredentialProvider? brokerCredentials = null) : IProvisioningSecretGenerator
{
    // Keep a keyset stable for a device generation; persistence can replace this port later.
    private readonly ConcurrentDictionary<(DeviceId DeviceId, int Generation), ProvisioningSecrets> keysets = new();

    public ProvisioningSecrets Generate(DeviceId deviceId, int generation)
    {
        if (generation < 1)
            throw new InvalidOperationException("KEY_GENERATION_INVALID");
        return keysets.GetOrAdd((deviceId, generation), key =>
        {
            var credentials = (brokerCredentials ?? new RandomBrokerCredentialProvider())
                .CreatePrimary(key.DeviceId, key.Generation);
            return new ProvisioningSecrets(credentials.Username, credentials.Password,
                RandomNumberGenerator.GetBytes(32), key.Generation);
        });
    }
}

public sealed class RandomBrokerCredentialProvider : IBrokerCredentialProvider
{
    public BrokerCredentialSet CreatePrimary(DeviceId deviceId, int generation)
    {
        var passwordBytes = RandomNumberGenerator.GetBytes(32);
        return new(
            $"hms-{deviceId.Value:N}",
            Base64Url(passwordBytes));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

public sealed class InMemoryProvisioningBundleStore : IProvisioningBundleStore
{
    private sealed record Entry(ProvisioningBundle Bundle, bool Used);
    private readonly Dictionary<Guid, Entry> _items = [];
    private readonly object _gate = new();

    public ProvisioningBundleMetadata Save(ProvisioningBundle bundle)
    {
        lock (_gate)
        {
            var entry = new Entry(bundle, false);
            _items.Add(bundle.BundleId, entry);
            return new(bundle.BundleId, bundle.DeviceId, bundle.ExpiresAt, false);
        }
    }

    public ProvisioningBundle? Consume(Guid bundleId)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(bundleId, out var entry) || entry.Used || entry.Bundle.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            _items[bundleId] = entry with { Used = true };
            return entry.Bundle;
        }
    }

    public ProvisioningBundle? Consume(DeviceId expectedDeviceId, Guid bundleId)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(bundleId, out var entry) ||
                entry.Bundle.DeviceId != expectedDeviceId ||
                entry.Used ||
                entry.Bundle.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            _items[bundleId] = entry with { Used = true };
            return entry.Bundle;
        }
    }
}
