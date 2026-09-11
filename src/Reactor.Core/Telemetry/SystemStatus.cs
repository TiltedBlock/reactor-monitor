namespace Reactor.Core.Telemetry;

/// <summary>Ordered by escalation - higher values win when several apply.</summary>
public enum SystemState
{
    Offline = 0,
    Nominal = 1,
    Active = 2,
    HighLoad = 3,
    PowerLimited = 4,
    ThermalLoad = 5,
    Warning = 6
}

public sealed record SystemStatus(SystemState State, string Label, string Detail)
{
    public static SystemStatus Offline { get; } =
        new(SystemState.Offline, "NO SIGNAL", "awaiting sensor data");
}

public static class SystemStateExtensions
{
    public static string ToLabel(this SystemState state) => state switch
    {
        SystemState.Offline => "NO SIGNAL",
        SystemState.Nominal => "NOMINAL",
        SystemState.Active => "ACTIVE",
        SystemState.HighLoad => "HIGH LOAD",
        SystemState.PowerLimited => "POWER LIMITED",
        SystemState.ThermalLoad => "THERMAL LOAD",
        SystemState.Warning => "WARNING",
        _ => "UNKNOWN"
    };
}
