using Reactor.Core.Metrics;
using Reactor.Core.Sensors;
using Xunit;

namespace Reactor.Core.Tests;

/// <summary>
/// Sensor selection is the part most likely to break silently on a machine we
/// cannot test on, so the fixtures below are transcriptions of real sensor
/// listings rather than invented names.
/// </summary>
public class SensorMapperTests
{
    private static SensorDescriptor Sensor(
        string name, SensorKind kind, HardwareKind hardware = HardwareKind.Cpu,
        string hardwareName = "CPU", int hardwareIndex = 0, string? identifier = null) =>
        new(identifier ?? $"/{hardwareName}/{kind}/{name}".ToLowerInvariant(),
            name, kind, hardware, hardwareName, $"/{hardwareName}".ToLowerInvariant(), hardwareIndex);

    /// <summary>Sensors an AMD Zen 4 desktop part exposes via the CPU driver.</summary>
    private static List<SensorDescriptor> Zen4Cpu()
    {
        var list = new List<SensorDescriptor>
        {
            Sensor("CPU Total", SensorKind.Load),
            Sensor("CPU Core Max", SensorKind.Load),
            Sensor("Core (Tctl/Tdie)", SensorKind.Temperature),
            Sensor("Package", SensorKind.Power),
            Sensor("Cores (Average)", SensorKind.Clock),
            Sensor("Cores (Average Effective)", SensorKind.Clock)
        };

        for (var i = 1; i <= 8; i++)
        {
            list.Add(Sensor($"Core #{i}", SensorKind.Clock));
            list.Add(Sensor($"Core #{i} (Effective)", SensorKind.Clock));
            list.Add(Sensor($"Core #{i} (SMU)", SensorKind.Power));
        }

        for (var i = 1; i <= 16; i++)
            list.Add(Sensor($"CPU Core #{i}", SensorKind.Load));

        return list;
    }

    [Fact]
    public void Maps_the_headline_cpu_metrics()
    {
        var map = SensorMapper.Build(Zen4Cpu());

        Assert.Equal("CPU Total", map.Binding(MetricId.CpuLoad)!.Sensors.Single().Name);
        Assert.Equal("Core (Tctl/Tdie)", map.Binding(MetricId.CpuTemperature)!.Sensors.Single().Name);
        Assert.Equal("Package", map.Binding(MetricId.CpuPackagePower)!.Sensors.Single().Name);
    }

    [Fact]
    public void Core_clock_aggregates_only_the_nominal_per_core_sensors()
    {
        var binding = SensorMapper.Build(Zen4Cpu()).Binding(MetricId.CpuCoreClock)!;

        // Eight cores - not sixteen, which is what an unanchored pattern would
        // give by also matching the "(Effective)" duplicates.
        Assert.Equal(8, binding.Sensors.Count);
        Assert.All(binding.Sensors, s => Assert.DoesNotContain("Effective", s.Name));
        Assert.Equal(Aggregation.Max, binding.Aggregation);
    }

    [Fact]
    public void Falls_back_to_the_vendor_average_clock_when_per_core_is_absent()
    {
        var sensors = Zen4Cpu().Where(s => !s.Name.StartsWith("Core #")).ToList();

        var binding = SensorMapper.Build(sensors).Binding(MetricId.CpuCoreClock)!;

        Assert.Equal("Cores (Average)", binding.Sensors.Single().Name);
    }

    [Fact]
    public void Per_core_load_sensors_are_ordered_numerically()
    {
        var map = SensorMapper.Build(Zen4Cpu());

        Assert.Equal(16, map.CoreLoadSensors.Count);
        Assert.Equal("CPU Core #1", map.CoreLoadSensors[0].Name);
        Assert.Equal("CPU Core #2", map.CoreLoadSensors[1].Name);
        // The trap: lexical ordering would put #10 here.
        Assert.Equal("CPU Core #9", map.CoreLoadSensors[8].Name);
        Assert.Equal("CPU Core #16", map.CoreLoadSensors[15].Name);
    }

