using System.Text.RegularExpressions;
using Reactor.Core.Metrics;

namespace Reactor.Core.Sensors;

public enum Aggregation
{
    /// <summary>Pick the single best-scoring sensor.</summary>
    Best,
    Max,
    Sum,
    Average
}

/// <summary>
/// Declarative description of how to find the sensor(s) behind one metric.
/// Patterns are ordered by preference: an earlier pattern always beats a later
/// one, and aggregation only ever combines sensors matched by the same pattern.
/// </summary>
public sealed class SensorRule
{
    private readonly Regex[] _patterns;
    private readonly Regex[] _excludedHardware;

    public SensorRule(
        MetricId metric,
        SensorKind kind,
        HardwareKind[] hardwareKinds,
        string[] namePatterns,
        Aggregation aggregation = Aggregation.Best,
        double scale = 1.0,
        string[]? excludeHardwarePatterns = null,
        SensorRole role = SensorRole.Telemetry)
    {
        Metric = metric;
        Kind = kind;
        HardwareKinds = hardwareKinds;
        NamePatterns = namePatterns;
        Aggregation = aggregation;
        Scale = scale;
        Role = role;
        ExcludeHardwarePatterns = excludeHardwarePatterns ?? Array.Empty<string>();

        _patterns = Compile(namePatterns);
        _excludedHardware = Compile(ExcludeHardwarePatterns);
    }

    public MetricId Metric { get; }
    public SensorKind Kind { get; }
    public HardwareKind[] HardwareKinds { get; }
    public string[] NamePatterns { get; }
    public Aggregation Aggregation { get; }
    public double Scale { get; }

    /// <summary>Whether this rule is looking for a reading or for a device limit.</summary>
    public SensorRole Role { get; }

    /// <summary>Hardware whose name matches any of these is never considered.</summary>
    public string[] ExcludeHardwarePatterns { get; }

    public bool MatchesHardware(HardwareKind kind) =>
        HardwareKinds.Length == 0 || HardwareKinds.Contains(kind);

    /// <summary>Higher is better; null means the sensor does not qualify at all.</summary>
    public int? Score(SensorDescriptor sensor)
    {
        if (sensor.Kind != Kind) return null;
        if (!MatchesHardware(sensor.HardwareKind)) return null;
        if (_excludedHardware.Any(r => r.IsMatch(sensor.HardwareName))) return null;

        // A firmware limit is never a live reading, and a live reading is never
        // the answer to a rule asking for a limit.
        if (SensorSemantics.RoleOf(sensor.Name) != Role) return null;

        for (var i = 0; i < _patterns.Length; i++)
            if (_patterns[i].IsMatch(sensor.Name))
                return _patterns.Length - i;

        return null;
    }

    private static Regex[] Compile(string[] patterns) => patterns
        .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .ToArray();
}
