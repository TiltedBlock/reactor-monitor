using Reactor.Core.Diagnostics;

namespace Reactor.Core.Sensors;

/// <summary>
/// Picks the GPU the dashboard should report on. Modern desktop CPUs ship an
/// integrated GPU that the library reports alongside the discrete card, and
/// enumeration order does not reliably put the interesting one first - so score
/// the devices by how much telemetry they actually expose.
/// </summary>
public static class GpuSelector
{
    public static string? SelectPrimary(IReadOnlyList<SensorDescriptor> sensors)
    {
        var gpus = sensors
            .Where(s => s.HardwareKind.IsGpu())
            .GroupBy(s => s.HardwareIdentifier)
            .ToList();

        if (gpus.Count == 0) return null;
        if (gpus.Count == 1) return gpus[0].Key;

        var ranked = gpus
            .Select(g => (Identifier: g.Key, Name: g.First().HardwareName, Score: Score(g.ToList()), Count: g.Count()))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Count)
            .ToList();

        foreach (var g in ranked)
            Log.Debug_($"  GPU candidate '{g.Name}' score={g.Score} sensors={g.Count}");
        Log.Info($"Primary GPU selected: {ranked[0].Name}");

        return ranked[0].Identifier;
    }

    private static int Score(IReadOnlyList<SensorDescriptor> sensors)
    {
        var score = 0;
        // A discrete card reports board power, a hotspot, a fan and its own VRAM;
        // an integrated one typically reports little more than load and a temp.
        if (sensors.Any(s => s.Kind == SensorKind.Power)) score += 5;
        if (sensors.Any(s => s.Kind == SensorKind.Temperature && s.Name.Contains("Hot", StringComparison.OrdinalIgnoreCase))) score += 3;
        if (sensors.Any(s => s.Kind == SensorKind.Fan)) score += 2;
        if (sensors.Any(s => s.Kind == SensorKind.SmallData && s.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))) score += 2;
        if (sensors[0].HardwareKind == HardwareKind.GpuNvidia) score += 1;
        return score;
    }

    /// <summary>
    /// Drops sensors belonging to secondary GPUs so the mapper cannot bind a
    /// metric to the integrated device. Non-GPU sensors pass through untouched.
    /// </summary>
    public static IReadOnlyList<SensorDescriptor> RestrictToPrimaryGpu(
        IReadOnlyList<SensorDescriptor> sensors, string? primaryGpuIdentifier)
    {
        if (primaryGpuIdentifier is null) return sensors;

        return sensors
            .Where(s => !s.HardwareKind.IsGpu() || s.HardwareIdentifier == primaryGpuIdentifier)
            .ToList();
    }
}
