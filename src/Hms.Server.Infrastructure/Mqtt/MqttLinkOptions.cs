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
}
