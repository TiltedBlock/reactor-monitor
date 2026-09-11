using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Reactor.Core.Diagnostics;
using Reactor.Core.Metrics;
using Reactor.Core.Sensors;
using CoreSensorKind = Reactor.Core.Sensors.SensorKind;
using CoreHardwareKind = Reactor.Core.Sensors.HardwareKind;

namespace Reactor.Hardware;

/// <summary>
/// LibreHardwareMonitor-backed sensor source. This is the only file in the
/// solution that knows the library exists.
/// </summary>
public sealed class LibreHardwareSource : ISensorSource
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly Dictionary<string, ISensor> _byIdentifier = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private bool _opened;
    private bool _disposed;

    public LibreHardwareSource()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            // Left off: they add enumeration cost and nothing the dashboard shows.
            IsControllerEnabled = false,
            IsPsuEnabled = false,
            IsBatteryEnabled = false
        };
    }

    public SystemIdentity Identity { get; private set; } = SystemIdentity.Unknown;

    public IReadOnlyList<SensorDescriptor> Sensors { get; private set; } = Array.Empty<SensorDescriptor>();

    public SensorMap Map { get; private set; } = SensorMap.Empty;

    public void Open()
    {
        lock (_gate)
        {
            if (_opened) return;

            var elevated = IsElevated();
            var driver = CpuDriverProbe.IsKernelDriverPresent();
            Log.Info($"Opening hardware monitor: {CpuDriverProbe.Describe(elevated, driver)}");
            _computer.Open();

            // One update pass first: some sensors only materialise after the
            // initial refresh (GPU power, per-core clocks).
            SafeUpdate();
            Discover();
            _opened = true;
        }
    }

    private void Discover()
    {
        var descriptors = new List<SensorDescriptor>();
        var index = 0;

        _byIdentifier.Clear();

        foreach (var hardware in _computer.Hardware)
            CollectHardware(hardware, descriptors, ref index);

        Sensors = descriptors;

        // Narrow the candidate set to one GPU and one drive before mapping, so
        // no rule can ever aggregate across devices.
        var primaryGpu = GpuSelector.SelectPrimary(descriptors);
        var primaryStorage = StorageSelector.SelectPrimary(descriptors);

        var mappable = GpuSelector.RestrictToPrimaryGpu(descriptors, primaryGpu);
        mappable = StorageSelector.RestrictToPrimary(mappable, primaryStorage);
        Map = SensorMapper.Build(mappable);

        Identity = BuildIdentity(descriptors, primaryGpu, primaryStorage);
        Log.Info($"Host {Identity.HostName} | CPU '{Identity.CpuName}' | GPU '{Identity.GpuName}' | {descriptors.Count} sensors");
    }

    private void CollectHardware(IHardware hardware, List<SensorDescriptor> into, ref int index)
    {
        var hardwareIndex = index++;
        var kind = MapHardwareKind(hardware.HardwareType);

        foreach (var sensor in hardware.Sensors)
        {
            var id = sensor.Identifier.ToString() ?? string.Empty;
            if (id.Length == 0) continue;

            _byIdentifier[id] = sensor;
            into.Add(new SensorDescriptor(
                id,
                sensor.Name ?? string.Empty,
                MapSensorKind(sensor.SensorType),
                kind,
                hardware.Name ?? string.Empty,
                hardware.Identifier.ToString() ?? string.Empty,
                hardwareIndex));
        }

        foreach (var sub in hardware.SubHardware)
            CollectHardware(sub, into, ref index);
    }

    private SystemIdentity BuildIdentity(
        IReadOnlyList<SensorDescriptor> descriptors, string? primaryGpu, string? primaryStorage)
    {
        string FirstHardwareName(CoreHardwareKind kind) =>
            descriptors.FirstOrDefault(d => d.HardwareKind == kind)?.HardwareName ?? string.Empty;

        var cpu = FirstHardwareName(CoreHardwareKind.Cpu);
        var gpu = primaryGpu is null
            ? descriptors.FirstOrDefault(d => d.HardwareKind.IsGpu())?.HardwareName ?? string.Empty
            : descriptors.First(d => d.HardwareIdentifier == primaryGpu).HardwareName;

        var storage = primaryStorage is null
            ? FirstHardwareName(CoreHardwareKind.Storage)
            : descriptors.First(d => d.HardwareIdentifier == primaryStorage).HardwareName;

        return new SystemIdentity(
            Environment.MachineName.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(cpu) ? "CPU UNAVAILABLE" : Clean(cpu),
            string.IsNullOrWhiteSpace(gpu) ? "GPU UNAVAILABLE" : Clean(gpu),
            FirstHardwareName(CoreHardwareKind.Motherboard),
            Clean(storage),
            Map.CoreLoadSensors.Count > 0 ? Map.CoreLoadSensors.Count : Environment.ProcessorCount,
            IsElevated(),
            CpuDriverProbe.IsKernelDriverPresent());
    }

    /// <summary>Strips the marketing noise vendors put in the product string.</summary>
    private static string Clean(string name) => name
        .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
        .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
        .Replace("  ", " ")
        .Trim();

    public MetricsSnapshot Poll()
    {
        lock (_gate)
        {
            if (!_opened || _disposed) return MetricsSnapshot.Unavailable(DateTime.UtcNow);

            SafeUpdate();

            var values = new Dictionary<MetricId, double>();

            foreach (var (metric, binding) in Map.Bindings)
            {
                var readings = new List<double?>(binding.Sensors.Count);
                foreach (var sensor in binding.Sensors)
                    readings.Add(ReadValue(sensor.Identifier));

                var combined = SensorMapper.Combine(binding, readings);
                if (combined is { } v && MetricPlausibility.IsPlausible(metric, v))
                    values[metric] = v;
            }

            // Device-reported limits, kept in their own bucket so they can
            // never be mistaken for a reading.
            var limits = new Dictionary<MetricId, double>();
            foreach (var (metric, binding) in Map.Limits)
            {
                var readings = new List<double?>(binding.Sensors.Count);
                foreach (var sensor in binding.Sensors)
                    readings.Add(ReadValue(sensor.Identifier));

                if (SensorMapper.Combine(binding, readings) is { } limit &&
                    MetricPlausibility.IsPlausible(metric, limit))
                    limits[metric] = limit;
            }

            var coreLoads = new List<double>(Map.CoreLoadSensors.Count);
            foreach (var sensor in Map.CoreLoadSensors)
                coreLoads.Add(ReadValue(sensor.Identifier) ?? 0);

            return new MetricsSnapshot(DateTime.UtcNow, values, coreLoads, limits);
        }
    }

    private void SafeUpdate()
    {
        // A single misbehaving device must not stop the rest of the machine from
        // being read, so each root is updated independently.
        foreach (var hardware in _computer.Hardware)
        {
            try
            {
                hardware.Accept(_visitor);
            }
            catch (Exception ex)
            {
                LogOnce($"update:{hardware.Identifier}", $"Update failed for '{hardware.Name}'", ex);
            }
        }
    }

    private double? ReadValue(string identifier)
    {
        if (!_byIdentifier.TryGetValue(identifier, out var sensor)) return null;

        try
        {
            var value = sensor.Value;
            if (value is null) return null;
            var d = (double)value.Value;
            return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
        }
        catch (Exception ex)
        {
            LogOnce($"read:{identifier}", $"Read failed for sensor '{identifier}'", ex);
            return null;
        }
    }

    public IReadOnlyList<HardwareNode> BuildTree()
    {
        lock (_gate)
        {
            if (!_opened || _disposed) return Array.Empty<HardwareNode>();

            SafeUpdate();
            return _computer.Hardware.Select(BuildNode).ToList();
        }
    }

    private HardwareNode BuildNode(IHardware hardware) => new(
        hardware.Name ?? string.Empty,
        MapHardwareKind(hardware.HardwareType),
        hardware.Identifier.ToString() ?? string.Empty,
        hardware.Sensors
            .OrderBy(s => s.SensorType)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SensorReading(
                s.Name ?? string.Empty,
                MapSensorKind(s.SensorType),
                s.Identifier.ToString() ?? string.Empty,
                ToDouble(s.Value), ToDouble(s.Min), ToDouble(s.Max)))
            .ToList(),
        hardware.SubHardware.Select(BuildNode).ToList());

    private static double? ToDouble(float? value) =>
        value is null || float.IsNaN(value.Value) || float.IsInfinity(value.Value)
            ? null
            : value.Value;

    // Enum names are matched by string so a library update that adds members
    // degrades to Unknown instead of silently mis-binding.
    private static CoreSensorKind MapSensorKind(SensorType type) =>
        Enum.TryParse<CoreSensorKind>(type.ToString(), true, out var kind) ? kind : CoreSensorKind.Unknown;

    private static CoreHardwareKind MapHardwareKind(HardwareType type) =>
        Enum.TryParse<CoreHardwareKind>(type.ToString(), true, out var kind) ? kind : CoreHardwareKind.Unknown;

    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A flaky sensor would otherwise fill the log with the same line.</summary>
    private void LogOnce(string key, string message, Exception ex)
    {
        if (_loggedFailures.Add(key)) Log.Warn($"{message}: {ex.GetType().Name}: {ex.Message}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_opened) _computer.Close();
                Log.Info("Hardware monitor closed");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to close hardware monitor", ex);
            }
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
