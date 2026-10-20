using Hms.Server.Application.Management;
using Hms.Server.Application.Provisioning;
using Hms.Server.Application.Licensing;
using Hms.Server.Domain.Devices;
using Hms.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.UnitTests;

public sealed class ProvisioningTests
{
    [Fact]
    public void Bundle_is_one_time_and_contains_a_32_byte_command_key()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Hms.Server.Domain.Organizations.Organization.Create("Master");
        organizations.Add(organization);
        var device = new CreateDeviceHandler(organizations, devices).Handle(new CreateDeviceRequest(
            "ESP-001",
            organization.Id,
            null,
            null,
            null));
        var store = new InMemoryProvisioningBundleStore();
        var handler = new CreateProvisioningBundleHandler(devices, new RandomProvisioningSecretGenerator(), store);

        var metadata = handler.Handle(new DeviceId(device.Id), "broker.example", 1883);
        var bundle = store.Consume(metadata.BundleId);

        Assert.NotNull(bundle);
        Assert.Equal("PRIMARY_ONLY", bundle.BrokerMode);
        Assert.Equal(32, Convert.FromBase64String(bundle.CommandKeyBase64).Length);
        Assert.True(ProvisioningBundleIntegrity.IsValid(bundle));
        Assert.Null(store.Consume(metadata.BundleId));
    }

    [Fact]
    public void Wrong_device_cannot_burn_a_bundle()
    {
        var organization = Hms.Server.Domain.Organizations.Organization.Create("Master");
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        organizations.Add(organization);
        var device = new CreateDeviceHandler(organizations, devices).Handle(new CreateDeviceRequest("ESP-001", organization.Id, null, null, null));
        var store = new InMemoryProvisioningBundleStore();
        var handler = new CreateProvisioningBundleHandler(devices, new RandomProvisioningSecretGenerator(), store);
        var metadata = handler.Handle(new DeviceId(device.Id), "broker.example", 1883);

        Assert.Null(store.Consume(DeviceId.New(), metadata.BundleId));
        Assert.NotNull(store.Consume(new DeviceId(device.Id), metadata.BundleId));
    }

    [Fact]
    public async Task Concurrent_consumers_can_consume_a_bundle_only_once()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Hms.Server.Domain.Organizations.Organization.Create("Master");
        organizations.Add(organization);
        var device = new CreateDeviceHandler(organizations, devices).Handle(
            new CreateDeviceRequest("ESP-001", organization.Id, null, null, null));
        var store = new InMemoryProvisioningBundleStore();
        var handler = new CreateProvisioningBundleHandler(devices, new RandomProvisioningSecretGenerator(), store);
        var metadata = handler.Handle(new DeviceId(device.Id), "broker.example", 1883);

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ =>
            Task.Run(() => store.Consume(metadata.BundleId))));

        Assert.Single(results, bundle => bundle is not null);
        Assert.Equal(63, results.Count(bundle => bundle is null));
    }

    [Fact]
    public void Unsupported_broker_mode_is_rejected_explicitly()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Hms.Server.Domain.Organizations.Organization.Create("Master");
        organizations.Add(organization);
        var device = new CreateDeviceHandler(organizations, devices).Handle(new CreateDeviceRequest(
            "ESP-001", organization.Id, null, null, null));
        var handler = new CreateProvisioningBundleHandler(
            devices,
            new RandomProvisioningSecretGenerator(),
            new InMemoryProvisioningBundleStore());

        var error = Assert.Throws<InvalidOperationException>(() => handler.Handle(
            new DeviceId(device.Id), "broker.example", 1883, "BOTH"));

        Assert.Equal("UNSUPPORTED_BROKER_MODE", error.Message);
    }

    [Fact]
    public void Revoked_device_cannot_create_a_new_bundle_or_generate_secrets()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Hms.Server.Domain.Organizations.Organization.Create("Master");
        organizations.Add(organization);
        var device = new CreateDeviceHandler(organizations, devices).Handle(new CreateDeviceRequest(
            "ESP-001",
            organization.Id,
            null,
            null,
            null));
        var deviceEntity = devices.Find(new DeviceId(device.Id))!;
        deviceEntity.Revoke();
        devices.Update(deviceEntity);

        var secrets = new CountingSecretGenerator();
        var store = new InMemoryProvisioningBundleStore();
        var handler = new CreateProvisioningBundleHandler(devices, secrets, store);

        var error = Assert.Throws<InvalidOperationException>(() => handler.Handle(
            new DeviceId(device.Id), "broker.example", 1883));

        Assert.Equal("DEVICE_REVOKED", error.Message);
        Assert.Equal(0, secrets.Calls);
        Assert.Null(store.Consume(Guid.NewGuid()));
    }

    [Fact]
    public void Oversized_broker_host_is_rejected_before_bundle_creation()
    {
        var organizations = new InMemoryOrganizationStore();
        var devices = new InMemoryDeviceStore();
        var organization = Hms.Server.Domain.Organizations.Organization.Create("Master");
        organizations.Add(organization);
        var device = new CreateDeviceHandler(organizations, devices).Handle(new CreateDeviceRequest(
            "ESP-001", organization.Id, null, null, null));
        var store = new InMemoryProvisioningBundleStore();
        var handler = new CreateProvisioningBundleHandler(devices, new RandomProvisioningSecretGenerator(), store);

        var error = Assert.Throws<InvalidOperationException>(() => handler.Handle(
            new DeviceId(device.Id), new string('a', 257), 1883));

        Assert.Equal("BROKER_ENDPOINT_INVALID", error.Message);
        Assert.Null(store.Consume(Guid.NewGuid()));
    }

    [Fact]
    public void Keyset_is_stable_for_the_same_device_generation()
    {
        var generator = new RandomProvisioningSecretGenerator();
        var device = DeviceId.New();
        var first = generator.Generate(device, 7);
        var second = generator.Generate(device, 7);

        Assert.Equal(first.Username, second.Username);
        Assert.Equal(first.Password, second.Password);
        Assert.Equal(first.CommandKey, second.CommandKey);
        Assert.NotEqual(first.CommandKey, generator.Generate(device, 8).CommandKey);
    }

    [Fact]
    public void Ef_keyset_survives_a_new_generator_instance()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Ef_keyset_survives_a_new_generator_instance))
            .Options;
        var protection = new EphemeralDataProtectionProvider();
        var secretRoot = new FailClosedSecretRootPolicy(configured: true);
        var device = DeviceId.New();

        ProvisioningSecrets first;
        using (var firstDb = new ApplicationDbContext(options))
        {
            first = new EfProvisioningSecretGenerator(
                firstDb,
                protection,
                secretRoot,
                new RandomBrokerCredentialProvider()).Generate(device, 3);
        }

        using (var secondDb = new ApplicationDbContext(options))
        {
            var second = new EfProvisioningSecretGenerator(
                secondDb,
                protection,
                secretRoot,
                new RandomBrokerCredentialProvider()).Generate(device, 3);

            Assert.Equal(first.Username, second.Username);
            Assert.Equal(first.Password, second.Password);
            Assert.Equal(first.CommandKey, second.CommandKey);
        }
    }

    private sealed class CountingSecretGenerator : IProvisioningSecretGenerator
    {
        public int Calls { get; private set; }

        public ProvisioningSecrets Generate(DeviceId deviceId, int generation)
        {
            Calls++;
            throw new InvalidOperationException("SECRET_GENERATION_SHOULD_NOT_RUN");
        }
    }
}
