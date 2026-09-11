using Reactor.Core.Metrics;

namespace Reactor.Core.Sensors;

/// <summary>
/// Rejects readings that are structurally impossible for a running machine.
///
/// This matters more than it sounds: without administrator rights the CPU
/// driver cannot be loaded, and the library still publishes the package
/// temperature, power and clock sensors - they simply read a flat 0. Showing
/// "0.0 &#176;C" is worse than showing nothing, so a running CPU reporting
/// absolute zero is treated as an absent sensor rather than a measurement.
/// </summary>
public static class MetricPlausibility
{
    public static bool IsPlausible(MetricId metric, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;

        return metric switch
        {
            // A powered component is never at 0 K, and never above ~150 C.
            MetricId.CpuTemperature or
            MetricId.CpuHottestCoreTemperature or
            MetricId.GpuTemperature or
            MetricId.GpuHotspotTemperature or
            MetricId.GpuMemoryTemperature or
            MetricId.StorageTemperature => value > 0 && value < 150,

            // Package power of exactly zero means "not readable", not "idle".
            MetricId.CpuPackagePower or
            MetricId.GpuPower => value > 0 && value < 2000,

            // Likewise a core clock: an executing core has a frequency.
            MetricId.CpuCoreClock or
            MetricId.GpuCoreClock => value > 0 && value < 20000,

            MetricId.CpuLoad or
            MetricId.CpuMaxCoreLoad or
            MetricId.GpuLoad or
            MetricId.MemoryLoad or
            MetricId.StorageLoad or
            MetricId.GpuFanPercent or
            MetricId.GpuPowerLimitPercent => value >= 0 && value <= 100.001,

            // Zero RPM is legitimate - idle cards stop their fans entirely.
            MetricId.GpuFanRpm => value >= 0 && value < 20000,

            MetricId.GpuMemoryUsed or
            MetricId.GpuMemoryTotal or
            MetricId.GpuMemoryFree or
            MetricId.MemoryUsed or
            MetricId.MemoryAvailable => value >= 0,

            MetricId.NetworkDownload or
            MetricId.NetworkUpload => value >= 0,

            _ => true
        };
    }
}
