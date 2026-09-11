using Reactor.Core.Metrics;
using Reactor.Core.Sensors;
using Xunit;

namespace Reactor.Core.Tests;

/// <summary>
/// The CPU sensor set a Zen 4 desktop part publishes once the PawnIO kernel
/// driver is available. Transcribed from an elevated probe run on a
/// Ryzen 7 7800X3D - the sensor list is noticeably larger than the one visible
/// without the driver, which is how the CCD mis-binding below slipped in.
/// </summary>
public class Zen4CpuMappingTests
{
    private static SensorDescriptor Sensor(string name, SensorKind kind, int index) =>
        new($"/amdcpu/0/{kind}/{index}".ToLowerInvariant(), name, kind,
            HardwareKind.Cpu, "AMD Ryzen 7 7800X3D", "/amdcpu/0");

    private static List<SensorDescriptor> Zen4Elevated()
    {
        var list = new List<SensorDescriptor>
        {
            Sensor("CPU Total", SensorKind.Load, 0),
            Sensor("CPU Core Max", SensorKind.Load, 1),

            // Three temperature sensors, none of which is a per-core probe.
            Sensor("Core (Tctl/Tdie)", SensorKind.Temperature, 2),
            Sensor("Package", SensorKind.Temperature, 5),
            Sensor("CCD1 (Tdie)", SensorKind.Temperature, 7),

            Sensor("Package", SensorKind.Power, 0),
            Sensor("Cores (Average)", SensorKind.Clock, 1),
            Sensor("Cores (Average Effective)", SensorKind.Clock, 2)
        };

        for (var i = 1; i <= 8; i++)
        {
            list.Add(Sensor($"Core #{i}", SensorKind.Clock, 4 + i * 2));
            list.Add(Sensor($"Core #{i} (Effective)", SensorKind.Clock, 5 + i * 2));
        }

        for (var i = 1; i <= 16; i++)
            list.Add(Sensor($"CPU Core #{i}", SensorKind.Load, 10 + i));

        return list;
    }

    [Fact]
    public void Package_temperature_prefers_tctl_over_the_other_candidates()
    {
        var binding = SensorMapper.Build(Zen4Elevated()).Binding(MetricId.CpuTemperature)!;

        Assert.Equal("Core (Tctl/Tdie)", binding.Sensors.Single().Name);
        // "Package" and "CCD1 (Tdie)" are real candidates that lost on priority.
        Assert.Equal(3, binding.AllCandidates.Count);
    }

    [Fact]
    public void A_compute_die_sensor_is_never_reported_as_the_hottest_core()
    {
        // CCD1 (Tdie) reads ~65 C while package Tctl/Tdie reads ~77 C. Binding
        // it here produced a "hottest core" cooler than the package.
        var map = SensorMapper.Build(Zen4Elevated());

        Assert.False(map.Has(MetricId.CpuHottestCoreTemperature));
        Assert.Contains(MetricId.CpuHottestCoreTemperature, map.Unresolved);
    }

    [Fact]
    public void Hottest_core_still_binds_where_real_per_core_probes_exist()
    {
        // An Intel part does expose them, and must keep working.
        var intel = new List<SensorDescriptor>
        {
            Sensor("CPU Package", SensorKind.Temperature, 0),
            Sensor("CPU Core #1", SensorKind.Temperature, 1),
            Sensor("CPU Core #2", SensorKind.Temperature, 2)
        };

        var binding = SensorMapper.Build(intel).Binding(MetricId.CpuHottestCoreTemperature)!;

        Assert.Equal(2, binding.Sensors.Count);
        Assert.Equal(84, SensorMapper.Combine(binding, new double?[] { 71, 84 }));
    }

    [Fact]
    public void Core_clock_uses_the_eight_nominal_sensors_only()
    {
        var binding = SensorMapper.Build(Zen4Elevated()).Binding(MetricId.CpuCoreClock)!;

        Assert.Equal(8, binding.Sensors.Count);
        Assert.All(binding.Sensors, s => Assert.DoesNotContain("Effective", s.Name));
        Assert.All(binding.Sensors, s => Assert.DoesNotContain("Average", s.Name));
        Assert.Equal(4850, SensorMapper.Combine(binding,
            new double?[] { 4200, 4850, 3600, 3600, 4100, 3600, 3600, 3600 }));
    }

    [Fact]
    public void Package_power_binds_to_the_power_sensor_not_the_temperature_one()
    {
        // Two different sensors are both called "Package"; only the kind separates them.
        var binding = SensorMapper.Build(Zen4Elevated()).Binding(MetricId.CpuPackagePower)!;

        Assert.Equal(SensorKind.Power, binding.Sensors.Single().Kind);
        Assert.Equal("/amdcpu/0/power/0", binding.Sensors.Single().Identifier);
    }

    [Fact]
    public void All_sixteen_threads_are_discovered_in_numeric_order()
    {
        var map = SensorMapper.Build(Zen4Elevated());

        Assert.Equal(16, map.CoreLoadSensors.Count);
        Assert.Equal("CPU Core #9", map.CoreLoadSensors[8].Name);
    }
}
