using Reactor.Core.Diagnostics;

namespace Reactor.Core.Sensors;

/// <summary>
/// Picks the one drive the dashboard reports on.
///
/// A machine with several NVMe drives exposes several "Composite Temperature"
/// sensors. Taking the maximum across drives is exactly the mistake that made
/// the board report a firmware limit as a live reading, so the primary drive is
/// chosen explicitly and every other drive stays in diagnostics only.
/// </summary>
public static class StorageSelector
{
    public static string? SelectPrimary(IReadOnlyList<SensorDescriptor> sensors)
    {
        var drives = sensors
            .Where(s => s.HardwareKind == HardwareKind.Storage)
            .GroupBy(s => s.HardwareIdentifier)
            .ToList();

        if (drives.Count == 0) return null;
        if (drives.Count == 1) return drives[0].Key;

        var ranked = drives
            .Select(g => (
                Identifier: g.Key,
                Name: g.First().HardwareName,
                Index: g.Min(s => s.HardwareIndex),
                Score: Score(g.ToList())))
            .OrderByDescending(x => x.Score)
            // Enumeration follows physical drive order, so the lowest index is
            // the system drive on any normal configuration.
            .ThenBy(x => x.Index)
            .ToList();

        foreach (var d in ranked)
            Log.Debug_($"  Storage candidate '{d.Name}' score={d.Score} index={d.Index}");
        Log.Info($"Primary storage selected: {ranked[0].Name}");

        return ranked[0].Identifier;
    }

    private static int Score(IReadOnlyList<SensorDescriptor> sensors)
    {
        var score = 0;

        // A drive that reports a live composite temperature is the one worth
        // putting on the board.
        if (sensors.Any(s => s.Kind == SensorKind.Temperature &&
                             SensorSemantics.RoleOf(s.Name) == SensorRole.Telemetry))
            score += 4;

        if (sensors.Any(s => s.Kind == SensorKind.Load)) score += 1;

        return score;
    }

    /// <summary>
    /// Drops sensors belonging to secondary drives. Non-storage sensors pass
    /// through untouched.
    /// </summary>
    public static IReadOnlyList<SensorDescriptor> RestrictToPrimary(
        IReadOnlyList<SensorDescriptor> sensors, string? primaryIdentifier)
    {
        if (primaryIdentifier is null) return sensors;

        return sensors
            .Where(s => s.HardwareKind != HardwareKind.Storage ||
                        s.HardwareIdentifier == primaryIdentifier)
            .ToList();
    }
}
