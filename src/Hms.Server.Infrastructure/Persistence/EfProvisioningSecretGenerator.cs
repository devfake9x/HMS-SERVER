using System.Security.Cryptography;
using System.Text.Json;
using Hms.Server.Application.Licensing;
using Hms.Server.Application.Provisioning;
using Hms.Server.Domain.Devices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfProvisioningSecretGenerator(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    ISecretRootPolicy secretRoot,
    IBrokerCredentialProvider brokerCredentials) : IProvisioningSecretGenerator
{
    private const string ProtectorPurpose = "Hms.Server.DeviceKeyset.v1";
    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public ProvisioningSecrets Generate(DeviceId deviceId, int generation)
    {
        if (generation < 1)
            throw new InvalidOperationException("KEY_GENERATION_INVALID");

        secretRoot.EnsureReady();
        var existing = db.DeviceKeysets.AsNoTracking()
            .SingleOrDefault(x => x.DeviceId == deviceId.Value && x.Generation == generation);
        if (existing is not null)
            return Unprotect(existing);

        var generated = Create(deviceId, generation);
        var record = new DeviceKeysetRecord
        {
            DeviceId = deviceId.Value,
            Generation = generation,
            ProtectedPayload = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(generated)),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DeviceKeysets.Add(record);

        try
        {
            db.SaveChanges();
            return generated;
        }
        catch (DbUpdateException)
        {
            // Another replica may have won the unique insert. Reload that
            // winner instead of returning a second credential set.
            db.Entry(record).State = EntityState.Detached;
            var winner = db.DeviceKeysets.AsNoTracking()
                .SingleOrDefault(x => x.DeviceId == deviceId.Value && x.Generation == generation);
            if (winner is null)
                throw;
            return Unprotect(winner);
        }
    }

    private ProvisioningSecrets Create(DeviceId deviceId, int generation)
    {
        var credentials = brokerCredentials.CreatePrimary(deviceId, generation);
        return new ProvisioningSecrets(
            credentials.Username,
            credentials.Password,
            RandomNumberGenerator.GetBytes(32),
            generation);
    }

    private ProvisioningSecrets Unprotect(DeviceKeysetRecord record)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ProvisioningSecrets>(
                _protector.Unprotect(record.ProtectedPayload));
            if (payload is null || payload.KeyGeneration != record.Generation ||
                string.IsNullOrWhiteSpace(payload.Username) ||
                string.IsNullOrWhiteSpace(payload.Password) ||
                payload.CommandKey is null ||
                payload.CommandKey.Length != 32)
            {
                throw new InvalidOperationException("KEYSET_PAYLOAD_INVALID");
            }

            return payload;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("KEYSET_PAYLOAD_INVALID", ex);
        }
    }
}
