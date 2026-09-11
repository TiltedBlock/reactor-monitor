namespace Reactor.Core.Metrics;

/// <summary>
/// The vocabulary the rest of the application speaks. Nothing above the sensor
/// mapping layer ever refers to a LibreHardwareMonitor sensor name.
/// </summary>
public enum MetricId
{
    CpuLoad,
    CpuMaxCoreLoad,
    CpuTemperature,
    CpuHottestCoreTemperature,
    CpuPackagePower,
    CpuCoreClock,

    GpuLoad,
    GpuTemperature,
    GpuHotspotTemperature,
    GpuMemoryTemperature,
    GpuPower,
    GpuPowerLimitPercent,
    GpuCoreClock,
    GpuMemoryUsed,
    GpuMemoryTotal,
    GpuMemoryFree,
    GpuFanRpm,
    GpuFanPercent,

    MemoryLoad,
    MemoryUsed,
    MemoryAvailable,

    StorageTemperature,
    StorageLoad,

    // Device-reported limits, not readings. Kept in MetricsSnapshot.Limits and
    // never displayed as telemetry.
    StorageWarningTemperature,
    StorageCriticalTemperature,

    NetworkDownload,
    NetworkUpload
}

public enum MetricUnit
{
    None,
    Percent,
    Celsius,
    Watt,
    Megahertz,
    Megabyte,
    Gigabyte,
    Rpm,
    BytesPerSecond
}
