namespace Reactor.Core.Metrics;

/// <summary>
/// One poll cycle of normalized readings. A metric that is missing from
/// <see cref="Values"/> is simply unavailable on this machine - callers use
/// <see cref="Get"/> and get a null back rather than an exception.
/// </summary>
public sealed class MetricsSnapshot
{
    private static readonly IReadOnlyDictionary<MetricId, double> NoValues =
        new Dictionary<MetricId, double>();

    public MetricsSnapshot(
        DateTime timestampUtc,
        IReadOnlyDictionary<MetricId, double> values,
        IReadOnlyList<double> coreLoads,
        IReadOnlyDictionary<MetricId, double>? limits = null)
    {
        TimestampUtc = timestampUtc;
        Values = values;
        CoreLoads = coreLoads;
        Limits = limits ?? NoValues;
    }

    /// <summary>A snapshot in which nothing could be read. Used when a poll
    /// fails, so the UI blanks to "unavailable" instead of holding stale
    /// numbers on screen.</summary>
    public static MetricsSnapshot Unavailable(DateTime timestampUtc) =>
        new(timestampUtc, NoValues, Array.Empty<double>());

    public DateTime TimestampUtc { get; }

    public IReadOnlyDictionary<MetricId, double> Values { get; }

    /// <summary>Per-core CPU load, in core order. Empty when unavailable.</summary>
    public IReadOnlyList<double> CoreLoads { get; }

    /// <summary>
    /// Device-reported limits (firmware warning/critical points). Deliberately
    /// separate from <see cref="Values"/>: these are constants, and treating
    /// them as readings is what once made a drive appear to sit at 88 C.
    /// </summary>
    public IReadOnlyDictionary<MetricId, double> Limits { get; }

    public double? Get(MetricId id) => Values.TryGetValue(id, out var v) ? v : null;

    public double? GetLimit(MetricId id) => Limits.TryGetValue(id, out var v) ? v : null;

    public bool Has(MetricId id) => Values.ContainsKey(id);

    /// <summary>True when this poll produced no usable telemetry at all.</summary>
    public bool IsEmpty => Values.Count == 0;
}
