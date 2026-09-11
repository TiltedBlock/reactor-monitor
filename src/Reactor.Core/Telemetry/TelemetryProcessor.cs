using Reactor.Core.Configuration;
using Reactor.Core.Metrics;

namespace Reactor.Core.Telemetry;

/// <summary>
/// The stage between hardware acquisition and the UI: takes raw snapshots,
/// maintains history and session statistics, and produces a ready-to-render
/// <see cref="TelemetryFrame"/>. Pure logic, no hardware and no UI types.
/// </summary>
public sealed class TelemetryProcessor
{
    private readonly AppConfig _config;
    private readonly StatusEvaluator _status;

    public TelemetryProcessor(AppConfig config)
    {
        _config = config;
        _status = new StatusEvaluator(config.Thresholds);
        History = HistoryStore.ForWindow(config.HistorySeconds, config.PollIntervalMs);
        Session = new SessionStatistics();
    }

    public HistoryStore History { get; }

    public SessionStatistics Session { get; }

    public TelemetryFrame Process(MetricsSnapshot snapshot)
    {
        var derived = DerivedMetrics.Compute(snapshot, _config);

        Session.Update(
            snapshot.TimestampUtc,
            snapshot.Get(MetricId.CpuTemperature) ?? snapshot.Get(MetricId.CpuHottestCoreTemperature),
            snapshot.Get(MetricId.GpuTemperature),
            snapshot.Get(MetricId.GpuHotspotTemperature),
            derived.ComponentPower,
            derived.WallPower);

        History.Append(snapshot, derived.ComponentPower);

        var history = Enum.GetValues<HistoryChannel>()
            .ToDictionary(c => c, c => History[c].ToArray());

        return new TelemetryFrame
        {
            Snapshot = snapshot,
            Derived = derived,
            Session = Session.ToSnapshot(_config.ElectricityPricePerKWh),
            Status = _status.Evaluate(snapshot, derived),
            History = history,
            CoreLoads = snapshot.CoreLoads
        };
    }

    public void ResetSession()
    {
        Session.Reset();
        History.Clear();
    }
}
