using Hms.Server.Application.Licensing;
using Hms.Server.Application.Provisioning;
using Hms.Server.Domain.Devices;
using Hms.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.UnitTests;

public sealed class ProtectedProvisioningBundleStoreTests
{
    [Fact]
    public void Bundle_payload_is_protected_and_consumed_once_for_the_bound_device()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new ApplicationDbContext(options);
        var protector = new EphemeralDataProtectionProvider();
        var store = new EfProvisioningBundleStore(
            db,
            protector,
            new FailClosedSecretRootPolicy(true));
        var deviceId = DeviceId.New();
        db.Devices.Add(new DeviceRecord
        {
            Id = deviceId.Value,
            Name = "ESP-001",
            OwnerOrganizationId = Guid.NewGuid(),
            State = DeviceLifecycleState.Provisioning.ToString(),
            KeyGeneration = 1
        });
        db.SaveChanges();
        var unsigned = new ProvisioningBundle(
            Guid.NewGuid(),
            deviceId,
            "ESP-001",
            "PRIMARY_ONLY",
            "broker.example",
            1883,
            "hms-user",
            "secret",
            Convert.ToBase64String(new byte[32]),
            1,
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImportNonce: "nonce");
        var bundle = unsigned with { BundleHash = ProvisioningBundleIntegrity.ComputeHash(unsigned) };

        store.Save(bundle);

        var row = db.ProvisioningBundles.Single();
        Assert.DoesNotContain("secret", System.Text.Encoding.UTF8.GetString(row.ProtectedPayload));
        Assert.NotEmpty(row.ProtectedPayload);
        Assert.Null(store.Consume(DeviceId.New(), bundle.BundleId));
        var consumed = store.Consume(deviceId, bundle.BundleId);
        Assert.NotNull(consumed);
        Assert.Equal(bundle.BundleId, consumed.BundleId);
        Assert.Equal(bundle.DeviceId, consumed.DeviceId);
        Assert.Equal(bundle.Username, consumed.Username);
        Assert.Equal(bundle.Password, consumed.Password);
        Assert.Equal(bundle.CommandKeyBase64, consumed.CommandKeyBase64);
        Assert.True(ProvisioningBundleIntegrity.IsValid(consumed));
        Assert.Null(store.Consume(deviceId, bundle.BundleId));
    }

    [Fact]
    public void Bundle_save_rejects_an_unknown_device()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new ApplicationDbContext(options);
        var deviceId = DeviceId.New();
        var store = new EfProvisioningBundleStore(
            db,
            new EphemeralDataProtectionProvider(),
            new FailClosedSecretRootPolicy(true));
        var unsigned = new ProvisioningBundle(
            Guid.NewGuid(),
            deviceId,
            "ESP-MISSING",
            "PRIMARY_ONLY",
            "broker.example",
            1883,
            "hms-user",
            "secret",
            Convert.ToBase64String(new byte[32]),
            1,
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImportNonce: "nonce");
        var bundle = unsigned with { BundleHash = ProvisioningBundleIntegrity.ComputeHash(unsigned) };

        var error = Assert.Throws<InvalidOperationException>(() => store.Save(bundle));

        Assert.Equal("DEVICE_NOT_FOUND", error.Message);
        Assert.Empty(db.ProvisioningBundles);
    }

    [Fact]
    public void Bundle_save_rejects_a_revoked_device_even_when_the_handler_checked_earlier()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new ApplicationDbContext(options);
        var deviceId = DeviceId.New();
        db.Devices.Add(new DeviceRecord
        {
            Id = deviceId.Value,
            Name = "ESP-REVOKED",
            OwnerOrganizationId = Guid.NewGuid(),
            State = DeviceLifecycleState.Revoked.ToString(),
            KeyGeneration = 1
        });
        db.SaveChanges();

        var store = new EfProvisioningBundleStore(
            db,
            new EphemeralDataProtectionProvider(),
            new FailClosedSecretRootPolicy(true));
        var unsigned = new ProvisioningBundle(
            Guid.NewGuid(),
            deviceId,
            "ESP-REVOKED",
            "PRIMARY_ONLY",
            "broker.example",
            1883,
            "hms-user",
            "secret",
            Convert.ToBase64String(new byte[32]),
            1,
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImportNonce: "nonce");
        var bundle = unsigned with { BundleHash = ProvisioningBundleIntegrity.ComputeHash(unsigned) };

        var error = Assert.Throws<InvalidOperationException>(() => store.Save(bundle));

        Assert.Equal("DEVICE_REVOKED", error.Message);
        Assert.Empty(db.ProvisioningBundles);
    }
}
