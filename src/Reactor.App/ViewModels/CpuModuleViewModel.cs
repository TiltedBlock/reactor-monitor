using Avalonia.Media;
using Reactor.App.Theming;
using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Telemetry;

namespace Reactor.App.ViewModels;

/// <summary>Left module: everything about the processor.</summary>
public sealed class CpuModuleViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private TelemetryFrame? _frame;

    public CpuModuleViewModel(AppConfig config) => _config = config;

    public string Subtitle { get; private set; } = "";

    public int HistoryCapacity { get; private set; } = 120;

    public void SetIdentity(SystemIdentity identity)
    {
        Subtitle = identity.CpuName.ToUpperInvariant();
        ThreadLabel = $"{identity.CoreCount} LOGICAL CORES";
        Raise(nameof(Subtitle));
        Raise(nameof(ThreadLabel));
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

    private double? Value(MetricId id) => _frame?.Snapshot.Get(id);

    private StatusThresholds T => _config.Thresholds;

    // ---- headline readouts -------------------------------------------------

    public string LoadValue => ValueFormat.Number(Value(MetricId.CpuLoad), 1);
    public IBrush LoadBrush => Ink(Value(MetricId.CpuLoad) is not null);
    public double LoadRatio => Value(MetricId.CpuLoad) ?? 0;
    public bool LoadAvailable => Value(MetricId.CpuLoad) is not null;

    public string TempValue => ValueFormat.Temperature(Value(MetricId.CpuTemperature), _config.TemperatureUnit, 1);
    public string TempUnit => ValueFormat.TemperatureUnitSymbol(_config.TemperatureUnit);
    public Severity TempSeverity => Theme.Classify(Value(MetricId.CpuTemperature), T.CpuTempHighC, T.CpuTempWarnC);
    public IBrush TempBrush => Theme.ForSeverity(TempSeverity);
    public bool TempAvailable => Value(MetricId.CpuTemperature) is not null;

    /// <summary>Bar scale is always Celsius so the fill means the same thing
    /// regardless of the unit the number is displayed in.</summary>
    public double TempRatio => Value(MetricId.CpuTemperature) ?? 0;

    /// <summary>Session peak drawn as a tick on the temperature gauge.</summary>
    public double? TempPeakMarker => _frame?.Session.PeakCpuTemperature;

    public string PowerValue => ValueFormat.Number(Value(MetricId.CpuPackagePower), 1);
    public IBrush PowerBrush => Ink(Value(MetricId.CpuPackagePower) is not null);
    public bool PowerAvailable => Value(MetricId.CpuPackagePower) is not null;
    public double PowerRatio => Value(MetricId.CpuPackagePower) ?? 0;
    public double PowerScale => Math.Max(20, T.CpuPowerCeilingWatts);
    public string PowerNote => PowerAvailable
        ? $"CEILING {T.CpuPowerCeilingWatts:F0} W"
        : "";

    // ---- secondary readouts ------------------------------------------------

    public string ClockValue => ValueFormat.Clock(Value(MetricId.CpuCoreClock), out _);
    public string ClockUnit { get { ValueFormat.Clock(Value(MetricId.CpuCoreClock), out var u); return u; } }
    public IBrush ClockBrush => Ink(Value(MetricId.CpuCoreClock) is not null);

    public string HotCoreValue => ValueFormat.Temperature(Value(MetricId.CpuHottestCoreTemperature), _config.TemperatureUnit, 1);
    public IBrush HotCoreBrush => Value(MetricId.CpuHottestCoreTemperature) is null
        ? Theme.TextFaint
        : Theme.ForSeverity(Theme.Classify(Value(MetricId.CpuHottestCoreTemperature), T.CpuTempHighC, T.CpuTempWarnC));
    public string HotCoreNote => Value(MetricId.CpuHottestCoreTemperature) is null ? "NOT EXPOSED" : "";

    public string MaxCoreLoadValue => ValueFormat.Number(Value(MetricId.CpuMaxCoreLoad), 1);
    public IBrush MaxCoreLoadBrush => Ink(Value(MetricId.CpuMaxCoreLoad) is not null);
    public string MaxCoreLoadNote =>
        Value(MetricId.CpuMaxCoreLoad) is not null ? "SINGLE HOTTEST THREAD" : "NOT EXPOSED";

    // ---- core activity -----------------------------------------------------

    public IReadOnlyList<double> CoreLoads => _frame?.CoreLoads ?? Array.Empty<double>();
    public string ThreadLabel { get; private set; } = "";
    public bool HasCoreLoads => CoreLoads.Count > 0;

    // ---- history -----------------------------------------------------------

    public double?[] LoadHistory => History(HistoryChannel.CpuLoad);
    public double?[] TempHistory => History(HistoryChannel.CpuTemperature);

    private double?[] History(HistoryChannel channel) =>
        _frame is not null && _frame.History.TryGetValue(channel, out var values)
            ? values
            : Array.Empty<double?>();

    /// <summary>Shown on the module header when core telemetry is unreadable.</summary>
    public string Badge => TempAvailable || PowerAvailable ? "" : "LIMITED";

    private static IBrush Ink(bool available) => available ? Theme.TextBright : Theme.TextFaint;
}
