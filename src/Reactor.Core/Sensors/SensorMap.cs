using Reactor.Core.Metrics;

namespace Reactor.Core.Sensors;

/// <summary>The sensors chosen for one metric, plus how to combine them.</summary>
public sealed record MetricBinding(
    MetricId Metric,
    IReadOnlyList<SensorDescriptor> Sensors,
    Aggregation Aggregation,
    double Scale,
    SensorRole Role = SensorRole.Telemetry,
    IReadOnlyList<SensorDescriptor>? Candidates = null)
{
    /// <summary>
    /// Every sensor that qualified for this metric, including the ones not
    /// chosen. Surfaced in diagnostics so an ambiguous mapping is visible
    /// before it becomes a wrong reading.
    /// </summary>
    public IReadOnlyList<SensorDescriptor> AllCandidates => Candidates ?? Sensors;

    /// <summary>True when several sensors competed for a single-value metric.</summary>
    public bool IsAmbiguous => Aggregation == Aggregation.Best && AllCandidates.Count > 1;

    public string Describe() =>
        Sensors.Count == 1
            ? $"{Sensors[0].HardwareName} / {Sensors[0].Name}"
            : $"{Aggregation} of {Sensors.Count} × {(Sensors.Count > 0 ? Sensors[0].Name : "?")}";
}

/// <summary>Result of running <see cref="SensorMapper"/> over discovered sensors.</summary>
public sealed class SensorMap
{
    public SensorMap(
        IReadOnlyDictionary<MetricId, MetricBinding> bindings,
        IReadOnlyList<MetricId> unresolved,
        IReadOnlyList<SensorDescriptor> coreLoadSensors,
        IReadOnlyDictionary<MetricId, MetricBinding>? limits = null)
    {
        Bindings = bindings;
        Unresolved = unresolved;
        CoreLoadSensors = coreLoadSensors;
        Limits = limits ?? new Dictionary<MetricId, MetricBinding>();
    }

    public static SensorMap Empty { get; } = new(
        new Dictionary<MetricId, MetricBinding>(),
        Array.Empty<MetricId>(),
        Array.Empty<SensorDescriptor>());

    /// <summary>Live telemetry bindings.</summary>
    public IReadOnlyDictionary<MetricId, MetricBinding> Bindings { get; }

    /// <summary>Device-reported limit bindings, kept out of the telemetry path.</summary>
    public IReadOnlyDictionary<MetricId, MetricBinding> Limits { get; }

    /// <summary>Metrics no sensor could be found for on this machine.</summary>
    public IReadOnlyList<MetricId> Unresolved { get; }

    /// <summary>Per-core load sensors in core order, for the core activity strip.</summary>
    public IReadOnlyList<SensorDescriptor> CoreLoadSensors { get; }

    public bool Has(MetricId id) => Bindings.ContainsKey(id);

    public MetricBinding? Binding(MetricId id) =>
        Bindings.TryGetValue(id, out var b) ? b : null;

    public MetricBinding? Limit(MetricId id) =>
        Limits.TryGetValue(id, out var b) ? b : null;

    /// <summary>Bindings where more than one sensor could have been chosen.</summary>
    public IEnumerable<MetricBinding> AmbiguousBindings =>
        Bindings.Values.Concat(Limits.Values).Where(b => b.IsAmbiguous);
}
