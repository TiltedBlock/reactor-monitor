using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Reactor.App.Theming;
using Reactor.Core.Configuration;
using Reactor.Core.Diagnostics;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Telemetry;
using Reactor.Hardware;

namespace Reactor.App.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly TelemetryProcessor _processor;
    private readonly LibreHardwareSource _source;
    private readonly TelemetryService _service;

    private TelemetryFrame? _frame;
    private string? _fault;

    public MainViewModel(AppConfig config, ConfigStore store)
    {
        _config = config;
        _store = store;

        Cpu = new CpuModuleViewModel(config);
        Gpu = new GpuModuleViewModel(config);
        System = new SystemBandViewModel(config);
        Diagnostics = new DiagnosticsViewModel();

        _processor = new TelemetryProcessor(config);
        _source = new LibreHardwareSource();
        _service = new TelemetryService(_source, _processor, config);

        _service.SourceReady += OnSourceReady;
        _service.FrameProduced += OnFrameProduced;
        _service.Failed += OnFailed;

        PriceText = config.ElectricityPricePerKWh.ToString("0.###", CultureInfo.InvariantCulture);
        IntervalText = config.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        HistoryText = config.HistorySeconds.ToString(CultureInfo.InvariantCulture);

        ResetSessionCommand = new RelayCommand(ResetSession);
        ToggleDiagnosticsCommand = new RelayCommand(() => IsDiagnosticsOpen = !IsDiagnosticsOpen);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        RefreshDiagnosticsCommand = new RelayCommand(RefreshDiagnostics);
        ApplySettingsCommand = new RelayCommand(ApplySettings);
        ElevateCommand = new RelayCommand(RestartElevated);
    }

    public CpuModuleViewModel Cpu { get; }
    public GpuModuleViewModel Gpu { get; }
    public SystemBandViewModel System { get; }
    public DiagnosticsViewModel Diagnostics { get; }

    public RelayCommand ResetSessionCommand { get; }
    public RelayCommand ToggleDiagnosticsCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand RefreshDiagnosticsCommand { get; }
    public RelayCommand ApplySettingsCommand { get; }
    public RelayCommand ElevateCommand { get; }

    public void Start()
    {
        var capacity = _processor.History.Capacity;
        Cpu.SetHistoryCapacity(capacity);
        Gpu.SetHistoryCapacity(capacity);
        System.SetHistoryCapacity(capacity);
        _service.Start();
    }

    // ---- telemetry plumbing ------------------------------------------------

    private void OnSourceReady(ISensorSource source) =>
        Dispatcher.UIThread.Post(() =>
        {
            var identity = source.Identity;
            HostName = identity.HostName;
            IsElevated = identity.Elevated;
            CpuKernelDriverPresent = identity.CpuKernelDriverPresent;
            Cpu.SetIdentity(identity);
            Gpu.SetIdentity(identity);
            System.SetIdentity(identity);
            Diagnostics.SetSource(source);
            UnresolvedSummary = source.Map.Unresolved.Count == 0
                ? "ALL MAPPED"
                : $"{source.Map.Unresolved.Count} UNMAPPED";
            RaiseAll();
        });

    private void OnFrameProduced(TelemetryFrame frame) =>
        Dispatcher.UIThread.Post(() =>
        {
            _frame = frame;
            Cpu.Update(frame);
            Gpu.Update(frame);
            System.Update(frame);
            RaiseAll();
        });

    private void OnFailed(Exception ex) =>
        Dispatcher.UIThread.Post(() =>
        {
            _fault = ex.Message;
            RaiseAll();
        });

    // ---- identity / status -------------------------------------------------

    public string HostName { get; private set; } = Environment.MachineName.ToUpperInvariant();

    public bool IsElevated { get; private set; }

    public string UnresolvedSummary { get; private set; } = "";

    public SystemState State => _frame?.Status.State ?? SystemState.Offline;

    public string StatusLabel => _fault is not null ? "SENSOR FAULT" : (_frame?.Status.Label ?? "INITIALISING");

    public string StatusDetail => _fault ?? _frame?.Status.Detail ?? "opening hardware monitor";

    public IBrush StatusBrush => _fault is not null ? Theme.Alert : Theme.ForState(State);

    public string SessionTime => ValueFormat.Duration(_frame?.Session.Duration ?? TimeSpan.Zero);

    public string SampleCount => (_frame?.Session.SampleCount ?? 0).ToString("N0", CultureInfo.InvariantCulture);

    public string IntervalLabel => $"{_config.PollIntervalMs} MS";

    public bool CpuKernelDriverPresent { get; private set; }

    /// <summary>
    /// True whenever the CPU package sensors exist but never produce a value.
    /// The library publishes them regardless, so their absence is the symptom;
    /// the cause is either missing privileges or a missing kernel driver.
    /// </summary>
    public bool IsTelemetryLimited =>
        _frame is not null &&
        !_frame.Snapshot.IsEmpty &&
        _frame.Snapshot.Get(MetricId.CpuTemperature) is null &&
        _frame.Snapshot.Get(MetricId.CpuPackagePower) is null;

    /// <summary>
    /// Reading package temperature, power and per-core clocks means reading
    /// model-specific registers, which needs a kernel driver. Elevation alone
    /// is not enough, so the banner names the actual blocker rather than
    /// telling the user to keep re-launching as administrator.
    /// </summary>
    public string LimitedMessage => (IsElevated, CpuKernelDriverPresent) switch
    {
        (false, _) => "CPU PACKAGE TEMPERATURE, POWER AND CORE CLOCKS REQUIRE ADMINISTRATOR RIGHTS",
        (true, false) => "CPU REGISTER ACCESS NEEDS THE PAWNIO KERNEL DRIVER — NOT INSTALLED ON THIS MACHINE",
        (true, true) => "CPU PACKAGE SENSORS ARE PRESENT BUT RETURNING NO VALUE — SEE THE LOG"
    };

    /// <summary>Only offer the elevation button when elevation is the blocker.</summary>
    public bool IsElevationActionable => !IsElevated;

    // ---- diagnostics / settings panels ------------------------------------

    private bool _isDiagnosticsOpen;
    public bool IsDiagnosticsOpen
    {
        get => _isDiagnosticsOpen;
        set
        {
            if (!Set(ref _isDiagnosticsOpen, value)) return;
            if (value)
            {
                IsSettingsOpen = false;
                RefreshDiagnostics();
            }
        }
    }

    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (!Set(ref _isSettingsOpen, value)) return;
            if (value) IsDiagnosticsOpen = false;
        }
    }

    private void RefreshDiagnostics() => Diagnostics.Refresh();

    // ---- settings ----------------------------------------------------------

    private string _priceText = "";
    public string PriceText { get => _priceText; set => Set(ref _priceText, value); }

    private string _intervalText = "";
    public string IntervalText { get => _intervalText; set => Set(ref _intervalText, value); }

    private string _historyText = "";
    public string HistoryText { get => _historyText; set => Set(ref _historyText, value); }

    public bool UseFahrenheit
    {
        get => _config.TemperatureUnit == TemperatureUnit.Fahrenheit;
        set
        {
            _config.TemperatureUnit = value ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;
            Raise();
            PushFrameToChildren();
        }
    }

    public string SettingsHint => "Poll interval and history window take effect on restart.";

    public string ConfigPath => _store.Path;

    public string LogPath => Log.FilePath ?? "(no log file)";

    private void ApplySettings()
    {
        if (double.TryParse(PriceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var price))
            _config.ElectricityPricePerKWh = price;

        if (int.TryParse(IntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
            _config.PollIntervalMs = interval;

        if (int.TryParse(HistoryText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var history))
            _config.HistorySeconds = history;

        _config.Normalized();
        _store.Save(_config);

        PriceText = _config.ElectricityPricePerKWh.ToString("0.###", CultureInfo.InvariantCulture);
        IntervalText = _config.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        HistoryText = _config.HistorySeconds.ToString(CultureInfo.InvariantCulture);

        PushFrameToChildren();
        IsSettingsOpen = false;
    }

    /// <summary>Re-renders every readout after a settings change, without
    /// waiting for the next poll.</summary>
    private void PushFrameToChildren()
    {
        if (_frame is null) return;
        Cpu.Update(_frame);
        Gpu.Update(_frame);
        System.Update(_frame);
        RaiseAll();
    }

    private void ResetSession()
    {
        _processor.ResetSession();
        Log.Info("Session statistics reset");
    }

    /// <summary>
    /// Relaunches through the shell with an elevation verb. Windows shows its
    /// own consent prompt; if the user declines we simply stay as we are.
    /// </summary>
    private void RestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;

            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Log.Info("Relaunching elevated");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            // Almost always "user declined the UAC prompt".
            Log.Warn($"Elevated restart not completed: {ex.Message}");
        }
    }

    public void Dispose() => _service.Dispose();
}
