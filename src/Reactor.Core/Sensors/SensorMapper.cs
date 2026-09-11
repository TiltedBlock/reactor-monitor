using System.Text.RegularExpressions;
using Reactor.Core.Diagnostics;
using Reactor.Core.Metrics;

namespace Reactor.Core.Sensors;

/// <summary>
/// Turns a flat list of discovered sensors into a metric -> sensor binding map.
/// Pure and side-effect free apart from logging, so it can be tested against
/// captured sensor listings from any machine.
/// </summary>
public static class SensorMapper
{
    private static readonly Regex CoreLoadPattern =
        new(@"^CPU Core #(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static SensorMap Build(IReadOnlyList<SensorDescriptor> sensors)
        => Build(sensors, SensorRules.All);

    public static SensorMap Build(IReadOnlyList<SensorDescriptor> sensors, IReadOnlyList<SensorRule> rules)
    {
        var bindings = new Dictionary<MetricId, MetricBinding>();
        var limits = new Dictionary<MetricId, MetricBinding>();
        var unresolved = new List<MetricId>();

        foreach (var rule in rules)
        {
            var binding = Resolve(rule, sensors);
            if (binding is null)
            {
                // A missing firmware limit is normal and not worth reporting as
                // an unmapped metric - the configured threshold covers it.
                if (rule.Role == SensorRole.Telemetry) unresolved.Add(rule.Metric);
                continue;
            }

            if (rule.Role == SensorRole.Threshold) limits[rule.Metric] = binding;
            else bindings[rule.Metric] = binding;
        }

        var coreLoads = DiscoverCoreLoadSensors(sensors);

        Log.Info($"Sensor map: {bindings.Count} metric(s) bound, {limits.Count} device limit(s), " +
                 $"{unresolved.Count} unresolved, {coreLoads.Count} CPU core load sensor(s)");

        foreach (var (metric, binding) in bindings.Concat(limits).OrderBy(b => b.Key.ToString()))
        {
            var tag = binding.Role == SensorRole.Threshold ? " [LIMIT]" : "";
            var ambiguous = binding.IsAmbiguous
                ? $"  (ambiguous: {binding.AllCandidates.Count} candidates - " +
                  string.Join(", ", binding.AllCandidates.Select(c => $"'{c.Name}'")) + ")"
                : "";
            Log.Debug_($"  {metric,-30}{tag} -> {binding.Describe()}{ambiguous}");
        }

        if (unresolved.Count > 0)
            Log.Warn("Unresolved metrics: " + string.Join(", ", unresolved));

        return new SensorMap(bindings, unresolved, coreLoads, limits);
    }

    private static MetricBinding? Resolve(SensorRule rule, IReadOnlyList<SensorDescriptor> sensors)
    {
        var scored = new List<(SensorDescriptor Sensor, int Score)>();
        foreach (var sensor in sensors)
        {
            var score = rule.Score(sensor);
            if (score.HasValue) scored.Add((sensor, score.Value));
        }

        if (scored.Count == 0) return null;

        var candidates = scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Sensor.HardwareIndex)
            .Select(x => x.Sensor)
            .ToList();

        List<SensorDescriptor> chosen;
        if (rule.Aggregation == Aggregation.Best)
        {
            // Best pattern match wins; ties break toward the first hardware
            // device, which is the primary CPU/GPU in enumeration order.
            var best = scored
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Sensor.HardwareIndex)
                .ThenBy(x => x.Sensor.Identifier, StringComparer.OrdinalIgnoreCase)
                .First();
            chosen = new List<SensorDescriptor> { best.Sensor };
        }
        else
        {
            // Aggregate over everything matching the single best pattern, so
            // "Core #1..#8" never gets mixed with a fallback pattern's hits.
            var topScore = scored.Max(x => x.Score);
            chosen = scored
                .Where(x => x.Score == topScore)
                .Select(x => x.Sensor)
                .OrderBy(x => x.HardwareIndex)
                .ThenBy(x => NaturalKey(x.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new MetricBinding(rule.Metric, chosen, rule.Aggregation, rule.Scale, rule.Role, candidates);
    }

    /// <summary>
    /// Per-core load sensors, ordered by their core number rather than
    /// lexically (so #10 does not sort between #1 and #2).
    /// </summary>
    private static List<SensorDescriptor> DiscoverCoreLoadSensors(IReadOnlyList<SensorDescriptor> sensors)
    {
        return sensors
            .Where(s => s.Kind == SensorKind.Load && s.HardwareKind == HardwareKind.Cpu)
            .Select(s => (Sensor: s, Match: CoreLoadPattern.Match(s.Name)))
            .Where(x => x.Match.Success)
            .OrderBy(x => int.Parse(x.Match.Groups[1].Value))
            .Select(x => x.Sensor)
            .ToList();
    }

    /// <summary>Pads digit runs so "#2" sorts before "#10".</summary>
    private static string NaturalKey(string name) =>
        Regex.Replace(name, @"\d+", m => m.Value.PadLeft(6, '0'));

    /// <summary>Combines raw sensor readings according to the binding's aggregation.</summary>
    public static double? Combine(MetricBinding binding, IReadOnlyList<double?> readings)
    {
        var values = readings.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (values.Count == 0) return null;

        var result = binding.Aggregation switch
        {
            Aggregation.Max => values.Max(),
            Aggregation.Sum => values.Sum(),
            Aggregation.Average => values.Average(),
            _ => values[0]
        };

        return result * binding.Scale;
    }
}
