using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Sensors;
using Reactor.Core.Telemetry;
using Xunit;

namespace Reactor.Core.Tests;

/// <summary>
/// Regression cover for the "firmware limit reported as a live reading" bug.
///
/// The dashboard showed DRIVE TEMP 88 &#176;C and escalated the whole board to
/// WARNING while the drive was actually at 52 &#176;C. The rule matched
/// "Temperature" loosely, which also caught "Warning Temperature" (83) and
/// "Critical Temperature" (88), and then aggregated the lot with Max.
/// </summary>
public class StorageMappingTests
{
    private static SensorDescriptor Sensor(
        string name, SensorKind kind, string drive = "KINGSTON SKC3000D2048G",
        int index = 0, HardwareKind hardware = HardwareKind.Storage) =>
        new($"/nvme/{index}/{kind}/{name}".ToLowerInvariant(), name, kind, hardware,
            drive, $"/nvme/{index}".ToLowerInvariant(), index);

    /// <summary>The exact sensor set the KC3000 publishes, as seen in diagnostics.</summary>
    private static List<SensorDescriptor> Kc3000(string drive = "KINGSTON SKC3000D2048G", int index = 0) => new()
    {
        Sensor("Composite Temperature", SensorKind.Temperature, drive, index),
        Sensor("Temperature #2", SensorKind.Temperature, drive, index),
        Sensor("Warning Temperature", SensorKind.Temperature, drive, index),
        Sensor("Critical Temperature", SensorKind.Temperature, drive, index),
        Sensor("Used Space", SensorKind.Load, drive, index)
    };

    private static readonly double?[] Kc3000Readings = { 52, 65, 83, 88, 41 };

    [Fact]
    public void Drive_temperature_is_the_live_composite_not_a_firmware_limit()
    {
        var binding = SensorMapper.Build(Kc3000()).Binding(MetricId.StorageTemperature)!;

        Assert.Equal("Composite Temperature", binding.Sensors.Single().Name);
        Assert.Equal(52, SensorMapper.Combine(binding, new double?[] { 52 }));
    }

    [Fact]
    public void Drive_temperature_is_never_aggregated_across_sensors()
    {
        var binding = SensorMapper.Build(Kc3000()).Binding(MetricId.StorageTemperature)!;

        // The original bug was Aggregation.Max over four sensors.
        Assert.Equal(Aggregation.Best, binding.Aggregation);
        Assert.Single(binding.Sensors);
    }

    [Fact]
    public void Threshold_sensors_never_bind_to_a_telemetry_metric()
    {
        var map = SensorMapper.Build(Kc3000());
        var chosen = map.Binding(MetricId.StorageTemperature)!.AllCandidates.Select(c => c.Name).ToList();

        Assert.DoesNotContain("Warning Temperature", chosen);
        Assert.DoesNotContain("Critical Temperature", chosen);
    }

    [Fact]
    public void Secondary_live_probes_stay_out_of_the_headline_value()
    {
        var binding = SensorMapper.Build(Kc3000()).Binding(MetricId.StorageTemperature)!;

        // Temperature #2 is a real reading, just not the canonical one.
        Assert.DoesNotContain(binding.Sensors, s => s.Name == "Temperature #2");
        Assert.Contains(binding.AllCandidates, s => s.Name == "Temperature #2");
    }

    [Fact]
    public void Firmware_limits_are_captured_as_limits_rather_than_readings()
    {
        var map = SensorMapper.Build(Kc3000());

        Assert.Equal("Warning Temperature",
            map.Limit(MetricId.StorageWarningTemperature)!.Sensors.Single().Name);
        Assert.Equal("Critical Temperature",
            map.Limit(MetricId.StorageCriticalTemperature)!.Sensors.Single().Name);

        // ...and they are not in the telemetry map at all.
        Assert.False(map.Has(MetricId.StorageWarningTemperature));
        Assert.False(map.Has(MetricId.StorageCriticalTemperature));
    }

    [Fact]
    public void Missing_firmware_limits_are_not_reported_as_unmapped_metrics()
    {
        // A SATA drive with a single plain temperature sensor.
        var map = SensorMapper.Build(new List<SensorDescriptor>
        {
            Sensor("Temperature", SensorKind.Temperature, "Samsung SSD 860")
        });

        Assert.Equal("Temperature", map.Binding(MetricId.StorageTemperature)!.Sensors.Single().Name);
        Assert.DoesNotContain(MetricId.StorageWarningTemperature, map.Unresolved);
        Assert.DoesNotContain(MetricId.StorageCriticalTemperature, map.Unresolved);
    }

    [Fact]
    public void Temperature_is_taken_from_one_drive_only()
    {
        var sensors = Kc3000("KINGSTON SKC3000D2048G", 0);
        sensors.AddRange(Kc3000("WD_BLACK SN850X 4TB", 1));

        var primary = StorageSelector.SelectPrimary(sensors);
        var map = SensorMapper.Build(StorageSelector.RestrictToPrimary(sensors, primary));
        var binding = map.Binding(MetricId.StorageTemperature)!;

        Assert.Single(binding.Sensors);
        Assert.Equal("KINGSTON SKC3000D2048G", binding.Sensors[0].HardwareName);
        // No sensor from the second drive may even be a candidate.
        Assert.All(binding.AllCandidates, c => Assert.Equal("KINGSTON SKC3000D2048G", c.HardwareName));
    }

