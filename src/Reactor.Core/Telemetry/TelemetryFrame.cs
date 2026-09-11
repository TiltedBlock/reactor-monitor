using Reactor.Core.Metrics;

namespace Reactor.Core.Telemetry;

/// <summary>
/// Everything the presentation layer needs for one refresh, assembled off the
/// UI thread. History arrays are copies, so the UI never reads a buffer that
/// the poll loop is concurrently writing.
/// </summary>
public sealed class TelemetryFrame
{
    public required MetricsSnapshot Snapshot { get; init; }
    public required DerivedMetrics Derived { get; init; }
    public required SessionSnapshot Session { get; init; }
    public required SystemStatus Status { get; init; }
    public required IReadOnlyDictionary<HistoryChannel, double?[]> History { get; init; }
    public required IReadOnlyList<double> CoreLoads { get; init; }
    public DateTime TimestampUtc => Snapshot.TimestampUtc;
}
