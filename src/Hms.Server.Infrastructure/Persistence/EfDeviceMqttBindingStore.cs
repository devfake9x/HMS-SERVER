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
            // Only a new row takes the allowlist from the caller; see the interface.
            record = new DeviceMqttBindingRecord
            {
                DeviceId = binding.DeviceId.Value,
                AllowedOutputChannels = FormatChannels(binding.AllowedOutputChannels)
            };
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

    public bool SetAllowedOutputChannels(DeviceId deviceId, IReadOnlyList<int>? channels)
    {
        var stored = FormatChannels(channels);
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            // One column, one statement: nothing read earlier is written back.
            return db.DeviceMqttBindings
                .Where(x => x.DeviceId == deviceId.Value)
                .ExecuteUpdate(setters => setters
                    .SetProperty(x => x.AllowedOutputChannels, stored)
                    .SetProperty(x => x.UpdatedAt, now)) > 0;
        }

        var record = db.DeviceMqttBindings.SingleOrDefault(x => x.DeviceId == deviceId.Value);
        if (record is null)
            return false;
        record.AllowedOutputChannels = stored;
        record.UpdatedAt = now;
        db.SaveChanges();
        return true;
    }

    private static string? FormatChannels(IReadOnlyList<int>? channels) =>
        channels is null ? null : string.Join(',', channels);

    private static DeviceMqttBinding ToBinding(DeviceMqttBindingRecord record) =>
        new(new DeviceId(record.DeviceId), record.TopicPrefix, record.MqttDeviceId,
            record.KeyVersion is { } version ? (uint)version : null,
            ParseChannels(record.AllowedOutputChannels));

    // A stored value that does not parse allows nothing rather than everything.
    private static IReadOnlyList<int>? ParseChannels(string? stored)
    {
        if (stored is null)
            return null;
        var parts = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var channels = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var channel))
                return [];
            channels.Add(channel);
        }

        return channels;
    }
}
