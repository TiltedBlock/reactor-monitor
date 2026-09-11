using Avalonia.Media;
using Reactor.App.Theming;
using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Telemetry;

namespace Reactor.App.ViewModels;

/// <summary>
/// The band across the bottom: whole-machine figures, derived estimates and
/// session accumulators. Anything modelled rather than measured is labelled EST
/// here and in the tooltip.
/// </summary>
public sealed class SystemBandViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private TelemetryFrame? _frame;

    public SystemBandViewModel(AppConfig config) => _config = config;

    public int HistoryCapacity { get; private set; } = 120;

    private string _driveName = "";
    private bool _elevated;

    public void SetIdentity(SystemIdentity identity)
    {
        _driveName = identity.PrimaryStorageName;
        _elevated = identity.Elevated;
        Raise(nameof(StorageNote));
    }

    public void SetHistoryCapacity(int capacity)
    {
        HistoryCapacity = capacity;
        Raise(nameof(HistoryCapacity));
    }

    public void Update(TelemetryFrame frame)
    {
        _frame = frame;
        RaiseAll();
    }

    private DerivedMetrics? D => _frame?.Derived;
    private SessionSnapshot? S => _frame?.Session;
    private double? Value(MetricId id) => _frame?.Snapshot.Get(id);

    // ---- power -------------------------------------------------------------

    public string ComponentPowerValue => ValueFormat.Number(D?.ComponentPower, 1);
    public IBrush ComponentPowerBrush => D?.ComponentPower is null ? Theme.TextFaint : Theme.Accent;
    public string ComponentPowerNote
    {
        get
        {
            if (D is null) return "";
            var cpu = D.CpuPower is { } c ? $"CPU {c:F0} W" : "CPU —";
            var gpu = D.GpuPower is { } g ? $"GPU {g:F0} W" : "GPU —";
            return $"{cpu}  ·  {gpu}";
        }
    }

    public string WallPowerValue => ValueFormat.Number(D?.WallPower, 0);
    public IBrush WallPowerBrush => Theme.TextBright;
    public string WallPowerNote =>
        $"EST · +{_config.WallPower.BaselineWatts:F0} W BASE / {_config.WallPower.PsuEfficiency:P0} PSU";

    public string HeatValue => ValueFormat.Number(D?.HeatOutputBtuPerHour, 0);
    /// <summary>Essentially all electrical input leaves the case as heat, so
    /// this is the system draw restated in the unit a room heater uses.</summary>
    public string HeatNote => D is null ? "EST" : $"EST · {D.HeatOutputWatts:F0} W INTO THE ROOM";

    // ---- memory ------------------------------------------------------------

    public bool MemoryAvailable => D?.MemoryUsedGb is not null;
    public string MemoryValue => ValueFormat.Number(D?.MemoryUsedGb, 1);
    public string MemoryNote => D?.MemoryTotalGb is { } total
        ? $"OF {total:F1} GB · {Value(MetricId.MemoryLoad):F0} %"
        : "";
    public double MemoryRatio => Value(MetricId.MemoryLoad) ?? 0;
    public IBrush MemoryBrush => MemoryAvailable ? Theme.TextBright : Theme.TextFaint;

    // ---- storage / network -------------------------------------------------

    public bool StorageAvailable => Value(MetricId.StorageTemperature) is not null;
    public string StorageValue => ValueFormat.Temperature(Value(MetricId.StorageTemperature), _config.TemperatureUnit, 0);
    public string StorageUnit => ValueFormat.TemperatureUnitSymbol(_config.TemperatureUnit);
    public IBrush StorageBrush => StorageAvailable
        ? Theme.ForSeverity(Theme.Classify(Value(MetricId.StorageTemperature),
            _config.Thresholds.StorageTempHighC, _config.Thresholds.StorageTempWarnC))
        : Theme.TextFaint;
    /// <summary>Names the drive being reported so the number is unambiguous on
    /// a machine with several NVMe devices.</summary>
    public string StorageNote => StorageAvailable
        ? Shorten(_driveName)
        : _elevated ? "NO DRIVE SENSOR" : "NEEDS ADMIN";

    private static string Shorten(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "PRIMARY DRIVE"
            : (name.Length > 22 ? name[..22].TrimEnd() : name).ToUpperInvariant();

    public string NetDownValue => ValueFormat.Throughput(Value(MetricId.NetworkDownload), out _);
    public string NetDownUnit { get { ValueFormat.Throughput(Value(MetricId.NetworkDownload), out var u); return u; } }
    public string NetUpValue => ValueFormat.Throughput(Value(MetricId.NetworkUpload), out _);
    public string NetUpUnit { get { ValueFormat.Throughput(Value(MetricId.NetworkUpload), out var u); return u; } }
    public IBrush NetBrush => Value(MetricId.NetworkDownload) is null ? Theme.TextFaint : Theme.TextBright;

    // ---- session accumulators ---------------------------------------------

    public string EnergyValue => S is null ? ValueFormat.Missing : ValueFormat.Energy(S.EnergyWattHours, out _);
    public string EnergyUnit { get { if (S is null) return "Wh"; ValueFormat.Energy(S.EnergyWattHours, out var u); return u; } }
    public string EnergyNote => "EST · INTEGRATED";

    public string CostValue => S is null ? ValueFormat.Missing : ValueFormat.Money(S.EstimatedCost, 3);
    public string CostUnit => _config.CurrencySymbol;
    public string CostNote => $"EST · {_config.ElectricityPricePerKWh:F2} {_config.CurrencySymbol}/kWh";

    public string CostRateValue => D is null ? ValueFormat.Missing : ValueFormat.Money(D.CostPerHour, 3);
    public string CostRateUnit => $"{_config.CurrencySymbol}/H";
    public string CostRateNote => "EST · AT CURRENT DRAW";

    public string AvgDrawValue => S is null ? ValueFormat.Missing : ValueFormat.Number(S.AverageWallPower, 0);
    public string AvgDrawNote => S is null ? "" : $"EST · {S.SampleCount} SAMPLES";

    // ---- session peaks -----------------------------------------------------

    public string PeakCpuValue => ValueFormat.Temperature(S?.PeakCpuTemperature, _config.TemperatureUnit, 1);
    public IBrush PeakCpuBrush => Peak(S?.PeakCpuTemperature);

    public string PeakGpuValue => ValueFormat.Temperature(S?.PeakGpuTemperature, _config.TemperatureUnit, 1);
    public IBrush PeakGpuBrush => Peak(S?.PeakGpuTemperature);

    public string PeakHotspotValue => ValueFormat.Temperature(S?.PeakGpuHotspot, _config.TemperatureUnit, 1);
    public IBrush PeakHotspotBrush => Peak(S?.PeakGpuHotspot);

    public string PeakPowerValue => ValueFormat.Number(S?.PeakComponentPower, 0);
    public IBrush PeakPowerBrush => Peak(S?.PeakComponentPower);

    public string TempUnit => ValueFormat.TemperatureUnitSymbol(_config.TemperatureUnit);

    private static IBrush Peak(double? value) => value is null ? Theme.TextFaint : Theme.TextMid;

    // ---- history -----------------------------------------------------------

    public double?[] PowerHistory =>
        _frame is not null && _frame.History.TryGetValue(HistoryChannel.TotalPower, out var values)
            ? values
            : Array.Empty<double?>();

    public string PowerHistoryCaption =>
        $"CPU + GPU PACKAGE POWER · LAST {_config.HistorySeconds} S";
}
