namespace Reactor.Core.Telemetry;

public readonly record struct GraphSample(
    double Seconds, double? CpuLoad, double? CpuTemperature,
    double? GpuLoad, double? GpuTemperature, double? Power, bool BreakBefore = false)
{
    public double? Read(HistoryChannel channel) => channel switch
    {
        HistoryChannel.CpuLoad => CpuLoad,
        HistoryChannel.CpuTemperature => CpuTemperature,
        HistoryChannel.GpuLoad => GpuLoad,
        HistoryChannel.GpuTemperature => GpuTemperature,
        HistoryChannel.TotalPower => Power,
        _ => null
    };
}

/// <summary>Shared viewport and bounded, timestamped history for the dashboard graphs. UI thread only.</summary>
public sealed class GraphTimeline(double windowSeconds, double sampleTolerance = 1)
{
    public const int RetentionSeconds = 30 * 60;
    private readonly List<GraphSample> _samples = [];
    private int _first;
    private double? _reviewEnd;

    public event Action? Changed;
    public double SampleTolerance { get; } = sampleTolerance;
    public double WindowSeconds { get; } = windowSeconds;
    public double Latest => _samples.Count == 0 ? 0 : _samples[^1].Seconds;
    public double End => _reviewEnd ?? Latest;
    public bool IsLive => _reviewEnd is null;
    private double EarliestEnd => Math.Min(Latest, _samples[_first].Seconds + WindowSeconds);

    public void Append(GraphSample sample)
    {
        if (_samples.Count > 0 && sample.Seconds <= Latest) return;
        _samples.Add(sample);
        while (_samples[_first].Seconds < Latest - RetentionSeconds) _first++;
        if (_first >= 256)
        {
            _samples.RemoveRange(0, _first);
            _first = 0;
        }

        if (_reviewEnd is { } end) _reviewEnd = Math.Max(end, EarliestEnd);
        Changed?.Invoke();
    }

    public void Scroll(double seconds)
    {
        if (_samples.Count == 0) return;
        double end = Math.Clamp(End + seconds, EarliestEnd, Latest);
        _reviewEnd = end >= Latest ? null : end;
        Changed?.Invoke();
    }

    public void GoLive()
    {
        _reviewEnd = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _samples.Clear();
        _first = 0;
        GoLive();
    }

    public IEnumerable<GraphSample> VisibleSamples()
    {
        double start = End - WindowSeconds;
        double end = End;
        for (int i = _first; i < _samples.Count; i++)
        {
            var sample = _samples[i];
            if (sample.Seconds > end) yield break;
            if (sample.Seconds >= start) yield return sample;
        }
    }
}
