using Avalonia.Media;
using Reactor.App.Theming;
using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Telemetry;

namespace Reactor.App.ViewModels;

/// <summary>Right module: everything about the graphics card.</summary>
public sealed class GpuModuleViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private TelemetryFrame? _frame;

    public GpuModuleViewModel(AppConfig config) => _config = config;

    public string Subtitle { get; private set; } = "";

    public int HistoryCapacity { get; private set; } = 120;

    public void SetIdentity(SystemIdentity identity)
    {
        Subtitle = identity.GpuName.ToUpperInvariant();
        Raise(nameof(Subtitle));
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

    public string LoadValue => ValueFormat.Number(Value(MetricId.GpuLoad), 1);
    public IBrush LoadBrush => Ink(Value(MetricId.GpuLoad) is not null);
    public double LoadRatio => Value(MetricId.GpuLoad) ?? 0;
    public bool LoadAvailable => Value(MetricId.GpuLoad) is not null;

    public string TempValue => ValueFormat.Temperature(Value(MetricId.GpuTemperature), _config.TemperatureUnit, 1);
    public string TempUnit => ValueFormat.TemperatureUnitSymbol(_config.TemperatureUnit);
    public Severity TempSeverity => Theme.Classify(Value(MetricId.GpuTemperature), T.GpuTempHighC, T.GpuTempWarnC);
    public IBrush TempBrush => Theme.ForSeverity(TempSeverity);
    public bool TempAvailable => Value(MetricId.GpuTemperature) is not null;
    public double TempRatio => Value(MetricId.GpuTemperature) ?? 0;

    /// <summary>Session peak drawn as a tick on the temperature gauge.</summary>
    public double? TempPeakMarker => _frame?.Session.PeakGpuTemperature;

    public string PowerValue => ValueFormat.Number(Value(MetricId.GpuPower), 1);
    public IBrush PowerBrush => Ink(Value(MetricId.GpuPower) is not null);
    public bool PowerAvailable => Value(MetricId.GpuPower) is not null;

    /// <summary>Fraction of the board power limit, straight from the card when
    /// it publishes it - no guessed wattage ceiling involved.</summary>
    public double PowerRatio => Value(MetricId.GpuPowerLimitPercent) ?? 0;
    public bool PowerBudgetAvailable => Value(MetricId.GpuPowerLimitPercent) is not null;
    public string PowerNote => PowerBudgetAvailable
        ? $"{ValueFormat.Number(Value(MetricId.GpuPowerLimitPercent), 0)} % OF LIMIT"
        : "";

    // ---- secondary readouts ------------------------------------------------

    public string HotspotValue => ValueFormat.Temperature(Value(MetricId.GpuHotspotTemperature), _config.TemperatureUnit, 1);
    public IBrush HotspotBrush => Value(MetricId.GpuHotspotTemperature) is null
        ? Theme.TextFaint
        : Theme.ForSeverity(Theme.Classify(Value(MetricId.GpuHotspotTemperature), T.GpuHotspotHighC, T.GpuHotspotWarnC));
    public string HotspotNote => Value(MetricId.GpuHotspotTemperature) is null ? "NOT EXPOSED" : "";

    public string VramTempValue => ValueFormat.Temperature(Value(MetricId.GpuMemoryTemperature), _config.TemperatureUnit, 0);
    public IBrush VramTempBrush => Value(MetricId.GpuMemoryTemperature) is null
        ? Theme.TextFaint
        : Theme.ForSeverity(Theme.Classify(Value(MetricId.GpuMemoryTemperature), T.GpuHotspotHighC, T.GpuHotspotWarnC));

    public string ClockValue => ValueFormat.Clock(Value(MetricId.GpuCoreClock), out _);
    public string ClockUnit { get { ValueFormat.Clock(Value(MetricId.GpuCoreClock), out var u); return u; } }
    public IBrush ClockBrush => Ink(Value(MetricId.GpuCoreClock) is not null);

    public string FanValue => Value(MetricId.GpuFanRpm) is { } rpm
        ? ValueFormat.Number(rpm, 0)
        : ValueFormat.Number(Value(MetricId.GpuFanPercent), 0);
    public string FanUnit => Value(MetricId.GpuFanRpm) is not null ? "RPM" : "%";
    public IBrush FanBrush => Ink(Value(MetricId.GpuFanRpm) is not null || Value(MetricId.GpuFanPercent) is not null);

    /// <summary>Zero RPM is a feature on modern cards, not a missing reading.</summary>
    public string FanNote => Value(MetricId.GpuFanRpm) is 0 ? "IDLE / STOPPED" : "";

    // ---- VRAM --------------------------------------------------------------

    public bool VramAvailable => _frame?.Derived.VramUsedMb is not null;
    public string VramUsedValue => ValueFormat.Gigabytes(_frame?.Derived.VramUsedMb, 1);
    public string VramTotalValue => ValueFormat.Gigabytes(_frame?.Derived.VramTotalMb, 1);
    public string VramSummary => VramAvailable
        ? $"{VramUsedValue} / {VramTotalValue} GB"
        : ValueFormat.Missing;
    public double VramRatio => _frame?.Derived.VramPercent ?? 0;
    public string VramPercentText => _frame?.Derived.VramPercent is { } p
        ? ValueFormat.Number(p, 0) + " %"
        : ValueFormat.Missing;
    public IBrush VramBrush => Ink(VramAvailable);

    // ---- history -----------------------------------------------------------

    public double?[] LoadHistory => History(HistoryChannel.GpuLoad);
    public double?[] TempHistory => History(HistoryChannel.GpuTemperature);

    private double?[] History(HistoryChannel channel) =>
        _frame is not null && _frame.History.TryGetValue(channel, out var values)
            ? values
            : Array.Empty<double?>();

    public string Badge => LoadAvailable ? "" : "LIMITED";

    private static IBrush Ink(bool available) => available ? Theme.TextBright : Theme.TextFaint;
}
