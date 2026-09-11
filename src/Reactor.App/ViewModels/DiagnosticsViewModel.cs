using System.Collections.ObjectModel;
using System.Globalization;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Sensors;
using Reactor.Hardware;

namespace Reactor.App.ViewModels;

/// <summary>A hardware device and its sensors, flattened for display.</summary>
public sealed class DiagnosticsGroup
{
    public required string Header { get; init; }
    public required string Kind { get; init; }
    public required string Identifier { get; init; }
    public required IReadOnlyList<DiagnosticsRow> Rows { get; init; }
}

public sealed class DiagnosticsRow
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Value { get; init; }
    public required string Identifier { get; init; }

    /// <summary>Set when this sensor is the one a dashboard metric reads.</summary>
    public string MappedTo { get; init; } = "";

    /// <summary>True for firmware limits, which are never live readings.</summary>
    public bool IsThreshold { get; init; }

    public string RoleTag => IsThreshold ? "LIMIT" : "";
    public bool IsMapped => MappedTo.Length > 0;
}

/// <summary>One canonical metric and the sensor decision behind it.</summary>
public sealed class MappingRow
{
    public required string Metric { get; init; }
    public required string Source { get; init; }
    public required string Detail { get; init; }
    public bool IsAmbiguous { get; init; }
    public bool IsLimit { get; init; }
    public string Tag => IsLimit ? "LIMIT" : IsAmbiguous ? "AMBIGUOUS" : "";
}

/// <summary>
/// The utilitarian second screen: which sensor feeds each canonical metric,
/// then every discovered device and raw sensor. Built after a wrong storage
/// mapping put a firmware limit on the dashboard - the mapping table exists so
/// that class of mistake is visible before it is believed.
/// </summary>
public sealed class DiagnosticsViewModel : ViewModelBase
{
    private ISensorSource? _source;

    public ObservableCollection<MappingRow> Mappings { get; } = new();

    public ObservableCollection<DiagnosticsGroup> Groups { get; } = new();

    public ObservableCollection<string> Unresolved { get; } = new();

    public string Summary { get; private set; } = "no source";

    public bool HasUnresolved => Unresolved.Count > 0;

    public bool HasAmbiguous => Mappings.Any(m => m.IsAmbiguous);

    public string AmbiguousSummary
    {
        get
        {
            var names = Mappings.Where(m => m.IsAmbiguous).Select(m => m.Metric).ToList();
            return names.Count == 0
                ? ""
                : $"{names.Count} METRIC(S) HAD MORE THAN ONE CANDIDATE SENSOR: {string.Join(", ", names)}";
        }
    }

    public void SetSource(ISensorSource source)
    {
        _source = source;
        Refresh();
    }

    public void Refresh()
    {
        if (_source is null) return;

        BuildMappings(_source.Map);

        // Which raw sensor identifier feeds which metric, so each row can say so.
        var mapped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (metric, binding) in _source.Map.Bindings.Concat(_source.Map.Limits))
        foreach (var sensor in binding.Sensors)
        {
            if (!mapped.TryGetValue(sensor.Identifier, out var list))
                mapped[sensor.Identifier] = list = new List<string>();
            list.Add(metric.ToString());
        }

        Groups.Clear();
        var sensorCount = 0;

        foreach (var node in _source.BuildTree())
            AddGroup(node, mapped, ref sensorCount);

        Unresolved.Clear();
        foreach (var metric in _source.Map.Unresolved.OrderBy(m => m.ToString()))
            Unresolved.Add($"{metric}  ·  {MetricCatalog.Get(metric).Label}");

        Summary = $"{Groups.Count} DEVICES · {sensorCount} SENSORS · " +
                  $"{_source.Map.Bindings.Count} MAPPED · {_source.Map.Limits.Count} LIMITS · " +
                  $"{_source.Map.Unresolved.Count} UNMAPPED";

        Raise(nameof(Summary));
        Raise(nameof(HasUnresolved));
        Raise(nameof(HasAmbiguous));
        Raise(nameof(AmbiguousSummary));
    }

    private void BuildMappings(SensorMap map)
    {
        Mappings.Clear();

        foreach (var (metric, binding) in map.Bindings.Concat(map.Limits).OrderBy(b => b.Key.ToString()))
        {
            var extra = binding.AllCandidates.Count - binding.Sensors.Count;

            Mappings.Add(new MappingRow
            {
                Metric = metric.ToString(),
                Source = binding.Describe(),
                Detail = binding.Aggregation == Aggregation.Best
                    ? extra > 0
                        ? $"BEST OF {binding.AllCandidates.Count} · NOT CHOSEN: " +
                          string.Join(", ", binding.AllCandidates.Skip(1).Take(4).Select(c => c.Name))
                        : "SINGLE CANDIDATE"
                    : $"{binding.Aggregation.ToString().ToUpperInvariant()} OF " + DescribeParts(binding),
                IsAmbiguous = binding.IsAmbiguous,
                IsLimit = binding.Role == SensorRole.Threshold
            });
        }
    }

    /// <summary>
    /// Names the members of an aggregate. Several devices often publish the
    /// identically named sensor (five adapters all called "Download Speed"), so
    /// fall back to the device name when the sensor names carry no information.
    /// </summary>
    private static string DescribeParts(MetricBinding binding)
    {
        var distinctNames = binding.Sensors.Select(s => s.Name).Distinct().Count();
        var labels = binding.Sensors
            .Select(s => distinctNames > 1 ? s.Name : s.HardwareName)
            .ToList();

        return string.Join(", ", labels.Take(4)) +
               (labels.Count > 4 ? $", +{labels.Count - 4} MORE" : "");
    }

    private void AddGroup(HardwareNode node, IReadOnlyDictionary<string, List<string>> mapped, ref int count)
    {
        var rows = new List<DiagnosticsRow>();

        foreach (var sensor in node.Sensors)
        {
            count++;
            rows.Add(new DiagnosticsRow
            {
                Name = sensor.Name,
                Kind = sensor.Kind.ToString().ToUpperInvariant(),
                Value = sensor.Value is { } v
                    ? v.ToString("0.###", CultureInfo.InvariantCulture)
                    : ValueFormat.Missing,
                Identifier = sensor.Identifier,
                IsThreshold = SensorSemantics.LooksLikeThreshold(sensor.Name),
                MappedTo = mapped.TryGetValue(sensor.Identifier, out var metrics)
                    ? string.Join(", ", metrics)
                    : ""
            });
        }

        Groups.Add(new DiagnosticsGroup
        {
            Header = node.Name.ToUpperInvariant(),
            Kind = node.Kind.ToString().ToUpperInvariant(),
            Identifier = node.Identifier,
            Rows = rows
        });

        foreach (var child in node.Children)
            AddGroup(child, mapped, ref count);
    }
}
