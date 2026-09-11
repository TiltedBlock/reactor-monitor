using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Sensors;
using Reactor.Core.Telemetry;
using Xunit;

namespace Reactor.Core.Tests;

public class StatusEvaluatorTests
{
    private static readonly AppConfig Config = new();

    private static MetricsSnapshot Snapshot(params (MetricId Id, double Value)[] values) =>
        new(DateTime.UtcNow, values.ToDictionary(v => v.Id, v => v.Value), Array.Empty<double>());

    private static SystemStatus Evaluate(params (MetricId Id, double Value)[] values)
    {
        var snapshot = Snapshot(values);
        return new StatusEvaluator(Config.Thresholds)
            .Evaluate(snapshot, DerivedMetrics.Compute(snapshot, Config));
    }

    [Fact]
    public void No_readings_means_offline()
    {
        Assert.Equal(SystemState.Offline, Evaluate().State);
    }

    [Fact]
    public void An_idle_machine_is_nominal()
    {
        Assert.Equal(SystemState.Nominal,
            Evaluate((MetricId.CpuLoad, 4), (MetricId.GpuLoad, 2), (MetricId.CpuTemperature, 41)).State);
    }

    [Fact]
    public void Moderate_load_reads_active()
    {
        Assert.Equal(SystemState.Active, Evaluate((MetricId.CpuLoad, 40)).State);
    }

    [Fact]
    public void Heavy_load_reads_high_load()
    {
        Assert.Equal(SystemState.HighLoad, Evaluate((MetricId.GpuLoad, 96)).State);
    }

    [Fact]
    public void A_hot_part_outranks_a_busy_one()
    {
        var status = Evaluate((MetricId.CpuLoad, 99), (MetricId.CpuTemperature, 84));

        Assert.Equal(SystemState.ThermalLoad, status.State);
        Assert.Contains("CPU", status.Detail);
    }

    [Fact]
    public void Crossing_the_warning_threshold_outranks_everything()
    {
        var status = Evaluate(
            (MetricId.CpuLoad, 99), (MetricId.CpuTemperature, 84), (MetricId.GpuHotspotTemperature, 104));

        Assert.Equal(SystemState.Warning, status.State);
        Assert.Contains("HOTSPOT", status.Detail);
    }

    [Fact]
    public void The_cards_own_power_budget_drives_power_limited()
    {
        var status = Evaluate((MetricId.GpuLoad, 80), (MetricId.GpuPowerLimitPercent, 99));

        Assert.Equal(SystemState.PowerLimited, status.State);
        Assert.Contains("LIMIT", status.Detail);
    }

    [Fact]
    public void A_relaxed_power_budget_does_not_trip_power_limited()
    {
        Assert.Equal(SystemState.HighLoad,
            Evaluate((MetricId.GpuLoad, 80), (MetricId.GpuPowerLimitPercent, 40)).State);
    }

    [Fact]
    public void Falls_back_to_the_configured_ceiling_when_no_budget_sensor_exists()
    {
        var status = Evaluate((MetricId.GpuLoad, 80), (MetricId.GpuPower, 315));

        Assert.Equal(SystemState.PowerLimited, status.State);
    }

    [Fact]
    public void Thresholds_come_from_configuration_only()
    {
        var relaxed = new StatusThresholds { CpuTempHighC = 95, CpuTempWarnC = 105 };
        var snapshot = Snapshot((MetricId.CpuTemperature, 84), (MetricId.CpuLoad, 10));

        var status = new StatusEvaluator(relaxed)
            .Evaluate(snapshot, DerivedMetrics.Compute(snapshot, Config));

        Assert.Equal(SystemState.Nominal, status.State);
    }

    [Fact]
    public void Every_state_has_a_display_label()
    {
        foreach (var state in Enum.GetValues<SystemState>())
            Assert.False(string.IsNullOrWhiteSpace(state.ToLabel()));
    }
}

public class MetricPlausibilityTests
{
    [Theory]
    [InlineData(MetricId.CpuTemperature, 0)]
    [InlineData(MetricId.GpuTemperature, 0)]
    [InlineData(MetricId.CpuPackagePower, 0)]
    [InlineData(MetricId.CpuCoreClock, 0)]
    public void A_flat_zero_from_an_unreachable_driver_is_rejected(MetricId metric, double value)
    {
        // Unelevated, the library still publishes these sensors; they read 0.
        Assert.False(MetricPlausibility.IsPlausible(metric, value));
    }

    [Theory]
    [InlineData(MetricId.CpuTemperature, 61.5)]
    [InlineData(MetricId.CpuPackagePower, 88.2)]
    [InlineData(MetricId.CpuCoreClock, 5050)]
    public void Real_readings_are_accepted(MetricId metric, double value)
    {
        Assert.True(MetricPlausibility.IsPlausible(metric, value));
    }

