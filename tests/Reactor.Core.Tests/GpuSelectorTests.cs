using Reactor.Core.Metrics;
using Reactor.Core.Sensors;
using Xunit;

namespace Reactor.Core.Tests;

public class GpuSelectorTests
{
    private static SensorDescriptor Sensor(
        string name, SensorKind kind, HardwareKind hardware, string hardwareName, int index) =>
        new($"/{hardwareName}/{kind}/{name}".ToLowerInvariant(), name, kind, hardware,
            hardwareName, $"/{hardwareName}".ToLowerInvariant(), index);

    /// <summary>
    /// A Ryzen desktop chip carries an integrated Radeon that enumerates before
    /// the discrete card and exposes its own load, power and "VRAM" sensors.
    /// </summary>
    private static List<SensorDescriptor> IntegratedAndDiscrete() => new()
    {
        Sensor("GPU Core", SensorKind.Load, HardwareKind.GpuAmd, "AMD Radeon(TM) Graphics", 0),
        Sensor("GPU Core", SensorKind.Power, HardwareKind.GpuAmd, "AMD Radeon(TM) Graphics", 0),
        Sensor("GPU VR SoC", SensorKind.Temperature, HardwareKind.GpuAmd, "AMD Radeon(TM) Graphics", 0),
        Sensor("GPU Memory Total", SensorKind.SmallData, HardwareKind.GpuAmd, "AMD Radeon(TM) Graphics", 0),
        Sensor("GPU Memory Used", SensorKind.SmallData, HardwareKind.GpuAmd, "AMD Radeon(TM) Graphics", 0),

        Sensor("GPU Core", SensorKind.Load, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1),
        Sensor("GPU Core", SensorKind.Temperature, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1),
        Sensor("GPU Hot Spot", SensorKind.Temperature, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1),
        Sensor("GPU Package", SensorKind.Power, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1),
        Sensor("GPU Fan 1", SensorKind.Fan, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1),
        Sensor("GPU Memory Total", SensorKind.SmallData, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1),
        Sensor("GPU Memory Used", SensorKind.SmallData, HardwareKind.GpuNvidia, "NVIDIA GeForce RTX 4080 SUPER", 1)
    };

    [Fact]
    public void Prefers_the_discrete_card_over_the_integrated_one()
    {
        var primary = GpuSelector.SelectPrimary(IntegratedAndDiscrete());

        Assert.Equal("/nvidia geforce rtx 4080 super", primary);
    }

    [Fact]
    public void Enumeration_order_does_not_decide_the_primary_gpu()
    {
        // Same devices, discrete listed first: the answer must not change.
        var reordered = IntegratedAndDiscrete().OrderByDescending(s => s.HardwareIndex).ToList();

        Assert.Equal("/nvidia geforce rtx 4080 super", GpuSelector.SelectPrimary(reordered));
    }

    [Fact]
    public void Secondary_gpu_sensors_are_excluded_from_mapping()
    {
        var sensors = IntegratedAndDiscrete();
        var primary = GpuSelector.SelectPrimary(sensors);

        var map = SensorMapper.Build(GpuSelector.RestrictToPrimaryGpu(sensors, primary));

        // The integrated GPU would otherwise supply a 512 MB VRAM total.
        Assert.Equal("NVIDIA GeForce RTX 4080 SUPER",
            map.Binding(MetricId.GpuMemoryTotal)!.Sensors.Single().HardwareName);
        Assert.Equal("NVIDIA GeForce RTX 4080 SUPER",
            map.Binding(MetricId.GpuPower)!.Sensors.Single().HardwareName);
    }

    [Fact]
    public void Non_gpu_sensors_survive_the_restriction()
    {
        var sensors = IntegratedAndDiscrete();
        sensors.Add(Sensor("CPU Total", SensorKind.Load, HardwareKind.Cpu, "Some CPU", 2));

        var restricted = GpuSelector.RestrictToPrimaryGpu(sensors, GpuSelector.SelectPrimary(sensors));

        Assert.Contains(restricted, s => s.HardwareKind == HardwareKind.Cpu);
    }

    [Fact]
    public void A_single_gpu_is_always_the_primary()
    {
        var only = IntegratedAndDiscrete().Where(s => s.HardwareKind == HardwareKind.GpuAmd).ToList();

        Assert.Equal("/amd radeon(tm) graphics", GpuSelector.SelectPrimary(only));
    }

    [Fact]
    public void No_gpu_selects_nothing_and_changes_nothing()
    {
        Assert.Null(GpuSelector.SelectPrimary(Array.Empty<SensorDescriptor>()));

        var sensors = new[] { Sensor("CPU Total", SensorKind.Load, HardwareKind.Cpu, "CPU", 0) };
        Assert.Same(sensors, GpuSelector.RestrictToPrimaryGpu(sensors, null));
    }
}
