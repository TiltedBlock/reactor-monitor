using Reactor.Core.Metrics;

namespace Reactor.Core.Telemetry;

/// <summary>The handful of series the dashboard actually graphs.</summary>
public enum HistoryChannel
{
    CpuLoad,
    CpuTemperature,
    GpuLoad,
    GpuTemperature,
    TotalPower,
    MemoryLoad
}

/// <summary>Rolling history for every graphed channel, sized from the poll interval.</summary>
public sealed class HistoryStore
{
    private readonly Dictionary<HistoryChannel, RollingSeries> _series;

    public HistoryStore(int capacity)
    {
        Capacity = capacity;
        _series = Enum.GetValues<HistoryChannel>()
            .ToDictionary(c => c, _ => new RollingSeries(capacity));
    }

    /// <summary>Sizes the buffers so they hold <paramref name="seconds"/> of samples.</summary>
    public static HistoryStore ForWindow(int seconds, int pollIntervalMs) =>
        new(Math.Clamp((int)Math.Ceiling(seconds * 1000.0 / Math.Max(1, pollIntervalMs)), 16, 4096));

    public int Capacity { get; }

    public RollingSeries this[HistoryChannel channel] => _series[channel];

    public void Append(MetricsSnapshot snapshot, double? totalPower)
    {
        _series[HistoryChannel.CpuLoad].Add(snapshot.Get(MetricId.CpuLoad));
        _series[HistoryChannel.CpuTemperature].Add(snapshot.Get(MetricId.CpuTemperature));
        _series[HistoryChannel.GpuLoad].Add(snapshot.Get(MetricId.GpuLoad));
        _series[HistoryChannel.GpuTemperature].Add(snapshot.Get(MetricId.GpuTemperature));
        _series[HistoryChannel.TotalPower].Add(totalPower);
        _series[HistoryChannel.MemoryLoad].Add(snapshot.Get(MetricId.MemoryLoad));
    }

    public void Clear()
    {
        foreach (var s in _series.Values) s.Clear();
    }
}
