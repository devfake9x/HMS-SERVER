using System.Buffers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Domain.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace Hms.Server.Infrastructure.Mqtt;

/// <summary>
/// The server's single broker session. It keeps the connection up, subscribes to the device
/// topics of every bound device, hands inbound messages to the application layer and publishes
/// commands. It holds no command or action logic (ADR 260927 modular monolith).
/// </summary>
public sealed class MqttDeviceLinkService : BackgroundService, IDeviceLinkTransportStatus, IMqttCommandPublisher
{
    private sealed record InboundMessage(DeviceId DeviceId, DeviceInboundKind Kind, byte[] Payload, DateTimeOffset ReceivedAt);

    private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromSeconds(2);

    private readonly MqttLinkOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MqttDeviceLinkService> _logger;
    private readonly IMqttClient _client;
    private readonly X509Certificate2? _caCertificate;
    private readonly Channel<InboundMessage> _inbound = Channel.CreateBounded<InboundMessage>(
        new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
    private readonly HashSet<string> _subscribed = new(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, (DeviceId DeviceId, DeviceInboundKind Kind)> _routes =
        new Dictionary<string, (DeviceId, DeviceInboundKind)>();
    private DateTimeOffset? _connectedSince;
    private string? _lastError;

    public MqttDeviceLinkService(
        IOptions<MqttLinkOptions> options,
        IServiceScopeFactory scopes,
        ILogger<MqttDeviceLinkService> logger)
    {
        _options = options.Value;
        _options.Validate();
        _scopes = scopes;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        if (!string.IsNullOrWhiteSpace(_options.CaCertificatePath))
            _caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(_options.CaCertificatePath));
    }

    public bool IsConnected => _client.IsConnected;

    public DeviceLinkTransportStatus Current => new(
        Enabled: true,
        Connected: _client.IsConnected,
        Broker: $"{_options.Host}:{_options.Port}",
        ConnectedSince: _client.IsConnected ? _connectedSince : null,
        LastError: _lastError);

    /// <summary>Publishes a signed command at QoS 1, retain=false, after making sure the device's
    /// reply topic is subscribed (spec R2).</summary>
    public async ValueTask<string?> PublishCommandAsync(
        string commandTopic,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
            return "MQTT_NOT_CONNECTED";

        var marker = commandTopic.LastIndexOf("/cmd/", StringComparison.Ordinal);
        if (marker <= 0)
            return "MQTT_COMMAND_TOPIC_INVALID";
        var responseTopic = commandTopic[..marker] + "/cmd/res";
        if (!IsSubscribed(responseTopic))
            await RefreshSubscriptionsAsync(cancellationToken);
        if (!IsSubscribed(responseTopic))
            return "MQTT_RESPONSE_SUBSCRIPTION_MISSING";

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(commandTopic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();
        var result = await _client.PublishAsync(message, cancellationToken);
        return result.IsSuccess ? null : $"MQTT_PUBLISH_{result.ReasonCode}".ToUpperInvariant();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processing = ProcessInboundAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                    await ConnectAsync(stoppingToken);
                await RefreshSubscriptionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogWarning("MQTT link to {Host}:{Port}: {Error}", _options.Host, _options.Port, ex.Message);
            }

            // Separate from the connection work: commands published before a broker outage must
            // still reach outcome-unknown while the broker is down.
            try
            {
                await SweepTimeoutsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sweeping timed-out device commands failed");
            }

            try
            {
                await Task.Delay(_client.IsConnected
                    ? HousekeepingInterval
                    : TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _inbound.Writer.TryComplete();
        await processing;
        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MQTT disconnect on shutdown failed");
            }
        }
    }

    public override void Dispose()
    {
        _client.Dispose();
        _caCertificate?.Dispose();
        _subscriptionGate.Dispose();
        base.Dispose();
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
            .WithTimeout(TimeSpan.FromSeconds(10));
        if (!string.IsNullOrEmpty(_options.Username))
            builder = builder.WithCredentials(_options.Username, _options.Password);
        if (_options.UseTls)
        {
            builder = builder.WithTlsOptions(tls => tls
                .UseTls()
                .WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13)
                .WithCertificateValidationHandler(ValidateBrokerCertificate));
        }

        var result = await _client.ConnectAsync(builder.Build(), cancellationToken);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
            throw new InvalidOperationException($"MQTT connect refused: {result.ResultCode}");

        // A clean session starts with no subscriptions on the broker side.
        lock (_subscribed)
            _subscribed.Clear();

        _connectedSince = DateTimeOffset.UtcNow;
        _lastError = null;
        _logger.LogInformation("MQTT link connected to {Host}:{Port} as {ClientId}", _options.Host, _options.Port, _options.ClientId);
    }

