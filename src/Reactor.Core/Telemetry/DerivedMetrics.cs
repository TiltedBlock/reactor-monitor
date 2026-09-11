using Reactor.Core.Configuration;
using Reactor.Core.Metrics;

namespace Reactor.Core.Telemetry;

/// <summary>
/// Values computed from measured readings rather than read from a sensor.
/// <see cref="IsEstimate"/> is surfaced in the UI so nothing here is mistaken
/// for a direct measurement.
/// </summary>
public sealed record DerivedMetrics(
    double? CpuPower,
    double? GpuPower,
    double? ComponentPower,
    double WallPower,
    bool WallPowerIsEstimate,
    double HeatOutputWatts,
    double HeatOutputBtuPerHour,
    double CostPerHour,
    double? VramUsedMb,
    double? VramTotalMb,
    double? VramPercent,
    double? MemoryUsedGb,
    double? MemoryTotalGb)
{
    public bool IsEstimate => WallPowerIsEstimate;

    public static DerivedMetrics Compute(MetricsSnapshot snapshot, AppConfig config)
    {
        var model = config.WallPower;

        var cpuPower = snapshot.Get(MetricId.CpuPackagePower);
        var gpuPower = snapshot.Get(MetricId.GpuPower);

        double? componentPower = cpuPower is null && gpuPower is null
            ? null
            : (cpuPower ?? 0) + (gpuPower ?? 0);

        // Substitute assumed figures for whatever we could not measure, and flag
        // the result as an estimate whenever we had to.
        var substituted = cpuPower is null || gpuPower is null;
        var cpuForModel = cpuPower ?? model.AssumedCpuWattsWhenUnknown;
        var gpuForModel = gpuPower ?? model.AssumedGpuWattsWhenUnknown;

        // Rest-of-system draw plus PSU conversion losses.
        var wall = (cpuForModel + gpuForModel + model.BaselineWatts) / model.PsuEfficiency;

        // Essentially all electrical input to a PC leaves as heat.
        var heatW = wall;
        var heatBtu = heatW * 3.412142;

        var costPerHour = wall / 1000.0 * config.ElectricityPricePerKWh;

        var vramUsed = snapshot.Get(MetricId.GpuMemoryUsed);
        var vramTotal = snapshot.Get(MetricId.GpuMemoryTotal);
        if (vramTotal is null && vramUsed is not null && snapshot.Get(MetricId.GpuMemoryFree) is { } free)
            vramTotal = vramUsed + free;
        double? vramPercent = vramUsed is not null && vramTotal is > 0
            ? vramUsed / vramTotal * 100.0
            : null;

        var memUsed = snapshot.Get(MetricId.MemoryUsed);
        var memAvail = snapshot.Get(MetricId.MemoryAvailable);
        double? memTotal = memUsed is not null && memAvail is not null ? memUsed + memAvail : null;

        return new DerivedMetrics(
            cpuPower, gpuPower, componentPower,
            wall, substituted,
            heatW, heatBtu, costPerHour,
            vramUsed, vramTotal, vramPercent,
            memUsed, memTotal);
    }
}
