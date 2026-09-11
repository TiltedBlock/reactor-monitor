using System.Globalization;
using Reactor.Core.Diagnostics;
using Reactor.Core.Metrics;
using Reactor.Core.Sensors;
using Reactor.Hardware;

// Headless sensor dump. Useful for checking what a machine exposes and how the
// mapping resolved, without launching the UI:
//     dotnet run --project tools/Reactor.Probe               summary + mapping
//     dotnet run --project tools/Reactor.Probe -- --all      every raw sensor
//     dotnet run --project tools/Reactor.Probe -- --raw cpu  raw sensors, filtered
//
// Raw values distinguish a missing reading from a zero one: "<null>" means the
// library returned nothing, "0" means it genuinely reported zero. That
// difference is the whole story when a kernel driver is unavailable.

Log.MinimumLevel = LogLevel.Debug;
Log.Initialize(Path.Combine(Path.GetTempPath(), "reactor-probe"));

var showAll = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
var rawIndex = Array.FindIndex(args, a => string.Equals(a, "--raw", StringComparison.OrdinalIgnoreCase));
var rawFilter = rawIndex >= 0 && rawIndex + 1 < args.Length ? args[rawIndex + 1] : null;

using var source = new LibreHardwareSource();
source.Open();

var id = source.Identity;
Console.WriteLine();
Console.WriteLine($"HOST      {id.HostName}");
Console.WriteLine($"CPU       {id.CpuName}  ({id.CoreCount} logical cores)");
Console.WriteLine($"GPU       {id.GpuName}");
Console.WriteLine($"BOARD     {id.MotherboardName}");
Console.WriteLine($"STORAGE   {id.PrimaryStorageName}   (primary drive)");
Console.WriteLine($"SENSORS   {source.Sensors.Count} discovered");
Console.WriteLine();
Console.WriteLine("--- PRIVILEGE / DRIVER ---");
Console.WriteLine($"  elevated ............ {id.Elevated}");
Console.WriteLine($"  PawnIO kernel driver  {(id.CpuKernelDriverPresent ? "present" : "NOT INSTALLED")}");
Console.WriteLine($"  => {CpuDriverProbe.Describe(id.Elevated, id.CpuKernelDriverPresent)}");

Console.WriteLine();
Console.WriteLine("--- METRIC BINDINGS ---");
foreach (var metric in Enum.GetValues<MetricId>())
{
    var binding = source.Map.Binding(metric) ?? source.Map.Limit(metric);
    if (binding is null)
    {
        Console.WriteLine($"  {metric,-30} -- UNRESOLVED --");
        continue;
    }

    var tag = binding.Role == SensorRole.Threshold ? "[LIMIT] " : "";
    Console.WriteLine($"  {metric,-30} {tag}{binding.Describe()}");

    // Name every sensor behind an aggregate so a wrong grouping is obvious.
    if (binding.Sensors.Count > 1)
        foreach (var s in binding.Sensors)
            Console.WriteLine($"  {"",-30}     + {s.Name,-32} {s.Identifier}");
    else
        Console.WriteLine($"  {"",-30}     = {binding.Sensors[0].Identifier}");

    if (binding.IsAmbiguous)
        Console.WriteLine($"  {"",-30}     ! {binding.AllCandidates.Count} candidates, not chosen: " +
                          string.Join(", ", binding.AllCandidates.Skip(1).Select(c => $"'{c.Name}'")));
}

Console.WriteLine();
Console.WriteLine($"--- CORE LOAD SENSORS ({source.Map.CoreLoadSensors.Count}) ---");
Console.WriteLine("  " + string.Join(", ", source.Map.CoreLoadSensors.Select(s => s.Name)));

// Two polls: the first primes rate-based sensors (network, some clocks).
source.Poll();
Thread.Sleep(1200);
var snapshot = source.Poll();

Console.WriteLine();
Console.WriteLine("--- NORMALIZED VALUES ---");
foreach (var metric in Enum.GetValues<MetricId>())
{
    var def = MetricCatalog.Get(metric);
    var value = snapshot.Get(metric) ?? snapshot.GetLimit(metric);
    var limit = snapshot.Limits.ContainsKey(metric) ? " (device limit)" : "";
    Console.WriteLine($"  {metric,-30} {Fmt(value),12}  {def.Unit}{limit}");
}
Console.WriteLine($"  {"CoreLoads",-30} {string.Join(" ", snapshot.CoreLoads.Select(v => v.ToString("F0", CultureInfo.InvariantCulture)))}");

if (showAll || rawFilter is not null)
{
    Console.WriteLine();
    Console.WriteLine("--- RAW SENSORS ---");
    foreach (var node in source.BuildTree()) Print(node, 0, rawFilter);
}

static string Fmt(double? value) =>
    value is null ? "--" : value.Value.ToString("F2", CultureInfo.InvariantCulture);

// "<null>" and "0" are deliberately different: a sensor that exists but returns
// nothing points at a driver problem, one that returns zero points at mapping.
static string Raw(double? value) =>
    value is null ? "<null>"
    : double.IsNaN(value.Value) ? "<NaN>"
    : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

static void Print(HardwareNode node, int depth, string? filter)
{
    var matches = filter is null ||
                  node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                  node.Kind.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);

    if (matches)
    {
        var pad = new string(' ', depth * 2);
        Console.WriteLine($"{pad}[{node.Kind}] {node.Name}   {node.Identifier}");
        foreach (var s in node.Sensors)
            Console.WriteLine(
                $"{pad}   {s.Kind,-12} {s.Name,-34} {Raw(s.Value),10}   " +
                $"min={Raw(s.Min),8} max={Raw(s.Max),8}   " +
                $"{(SensorSemantics.LooksLikeThreshold(s.Name) ? "LIMIT " : "      ")}{s.Identifier}");
    }

    foreach (var child in node.Children) Print(child, depth + 1, filter);
}