    private bool ValidateBrokerCertificate(MqttClientCertificateValidationEventArgs args)
    {
        if (args.SslPolicyErrors == SslPolicyErrors.None)
            return true;
        if (_options.AllowUntrustedCertificate)
        {
            _logger.LogWarning("Accepting an untrusted MQTT broker certificate ({Errors}); development only", args.SslPolicyErrors);
            return true;
        }

        // A name mismatch or missing certificate is never excused; only the chain may be
        // re-checked against the configured CA.
        if (_caCertificate is null || args.Certificate is null ||
            (args.SslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_caCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var certificate = X509CertificateLoader.LoadCertificate(args.Certificate.GetRawCertData());
        return chain.Build(certificate);
    }

    private async Task RefreshSubscriptionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceMqttBinding> bindings;
        using (var scope = _scopes.CreateScope())
            bindings = scope.ServiceProvider.GetRequiredService<IDeviceMqttBindingStore>().List();

        var routes = new Dictionary<string, (DeviceId, DeviceInboundKind)>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            foreach (var (suffix, kind) in DeviceLinkTopics.InboundSuffixes)
                routes[$"{binding.TopicBase}/{suffix}"] = (binding.DeviceId, kind);
        }

        _routes = routes;
        if (!_client.IsConnected)
            return;

        await _subscriptionGate.WaitAsync(cancellationToken);
        try
        {
            string[] missing;
            lock (_subscribed)
                missing = routes.Keys.Where(topic => !_subscribed.Contains(topic)).ToArray();
            if (missing.Length > 0)
            {
                var subscribe = new MqttClientSubscribeOptionsBuilder();
                foreach (var topic in missing)
                    subscribe = subscribe.WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce);
                var result = await _client.SubscribeAsync(subscribe.Build(), cancellationToken);
                foreach (var item in result.Items)
                {
                    if (item.ResultCode is MqttClientSubscribeResultCode.GrantedQoS0
                        or MqttClientSubscribeResultCode.GrantedQoS1
                        or MqttClientSubscribeResultCode.GrantedQoS2)
                    {
                        lock (_subscribed)
                            _subscribed.Add(item.TopicFilter.Topic);
                    }
                    else
                        _logger.LogWarning("Broker refused subscription {Topic}: {Code}", item.TopicFilter.Topic, item.ResultCode);
                }
            }

            string[] stale;
            lock (_subscribed)
                stale = _subscribed.Where(topic => !routes.ContainsKey(topic)).ToArray();
            if (stale.Length > 0)
            {
                var unsubscribe = new MqttClientUnsubscribeOptionsBuilder();
                foreach (var topic in stale)
                    unsubscribe = unsubscribe.WithTopicFilter(topic);
                await _client.UnsubscribeAsync(unsubscribe.Build(), cancellationToken);
                lock (_subscribed)
                    _subscribed.ExceptWith(stale);
            }
        }
        finally
        {
            _subscriptionGate.Release();
        }
    }

    private bool IsSubscribed(string topic)
    {
        lock (_subscribed)
            return _subscribed.Contains(topic);
    }

    private async Task SweepTimeoutsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DeviceCommandResponseCorrelator>()
            .SweepExpiredAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        if (_routes.TryGetValue(topic, out var route))
        {
            var message = new InboundMessage(route.DeviceId, route.Kind, args.ApplicationMessage.Payload.ToArray(), DateTimeOffset.UtcNow);
            if (!_inbound.Writer.TryWrite(message))
                _logger.LogWarning("MQTT inbound queue full; dropped a message on {Topic}", topic);
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (args.ClientWasConnected)
        {
            _lastError = args.Exception?.Message ?? args.Reason.ToString();
            _logger.LogWarning("MQTT link disconnected: {Reason}", _lastError);
        }

        return Task.CompletedTask;
    }

    private async Task ProcessInboundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _inbound.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<DeviceInboundMessageHandler>()
                        .HandleAsync(message.DeviceId, message.Kind, message.Payload, message.ReceivedAt, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Handling {Kind} from device {DeviceId} failed", message.Kind, message.DeviceId.Value);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
