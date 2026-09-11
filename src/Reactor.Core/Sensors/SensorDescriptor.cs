namespace Reactor.Core.Sensors;

/// <summary>Neutral mirror of the sensor categories we care about.</summary>
public enum SensorKind
{
    Unknown, Voltage, Current, Power, Clock, Temperature, Load, Frequency,
    Fan, Flow, Control, Level, Factor, Data, SmallData, Throughput,
    TimeSpan, Energy, Noise
}

/// <summary>Neutral mirror of hardware categories.</summary>
public enum HardwareKind
{
    Unknown, Motherboard, SuperIO, Cpu, Memory, GpuNvidia, GpuAmd, GpuIntel,
    Storage, Network, Cooler, EmbeddedController, Psu, Battery
}

public static class HardwareKindExtensions
{
    public static bool IsGpu(this HardwareKind kind) =>
        kind is HardwareKind.GpuNvidia or HardwareKind.GpuAmd or HardwareKind.GpuIntel;
}

/// <summary>
/// A sensor as seen by the mapping layer. This is deliberately a plain record
/// with no dependency on the hardware library, so the selection logic is fully
/// unit-testable without touching real hardware.
/// </summary>
public sealed record SensorDescriptor(
    string Identifier,
    string Name,
    SensorKind Kind,
    HardwareKind HardwareKind,
    string HardwareName,
    string HardwareIdentifier,
    int HardwareIndex = 0)
{
    public override string ToString() => $"{HardwareName} / {Name} [{Kind}] {Identifier}";
}
