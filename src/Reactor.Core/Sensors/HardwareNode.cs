namespace Reactor.Core.Sensors;

/// <summary>One sensor row in the diagnostics view.</summary>
public sealed record SensorReading(
    string Name,
    SensorKind Kind,
    string Identifier,
    double? Value,
    double? Min,
    double? Max);

/// <summary>
/// Hardware-tree node for the diagnostics view. Mirrors the library's tree but
/// carries no library types, so the view layer stays decoupled.
/// </summary>
public sealed record HardwareNode(
    string Name,
    HardwareKind Kind,
    string Identifier,
    IReadOnlyList<SensorReading> Sensors,
    IReadOnlyList<HardwareNode> Children);