    [Fact]
    public void A_stopped_fan_is_a_valid_reading()
    {
        Assert.True(MetricPlausibility.IsPlausible(MetricId.GpuFanRpm, 0));
    }

    [Fact]
    public void An_idle_load_of_zero_is_a_valid_reading()
    {
        Assert.True(MetricPlausibility.IsPlausible(MetricId.GpuLoad, 0));
    }

    [Fact]
    public void Out_of_range_and_non_finite_values_are_rejected()
    {
        Assert.False(MetricPlausibility.IsPlausible(MetricId.CpuTemperature, 400));
        Assert.False(MetricPlausibility.IsPlausible(MetricId.CpuLoad, 140));
        Assert.False(MetricPlausibility.IsPlausible(MetricId.GpuPower, double.NaN));
        Assert.False(MetricPlausibility.IsPlausible(MetricId.GpuPower, double.PositiveInfinity));
    }
}

public class ValueFormatTests
{
    [Fact]
    public void Missing_values_render_as_a_dash_everywhere()
    {
        Assert.Equal(ValueFormat.Missing, ValueFormat.Number(null));
        Assert.Equal(ValueFormat.Missing, ValueFormat.Temperature(null, TemperatureUnit.Celsius));
        Assert.Equal(ValueFormat.Missing, ValueFormat.Gigabytes(null));
        Assert.Equal(ValueFormat.Missing, ValueFormat.Clock(null, out _));
        Assert.Equal(ValueFormat.Missing, ValueFormat.Throughput(null, out _));
    }

    [Fact]
    public void Numbers_use_the_invariant_decimal_point()
    {
        // The app is used on a German locale; a comma here would look broken
        // next to monospaced, right-aligned readouts.
        Assert.Equal("61.4", ValueFormat.Number(61.42, 1));
    }

    [Fact]
    public void Fahrenheit_conversion_is_applied_to_the_displayed_number()
    {
        Assert.Equal("100.0", ValueFormat.Temperature(37.7777778, TemperatureUnit.Fahrenheit, 1));
        Assert.Equal("°F", ValueFormat.TemperatureUnitSymbol(TemperatureUnit.Fahrenheit));
    }

    [Fact]
    public void Clocks_switch_to_gigahertz_above_a_thousand()
    {
        Assert.Equal("4.85", ValueFormat.Clock(4850, out var ghz));
        Assert.Equal("GHz", ghz);

        Assert.Equal("795", ValueFormat.Clock(795, out var mhz));
        Assert.Equal("MHz", mhz);
    }

    [Fact]
    public void Energy_switches_to_kilowatt_hours_above_a_thousand()
    {
        Assert.Equal("240.0", ValueFormat.Energy(240, out var wh));
        Assert.Equal("Wh", wh);

        Assert.Equal("1.250", ValueFormat.Energy(1250, out var kwh));
        Assert.Equal("kWh", kwh);
    }

    [Fact]
    public void Throughput_switches_to_megabytes_above_a_megabyte()
    {
        Assert.Equal("24.6", ValueFormat.Throughput(25_774_508, out var mb));
        Assert.Equal("MB/s", mb);

        Assert.Equal("3", ValueFormat.Throughput(3113, out var kb));
        Assert.Equal("KB/s", kb);
    }

    [Fact]
    public void Durations_grow_an_hours_field_only_when_needed()
    {
        Assert.Equal("07:12", ValueFormat.Duration(TimeSpan.FromSeconds(432)));
        Assert.Equal("02:00:03", ValueFormat.Duration(TimeSpan.FromSeconds(7203)));
    }

    [Fact]
    public void Megabytes_are_shown_as_gigabytes()
    {
        Assert.Equal("16.0", ValueFormat.Gigabytes(16376));
    }
}

public class ConfigTests
{
    [Fact]
    public void Normalization_clamps_values_into_a_usable_range()
    {
        var config = new AppConfig { PollIntervalMs = 5, HistorySeconds = 100000 }.Normalized();

        Assert.Equal(250, config.PollIntervalMs);
        Assert.Equal(900, config.HistorySeconds);
    }

    [Fact]
    public void An_empty_currency_symbol_falls_back_to_the_default()
    {
        Assert.Equal("€", new AppConfig { CurrencySymbol = "  " }.Normalized().CurrencySymbol);
    }

    [Fact]
    public void Psu_efficiency_can_never_be_zero()
    {
        var config = new AppConfig();
        config.WallPower.PsuEfficiency = 0;
        config.Normalized();

        Assert.True(config.WallPower.PsuEfficiency >= 0.5);
    }
}