    [Fact]
    public void Non_storage_sensors_survive_the_drive_restriction()
    {
        var sensors = Kc3000();
        sensors.Add(Sensor("CPU Total", SensorKind.Load, "AMD Ryzen 7 7800X3D", 5, HardwareKind.Cpu));

        var restricted = StorageSelector.RestrictToPrimary(sensors, StorageSelector.SelectPrimary(sensors));

        Assert.Contains(restricted, s => s.HardwareKind == HardwareKind.Cpu);
    }

    [Fact]
    public void A_drive_at_its_normal_temperature_does_not_raise_a_warning()
    {
        var map = SensorMapper.Build(Kc3000());
        var snapshot = Read(map, Kc3000Readings);

        var status = new StatusEvaluator(new StatusThresholds())
            .Evaluate(snapshot, DerivedMetrics.Compute(snapshot, new AppConfig()));

        Assert.Equal(52, snapshot.Get(MetricId.StorageTemperature));
        Assert.NotEqual(SystemState.Warning, status.State);
        Assert.NotEqual(SystemState.ThermalLoad, status.State);
    }

    [Fact]
    public void The_drives_own_limits_drive_the_status_thresholds()
    {
        var map = SensorMapper.Build(Kc3000());

        // 85 C is below the generic 72 C warning trip but under the drive's own
        // 88 C critical point, so it must read as thermal load, not warning.
        var snapshot = Read(map, new double?[] { 85, 86, 83, 88, 41 });

        var status = new StatusEvaluator(new StatusThresholds())
            .Evaluate(snapshot, DerivedMetrics.Compute(snapshot, new AppConfig()));

        Assert.Equal(83, snapshot.GetLimit(MetricId.StorageWarningTemperature));
        Assert.Equal(88, snapshot.GetLimit(MetricId.StorageCriticalTemperature));
        Assert.Equal(SystemState.ThermalLoad, status.State);

        // Above the firmware critical point it does escalate.
        var hot = Read(map, new double?[] { 91, 92, 83, 88, 41 });
        Assert.Equal(SystemState.Warning,
            new StatusEvaluator(new StatusThresholds())
                .Evaluate(hot, DerivedMetrics.Compute(hot, new AppConfig())).State);
    }

    /// <summary>
    /// Mirrors what the hardware layer does: read each bound sensor, combine it,
    /// and keep limits in their own bucket.
    /// </summary>
    private static MetricsSnapshot Read(SensorMap map, double?[] readings)
    {
        var order = Kc3000().Select(s => s.Identifier).ToList();
        double? ValueOf(SensorDescriptor s) => readings[order.IndexOf(s.Identifier)];

        var values = new Dictionary<MetricId, double>();
        foreach (var (metric, binding) in map.Bindings)
            if (SensorMapper.Combine(binding, binding.Sensors.Select(ValueOf).ToList()) is { } v &&
                MetricPlausibility.IsPlausible(metric, v))
                values[metric] = v;

        var limits = new Dictionary<MetricId, double>();
        foreach (var (metric, binding) in map.Limits)
            if (SensorMapper.Combine(binding, binding.Sensors.Select(ValueOf).ToList()) is { } v)
                limits[metric] = v;

        return new MetricsSnapshot(DateTime.UtcNow, values, Array.Empty<double>(), limits);
    }
}

/// <summary>Generic cover for the threshold-vs-reading distinction.</summary>
public class SensorSemanticsTests
{
    [Theory]
    [InlineData("Warning Temperature")]
    [InlineData("Critical Temperature")]
    [InlineData("Shutdown Temperature")]
    [InlineData("Maximum Temperature")]
    [InlineData("Temperature Limit")]
    [InlineData("Power Limit")]
    [InlineData("Throttle Temperature")]
    public void Firmware_limits_are_recognised(string name)
    {
        Assert.True(SensorSemantics.LooksLikeThreshold(name));
        Assert.Equal(SensorRole.Threshold, SensorSemantics.RoleOf(name));
    }

    [Theory]
    [InlineData("Composite Temperature")]
    [InlineData("Temperature")]
    [InlineData("Temperature #2")]
    [InlineData("Core (Tctl/Tdie)")]
    [InlineData("GPU Hot Spot")]
    [InlineData("CPU Core Max")]     // a live maximum, despite the word "Max"
    [InlineData("Core Max")]
    [InlineData("GPU Package")]
    public void Live_readings_are_not_mistaken_for_limits(string name)
    {
        Assert.False(SensorSemantics.LooksLikeThreshold(name));
        Assert.Equal(SensorRole.Telemetry, SensorSemantics.RoleOf(name));
    }

    [Fact]
    public void The_busiest_core_metric_still_binds_despite_its_name()
    {
        // "CPU Core Max" must not be swept up by the threshold filter.
        var map = SensorMapper.Build(new[]
        {
            new SensorDescriptor("/amdcpu/0/load/1", "CPU Core Max", SensorKind.Load,
                HardwareKind.Cpu, "AMD Ryzen 7 7800X3D", "/amdcpu/0")
        });

        Assert.Equal("CPU Core Max", map.Binding(MetricId.CpuMaxCoreLoad)!.Sensors.Single().Name);
    }
}
