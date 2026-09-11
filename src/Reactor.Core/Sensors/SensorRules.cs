using Reactor.Core.Metrics;

namespace Reactor.Core.Sensors;

/// <summary>
/// The complete sensor-naming knowledge of the application, in one table.
/// Vendors name things differently (and rename them between driver versions),
/// so each metric lists several candidates in order of preference.
///
/// Patterns are anchored deliberately: an unanchored "Core #\d+" also matches
/// "Core #1 (Effective)", which silently mixes two different measurements into
/// one aggregate.
/// </summary>
public static class SensorRules
{
    private static readonly HardwareKind[] Cpu = { HardwareKind.Cpu };
    private static readonly HardwareKind[] Gpu =
        { HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel };
    private static readonly HardwareKind[] Memory = { HardwareKind.Memory };
    private static readonly HardwareKind[] Storage = { HardwareKind.Storage };
    private static readonly HardwareKind[] Network = { HardwareKind.Network };

    /// <summary>
    /// Windows reports the page file as a second "memory" device whose numbers
    /// look like RAM but are commit charge. Never bind physical memory to it.
    /// </summary>
    private static readonly string[] NotVirtualMemory = { @"Virtual" };

    public static IReadOnlyList<SensorRule> All { get; } = new List<SensorRule>
    {
        // ---- CPU -----------------------------------------------------------
        new(MetricId.CpuLoad, SensorKind.Load, Cpu,
            new[] { @"^CPU Total$", @"^Total$", @"^CPU Utility$" }),

        // The single busiest logical core - a lightly threaded workload can pin
        // one core while total load looks idle.
        new(MetricId.CpuMaxCoreLoad, SensorKind.Load, Cpu,
            new[] { @"^CPU Core Max$", @"^Core Max$" }),

        new(MetricId.CpuTemperature, SensorKind.Temperature, Cpu,
            new[]
            {
                @"^Core \(Tctl/Tdie\)$",   // AMD Zen
                @"^CPU Package$",          // Intel
                @"^Core \(Tdie\)$",
                @"^Package$",
                @"^CPU Cores$",
                @"^Core Average$",
                @"^CCD1 \(Tdie\)$",
                @"^Core \(Tctl\)$"
            }),

        // Hottest individual core. Only genuine per-core probes qualify.
        //
        // Deliberately NOT falling back to "CCD1 (Tdie)": a compute-die sensor
        // is not a core sensor, and on Zen 4 it reads several degrees BELOW the
        // package Tctl/Tdie - so presenting it as the hottest core produces a
        // hottest core cooler than the package, which is nonsense. Zen 4
        // desktop parts expose no per-core temperature at all, and the readout
        // correctly hides itself.
        new(MetricId.CpuHottestCoreTemperature, SensorKind.Temperature, Cpu,
            new[] { @"^CPU Core #\d+$", @"^Core #\d+$", @"^Core Max$" },
            Aggregation.Max),

        new(MetricId.CpuPackagePower, SensorKind.Power, Cpu,
            new[]
            {
                @"^Package$",              // AMD + Intel package power
                @"^CPU Package$",
                @"^CPU PPT$",
                @"^Core \(SVI2 TFN\)$",
                @"^CPU Cores$"
            }),

        // "Effective" clock: prefer the fastest core actually running, fall back
        // to the vendor's own averages.
        new(MetricId.CpuCoreClock, SensorKind.Clock, Cpu,
            new[]
            {
                @"^Core #\d+$",
                @"^CPU Core #\d+$",
                @"^Cores \(Average\)$",
                @"^Core #\d+ \(Effective\)$",
                @"^Cores \(Average Effective\)$",
                @"^Bus Speed$"
            },
            Aggregation.Max),

        // ---- GPU -----------------------------------------------------------
        new(MetricId.GpuLoad, SensorKind.Load, Gpu,
            new[] { @"^GPU Core$", @"^GPU$", @"^D3D 3D$" }),

        new(MetricId.GpuTemperature, SensorKind.Temperature, Gpu,
            new[] { @"^GPU Core$", @"^GPU$", @"^GPU Temperature$" }),

        new(MetricId.GpuHotspotTemperature, SensorKind.Temperature, Gpu,
            new[] { @"^GPU Hot ?Spot$", @"Hot ?Spot", @"^GPU Junction$" }),

        new(MetricId.GpuMemoryTemperature, SensorKind.Temperature, Gpu,
            new[] { @"^GPU Memory Junction$", @"^GPU (V)?RAM$", @"Memory Junction" }),

        new(MetricId.GpuPower, SensorKind.Power, Gpu,
            new[] { @"^GPU Package$", @"^GPU Power$", @"^GPU Total$", @"^GPU PPT$", @"^Board Power" }),

        // NVIDIA exposes "how close the board is to its power limit" as a Load
        // sensor. When present this beats any guessed wattage ceiling.
        new(MetricId.GpuPowerLimitPercent, SensorKind.Load, Gpu,
            new[] { @"^GPU Power$", @"^GPU Board Power$" }),

        new(MetricId.GpuCoreClock, SensorKind.Clock, Gpu,
            new[] { @"^GPU Core$", @"^GPU$" }),

        // NVIDIA reports VRAM as SmallData in MB; some drivers only expose the
        // D3D counters, which are close enough for a dashboard.
        new(MetricId.GpuMemoryUsed, SensorKind.SmallData, Gpu,
            new[] { @"^GPU Memory Used$", @"^D3D Dedicated Memory Used$" }),

        new(MetricId.GpuMemoryTotal, SensorKind.SmallData, Gpu,
            new[] { @"^GPU Memory Total$", @"^D3D Dedicated Memory Total$" }),

        new(MetricId.GpuMemoryFree, SensorKind.SmallData, Gpu,
            new[] { @"^GPU Memory Free$", @"^D3D Dedicated Memory Free$" }),

        // Cards have several fans; the loudest one is the interesting number.
        new(MetricId.GpuFanRpm, SensorKind.Fan, Gpu,
            new[] { @"^GPU Fan ?\d*$", @"^Fan ?\d*$" }, Aggregation.Max),

        new(MetricId.GpuFanPercent, SensorKind.Control, Gpu,
            new[] { @"^GPU Fan ?\d*$", @"^Fan ?\d*$" }, Aggregation.Max),

        // ---- Memory --------------------------------------------------------
        new(MetricId.MemoryLoad, SensorKind.Load, Memory,
            new[] { @"^Memory$" }, excludeHardwarePatterns: NotVirtualMemory),

        new(MetricId.MemoryUsed, SensorKind.Data, Memory,
            new[] { @"^Memory Used$" }, excludeHardwarePatterns: NotVirtualMemory),

        new(MetricId.MemoryAvailable, SensorKind.Data, Memory,
            new[] { @"^Memory Available$" }, excludeHardwarePatterns: NotVirtualMemory),

        // ---- Storage -------------------------------------------------------
        // One drive, one live temperature. Never aggregated: the sensor list of
        // an NVMe drive contains both secondary probes and firmware limits, and
        // taking a maximum over "anything with Temperature in the name" reports
        // the drive's own critical limit as its current temperature.
        //
        // Threshold sensors are already excluded by role (see SensorSemantics);
        // the ordering here decides which *live* probe is the canonical one.
        new(MetricId.StorageTemperature, SensorKind.Temperature, Storage,
            new[]
            {
                @"^Composite Temperature$",   // NVMe composite - the right answer
                @"^Temperature$",             // SATA / generic
                @"^Drive Temperature$",
                @"^Assembly Temperature$",
                @"^Temperature #?1$",         // first probe when there is no composite
                @"^Temperature #?\d+$"       // last resort: any numbered probe
            }),

        new(MetricId.StorageLoad, SensorKind.Load, Storage,
            new[] { @"^Used Space$" }),

        // Device-reported limits. These are metadata, not telemetry: they are
        // kept apart from the readings and only feed the status thresholds.
        new(MetricId.StorageWarningTemperature, SensorKind.Temperature, Storage,
            new[] { @"^Warning Temperature$", @"^Warning.*Temperature" },
            role: SensorRole.Threshold),

        new(MetricId.StorageCriticalTemperature, SensorKind.Temperature, Storage,
            new[] { @"^Critical Temperature$", @"^Critical.*Temperature", @"^Shutdown Temperature$" },
            role: SensorRole.Threshold),

        // ---- Network -------------------------------------------------------
        // Busiest adapter rather than a sum: virtual and disconnected adapters
        // would otherwise dilute or double-count the real link.
        new(MetricId.NetworkDownload, SensorKind.Throughput, Network,
            new[] { @"^Download Speed$" }, Aggregation.Max),

        new(MetricId.NetworkUpload, SensorKind.Throughput, Network,
            new[] { @"^Upload Speed$" }, Aggregation.Max)
    };
}