    [Fact]
    public void Hottest_core_is_unresolved_when_no_per_core_temperature_exists()
    {
        var map = SensorMapper.Build(Zen4Cpu());

        Assert.False(map.Has(MetricId.CpuHottestCoreTemperature));
        Assert.Contains(MetricId.CpuHottestCoreTemperature, map.Unresolved);
    }

    [Fact]
    public void Hottest_core_takes_the_maximum_across_cores_when_available()
    {
        var sensors = new List<SensorDescriptor>
        {
            Sensor("CPU Core #1", SensorKind.Temperature),
            Sensor("CPU Core #2", SensorKind.Temperature),
            Sensor("CPU Core #3", SensorKind.Temperature)
        };

        var binding = SensorMapper.Build(sensors).Binding(MetricId.CpuHottestCoreTemperature)!;
        Assert.Equal(3, binding.Sensors.Count);

        var combined = SensorMapper.Combine(binding, new double?[] { 61, 74.5, 68 });
        Assert.Equal(74.5, combined);
    }

    [Fact]
    public void Physical_memory_is_never_bound_to_the_page_file()
    {
        // Windows reports commit charge as a second memory device whose sensor
        // names are identical to the physical ones.
        var sensors = new List<SensorDescriptor>
        {
            Sensor("Memory", SensorKind.Load, HardwareKind.Memory, "Virtual Memory", 0),
            Sensor("Memory Used", SensorKind.Data, HardwareKind.Memory, "Virtual Memory", 0),
            Sensor("Memory Available", SensorKind.Data, HardwareKind.Memory, "Virtual Memory", 0),
            Sensor("Memory", SensorKind.Load, HardwareKind.Memory, "Total Memory", 1),
            Sensor("Memory Used", SensorKind.Data, HardwareKind.Memory, "Total Memory", 1),
            Sensor("Memory Available", SensorKind.Data, HardwareKind.Memory, "Total Memory", 1)
        };

        var map = SensorMapper.Build(sensors);

        Assert.Equal("Total Memory", map.Binding(MetricId.MemoryLoad)!.Sensors.Single().HardwareName);
        Assert.Equal("Total Memory", map.Binding(MetricId.MemoryUsed)!.Sensors.Single().HardwareName);
    }

    [Fact]
    public void Network_takes_the_busiest_adapter_rather_than_the_sum()
    {
        var sensors = new List<SensorDescriptor>
        {
            Sensor("Download Speed", SensorKind.Throughput, HardwareKind.Network, "WLAN", 0),
            Sensor("Download Speed", SensorKind.Throughput, HardwareKind.Network, "vEthernet (WSL)", 1),
            Sensor("Download Speed", SensorKind.Throughput, HardwareKind.Network, "Ethernet 3", 2)
        };

        var binding = SensorMapper.Build(sensors).Binding(MetricId.NetworkDownload)!;

        Assert.Equal(Aggregation.Max, binding.Aggregation);
        Assert.Equal(25_000_000, SensorMapper.Combine(binding, new double?[] { 25_000_000, 0, 0 }));
    }

    [Fact]
    public void Missing_readings_do_not_poison_an_aggregate()
    {
        var binding = new MetricBinding(
            MetricId.CpuCoreClock,
            new[] { Sensor("Core #1", SensorKind.Clock), Sensor("Core #2", SensorKind.Clock) },
            Aggregation.Max, 1.0);

        Assert.Equal(4200, SensorMapper.Combine(binding, new double?[] { null, 4200 }));
        Assert.Null(SensorMapper.Combine(binding, new double?[] { null, null }));
    }

    [Fact]
    public void Unknown_hardware_leaves_every_metric_unresolved_without_throwing()
    {
        var map = SensorMapper.Build(Array.Empty<SensorDescriptor>());

        Assert.Empty(map.Bindings);
        Assert.NotEmpty(map.Unresolved);
        Assert.Empty(map.CoreLoadSensors);
    }
}
