namespace Reactor.Core.Metrics;

public sealed record MetricDefinition(MetricId Id, string Label, MetricUnit Unit, int Decimals);

/// <summary>Static description of every metric: label, unit and display precision.</summary>
public static class MetricCatalog
{
    private static readonly Dictionary<MetricId, MetricDefinition> Map = new[]
    {
        new MetricDefinition(MetricId.CpuLoad, "CPU LOAD", MetricUnit.Percent, 1),
        new MetricDefinition(MetricId.CpuMaxCoreLoad, "BUSIEST CORE", MetricUnit.Percent, 1),
        new MetricDefinition(MetricId.CpuTemperature, "CPU TEMP", MetricUnit.Celsius, 1),
        new MetricDefinition(MetricId.CpuHottestCoreTemperature, "HOT CORE", MetricUnit.Celsius, 1),
        new MetricDefinition(MetricId.CpuPackagePower, "CPU PKG PWR", MetricUnit.Watt, 1),
        new MetricDefinition(MetricId.CpuCoreClock, "CORE CLOCK", MetricUnit.Megahertz, 0),

        new MetricDefinition(MetricId.GpuLoad, "GPU LOAD", MetricUnit.Percent, 1),
        new MetricDefinition(MetricId.GpuTemperature, "GPU TEMP", MetricUnit.Celsius, 1),
        new MetricDefinition(MetricId.GpuHotspotTemperature, "HOTSPOT", MetricUnit.Celsius, 1),
        new MetricDefinition(MetricId.GpuMemoryTemperature, "VRAM TEMP", MetricUnit.Celsius, 0),
        new MetricDefinition(MetricId.GpuPower, "GPU PWR", MetricUnit.Watt, 1),
        new MetricDefinition(MetricId.GpuPowerLimitPercent, "PWR BUDGET", MetricUnit.Percent, 0),
        new MetricDefinition(MetricId.GpuCoreClock, "GPU CLOCK", MetricUnit.Megahertz, 0),
        new MetricDefinition(MetricId.GpuMemoryUsed, "VRAM USED", MetricUnit.Megabyte, 0),
        new MetricDefinition(MetricId.GpuMemoryTotal, "VRAM TOTAL", MetricUnit.Megabyte, 0),
        new MetricDefinition(MetricId.GpuMemoryFree, "VRAM FREE", MetricUnit.Megabyte, 0),
        new MetricDefinition(MetricId.GpuFanRpm, "GPU FAN", MetricUnit.Rpm, 0),
        new MetricDefinition(MetricId.GpuFanPercent, "GPU FAN", MetricUnit.Percent, 0),

        new MetricDefinition(MetricId.MemoryLoad, "MEMORY", MetricUnit.Percent, 1),
        new MetricDefinition(MetricId.MemoryUsed, "MEM USED", MetricUnit.Gigabyte, 1),
        new MetricDefinition(MetricId.MemoryAvailable, "MEM FREE", MetricUnit.Gigabyte, 1),

        new MetricDefinition(MetricId.StorageTemperature, "DRIVE TEMP", MetricUnit.Celsius, 0),
        new MetricDefinition(MetricId.StorageLoad, "DRIVE USED", MetricUnit.Percent, 0),
        new MetricDefinition(MetricId.StorageWarningTemperature, "DRIVE WARN LIMIT", MetricUnit.Celsius, 0),
        new MetricDefinition(MetricId.StorageCriticalTemperature, "DRIVE CRIT LIMIT", MetricUnit.Celsius, 0),

        new MetricDefinition(MetricId.NetworkDownload, "NET DOWN", MetricUnit.BytesPerSecond, 0),
        new MetricDefinition(MetricId.NetworkUpload, "NET UP", MetricUnit.BytesPerSecond, 0)
    }.ToDictionary(d => d.Id);

    public static MetricDefinition Get(MetricId id) =>
        Map.TryGetValue(id, out var d) ? d : new MetricDefinition(id, id.ToString(), MetricUnit.None, 1);

    public static IEnumerable<MetricDefinition> All => Map.Values;
}
