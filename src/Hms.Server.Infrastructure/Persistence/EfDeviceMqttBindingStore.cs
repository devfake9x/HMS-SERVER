using Hms.Server.Application.DeviceLink;
using Hms.Server.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfDeviceMqttBindingStore(ApplicationDbContext db) : IDeviceMqttBindingStore
{
    public DeviceMqttBinding? Find(DeviceId deviceId) =>
        db.DeviceMqttBindings.AsNoTracking().SingleOrDefault(x => x.DeviceId == deviceId.Value) is { } record
            ? ToBinding(record)
            : null;

    public IReadOnlyList<DeviceMqttBinding> List() =>
        db.DeviceMqttBindings.AsNoTracking().AsEnumerable().Select(ToBinding).ToArray();

    public void Upsert(DeviceMqttBinding binding)
    {
        if (db.DeviceMqttBindings.AsNoTracking().Any(x =>
                x.DeviceId != binding.DeviceId.Value &&
                x.TopicPrefix == binding.TopicPrefix &&
                x.MqttDeviceId == binding.MqttDeviceId))
            throw new InvalidOperationException("MQTT_TOPIC_BASE_IN_USE");

        var record = db.DeviceMqttBindings.SingleOrDefault(x => x.DeviceId == binding.DeviceId.Value);
        if (record is null)
        {
            record = new DeviceMqttBindingRecord { DeviceId = binding.DeviceId.Value };
            db.DeviceMqttBindings.Add(record);
        }

        record.TopicPrefix = binding.TopicPrefix;
        record.MqttDeviceId = binding.MqttDeviceId;
        record.KeyVersion = binding.KeyVersion;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            // The unique (prefix, device id) index decides a concurrent claim of the same base.
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("MQTT_TOPIC_BASE_IN_USE", ex);
        }
    }

    private static DeviceMqttBinding ToBinding(DeviceMqttBindingRecord record) =>
        new(new DeviceId(record.DeviceId), record.TopicPrefix, record.MqttDeviceId,
            record.KeyVersion is { } version ? (uint)version : null);
}
