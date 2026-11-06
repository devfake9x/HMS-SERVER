namespace Hms.Server.Infrastructure.Mqtt;

/// <summary>The server's own broker session (configuration section <c>Mqtt</c>).</summary>
public sealed class MqttLinkOptions
{
    public const string SectionName = "Mqtt";

    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8883;
    public bool UseTls { get; set; } = true;

    /// <summary>PEM file of the CA that signed the broker certificate, when it is not in the
    /// machine trust store (a lab broker with its own CA).</summary>
    public string? CaCertificatePath { get; set; }

    /// <summary>Development only: accept any broker certificate.</summary>
    public bool AllowUntrustedCertificate { get; set; }

    public string ClientId { get; set; } = "hms-server";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int KeepAliveSeconds { get; set; } = 30;
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>Spec R4: after 30 s without a reply the outcome is unknown.</summary>
    public int ResponseTimeoutSeconds { get; set; } = 30;

    /// <summary>Server cap on <c>control.pulse</c> duration. The spec leaves the device's own
    /// limit to its HTTP configuration (§6.4), so this is a server safety policy.</summary>
    public int MaxPulseMs { get; set; } = Application.DeviceLink.OutputCommandPolicy.DefaultMaxPulseMs;

    /// <summary>Read commands a device may have waiting for a reply at once. The device has one
    /// 16-slot queue and one rate bucket for every client (spec §2/§3) and drops what does not fit
    /// without a reply, so reads are capped to leave room for relay commands, which are not.</summary>
    public int MaxPendingReadsPerDevice { get; set; } = 4;

    /// <summary>Development seed: devices to bind (and, from a secret store only, their command
    /// key) at startup. Production binds devices through the API.</summary>
    public List<MqttDeviceSeedOptions> Devices { get; set; } = [];

    public void Validate()
    {
        if (!Enabled)
            return;
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535)
            throw new InvalidOperationException("MQTT_BROKER_ENDPOINT_INVALID");
        if (string.IsNullOrWhiteSpace(ClientId) || ClientId.Length > 63)
            throw new InvalidOperationException("MQTT_CLIENT_ID_INVALID");
        if (ResponseTimeoutSeconds is < 1 or > 600 || KeepAliveSeconds is < 5 or > 3600 || ReconnectDelaySeconds is < 1 or > 300)
            throw new InvalidOperationException("MQTT_TIMING_INVALID");
        // A command must expire on the device before the server gives up on its reply.
        if (ResponseTimeoutSeconds * 1000L <= Application.Commands.SendDeviceCommandHandler.MaxCommandTtlMs)
            throw new InvalidOperationException("MQTT_TIMING_INVALID");
        if (MaxPulseMs is < 1 or > 3_600_000)
            throw new InvalidOperationException("MQTT_MAX_PULSE_INVALID");
        if (MaxPendingReadsPerDevice is < 1 or > 16)
            throw new InvalidOperationException("MQTT_READ_LIMIT_INVALID");
    }
}

public sealed class MqttDeviceSeedOptions
{
    public Guid DeviceId { get; set; }
    public string TopicPrefix { get; set; } = string.Empty;
    public string MqttDeviceId { get; set; } = string.Empty;
    public uint? KeyVersion { get; set; }

    /// <summary>64 hex characters. Set it through user secrets or the environment, never in a
    /// committed settings file.</summary>
    public string? CommandKeyHex { get; set; }

    /// <summary>Output channels the server may drive. Left out = none (fail closed).</summary>
    public int[]? AllowedOutputChannels { get; set; }
}
