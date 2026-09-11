using System.Text.Json.Serialization;

namespace Reactor.Core.Configuration;

public enum TemperatureUnit { Celsius, Fahrenheit }

/// <summary>
/// Every tunable number in the application lives here. Thresholds are aesthetic
/// (they drive the status banner), not a safety system.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Sensor poll interval. Clamped to a sane range on load.</summary>
    public int PollIntervalMs { get; set; } = 750;

    /// <summary>Electricity price used for the session cost estimate.</summary>
    public double ElectricityPricePerKWh { get; set; } = 0.32;

    public string CurrencySymbol { get; set; } = "\u20ac";

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    /// <summary>Seconds of rolling history retained for the graph strips.</summary>
    public int HistorySeconds { get; set; } = 120;

    public bool StartMinimized { get; set; }

    public WallPowerModel WallPower { get; set; } = new();

    public StatusThresholds Thresholds { get; set; } = new();

    public AppConfig Normalized()
    {
        PollIntervalMs = Math.Clamp(PollIntervalMs, 250, 5000);
        HistorySeconds = Math.Clamp(HistorySeconds, 30, 900);
        ElectricityPricePerKWh = Math.Clamp(ElectricityPricePerKWh, 0, 10);
        if (string.IsNullOrWhiteSpace(CurrencySymbol)) CurrencySymbol = "\u20ac";
        WallPower.Normalize();
        return this;
    }
}

/// <summary>
/// Turns measured CPU+GPU package power into an estimate of what the whole
/// machine pulls from the wall. Everything here is an approximation and the UI
/// labels it as such.
/// </summary>
public sealed class WallPowerModel
{
    /// <summary>Rest-of-system draw: board, RAM, drives, fans, peripherals.</summary>
    public double BaselineWatts { get; set; } = 45;

    /// <summary>Typical PSU conversion efficiency at mid load.</summary>
    public double PsuEfficiency { get; set; } = 0.90;

    /// <summary>Assumed draw when a component reports no power sensor at all.</summary>
    public double AssumedCpuWattsWhenUnknown { get; set; } = 45;

    public double AssumedGpuWattsWhenUnknown { get; set; } = 30;

    public void Normalize()
    {
        BaselineWatts = Math.Clamp(BaselineWatts, 0, 400);
        PsuEfficiency = Math.Clamp(PsuEfficiency, 0.5, 1.0);
        AssumedCpuWattsWhenUnknown = Math.Clamp(AssumedCpuWattsWhenUnknown, 0, 400);
        AssumedGpuWattsWhenUnknown = Math.Clamp(AssumedGpuWattsWhenUnknown, 0, 800);
    }
}

/// <summary>Single place where all status-banner thresholds are defined.</summary>
public sealed class StatusThresholds
{
    public double LoadActivePercent { get; set; } = 25;
    public double LoadHighPercent { get; set; } = 75;

    public double CpuTempHighC { get; set; } = 80;
    public double CpuTempWarnC { get; set; } = 90;

    public double GpuTempHighC { get; set; } = 75;
    public double GpuTempWarnC { get; set; } = 85;

    public double GpuHotspotHighC { get; set; } = 90;
    public double GpuHotspotWarnC { get; set; } = 100;

    public double StorageTempHighC { get; set; } = 60;
    public double StorageTempWarnC { get; set; } = 72;

    /// <summary>Approximate sustained package power ceilings, used only to light
    /// the POWER LIMITED indicator. Adjust per machine in the config file.</summary>
    public double CpuPowerCeilingWatts { get; set; } = 100;
    public double GpuPowerCeilingWatts { get; set; } = 320;

    /// <summary>Fraction of the ceiling at which a component counts as pinned.</summary>
    public double PowerCeilingRatio { get; set; } = 0.95;
}
