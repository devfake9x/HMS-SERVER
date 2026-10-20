using System.Diagnostics.Metrics;

namespace Hms.Server.Api.Hubs;

/// <summary>
/// Runtime instruments for bounded SignalR backlog pressure. Exporters remain
/// host-configured; the application never depends on a monitoring service.
/// </summary>
public static class SignalRBacklogMetrics
{
    public const string MeterName = "Hms.Server.SignalR";
    public const string DroppedInstrumentName = "hms.signalr.backlog.dropped";
    public const string RetryInstrumentName = "hms.signalr.backlog.retry";
    public const string ResynchronizationInstrumentName = "hms.signalr.backlog.resynchronization";

    private static readonly Meter Meter = new(MeterName, "1.0");
    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(DroppedInstrumentName, "events");
    private static readonly Counter<long> Retry = Meter.CreateCounter<long>(RetryInstrumentName, "attempts");
    private static readonly Counter<long> Resynchronization = Meter.CreateCounter<long>(ResynchronizationInstrumentName, "events");

    public static void RecordDropped() => Dropped.Add(1);

    public static void RecordRetry() => Retry.Add(1);

    public static void RecordResynchronization() => Resynchronization.Add(1);
}
