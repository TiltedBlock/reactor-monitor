using System.Globalization;
using Reactor.Core.Configuration;
using Reactor.Core.Metrics;

namespace Reactor.Core.Telemetry;

/// <summary>
/// Single place that decides what the status banner says. Every number it uses
/// comes from <see cref="StatusThresholds"/>; nothing is hardcoded here.
/// </summary>
public sealed class StatusEvaluator
{
    private readonly StatusThresholds _t;

    public StatusEvaluator(StatusThresholds thresholds) => _t = thresholds;

    public SystemStatus Evaluate(MetricsSnapshot snapshot, DerivedMetrics derived)
    {
        if (snapshot.Values.Count == 0) return SystemStatus.Offline;

        var candidates = new List<(SystemState State, string Detail)>();

        var cpuTemp = snapshot.Get(MetricId.CpuTemperature) ?? snapshot.Get(MetricId.CpuHottestCoreTemperature);
        var gpuTemp = snapshot.Get(MetricId.GpuTemperature);
        var hotspot = snapshot.Get(MetricId.GpuHotspotTemperature);
        var driveTemp = snapshot.Get(MetricId.StorageTemperature);

        AddTemperature(candidates, cpuTemp, _t.CpuTempHighC, _t.CpuTempWarnC, "CPU");
        AddTemperature(candidates, gpuTemp, _t.GpuTempHighC, _t.GpuTempWarnC, "GPU");
        AddTemperature(candidates, hotspot, _t.GpuHotspotHighC, _t.GpuHotspotWarnC, "GPU HOTSPOT");

        // Drives publish their own warning and critical points. Believe the
        // firmware over our generic guess when it is available.
        var driveHigh = snapshot.GetLimit(MetricId.StorageWarningTemperature) ?? _t.StorageTempHighC;
        var driveWarn = snapshot.GetLimit(MetricId.StorageCriticalTemperature) ?? _t.StorageTempWarnC;
        if (driveWarn < driveHigh) (driveHigh, driveWarn) = (driveWarn, driveHigh);
        AddTemperature(candidates, driveTemp, driveHigh, driveWarn, "DRIVE");

        AddPowerCeiling(candidates, derived.CpuPower, _t.CpuPowerCeilingWatts, "CPU");

        // When the card reports its own power-limit headroom, believe it in
        // preference to a wattage ceiling we had to guess.
        if (snapshot.Get(MetricId.GpuPowerLimitPercent) is { } budget)
        {
            if (budget >= _t.PowerCeilingRatio * 100.0)
                candidates.Add((SystemState.PowerLimited, $"GPU {Fmt(budget)} % OF LIMIT"));
        }
        else
        {
            AddPowerCeiling(candidates, derived.GpuPower, _t.GpuPowerCeilingWatts, "GPU");
        }

        var cpuLoad = snapshot.Get(MetricId.CpuLoad);
        var gpuLoad = snapshot.Get(MetricId.GpuLoad);
        var peakLoad = Max(cpuLoad, gpuLoad);
        if (peakLoad is { } load)
        {
            var who = cpuLoad >= gpuLoad ? "CPU" : "GPU";
            if (load >= _t.LoadHighPercent)
                candidates.Add((SystemState.HighLoad, $"{who} {Fmt(load)} %"));
            else if (load >= _t.LoadActivePercent)
                candidates.Add((SystemState.Active, $"{who} {Fmt(load)} %"));
        }

        if (candidates.Count == 0)
            return new SystemStatus(SystemState.Nominal, SystemState.Nominal.ToLabel(), "all subsystems within range");

        var best = candidates.OrderByDescending(c => c.State).First();
        return new SystemStatus(best.State, best.State.ToLabel(), best.Detail);
    }

    private void AddTemperature(
        List<(SystemState, string)> into, double? value, double high, double warn, string label)
    {
        if (value is not { } v) return;
        if (v >= warn) into.Add((SystemState.Warning, $"{label} {Fmt(v)} \u00b0C"));
        else if (v >= high) into.Add((SystemState.ThermalLoad, $"{label} {Fmt(v)} \u00b0C"));
    }

    private void AddPowerCeiling(
        List<(SystemState, string)> into, double? value, double ceiling, string label)
    {
        if (value is not { } v || ceiling <= 0) return;
        if (v >= ceiling * _t.PowerCeilingRatio)
            into.Add((SystemState.PowerLimited, $"{label} {Fmt(v)} W"));
    }

    private static double? Max(double? a, double? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static string Fmt(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
