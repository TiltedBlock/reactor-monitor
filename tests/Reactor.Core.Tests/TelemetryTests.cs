using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Telemetry;
using Xunit;

namespace Reactor.Core.Tests;

public class RollingSeriesTests
{
    [Fact]
    public void Keeps_samples_in_order_until_it_is_full()
    {
        var series = new RollingSeries(4);
        series.Add(1);
        series.Add(2);
        series.Add(3);

        Assert.Equal(3, series.Count);
        Assert.Equal(new double?[] { 1, 2, 3 }, series.ToArray());
        Assert.Equal(3, series.Latest);
    }

    [Fact]
    public void Discards_the_oldest_sample_once_full()
    {
        var series = new RollingSeries(3);
        foreach (var v in new double[] { 1, 2, 3, 4, 5 }) series.Add(v);

        Assert.Equal(3, series.Count);
        Assert.Equal(new double?[] { 3, 4, 5 }, series.ToArray());
        Assert.Equal(5, series.Latest);
    }

    [Fact]
    public void Wraps_repeatedly_without_corrupting_order()
    {
        var series = new RollingSeries(5);
        for (var i = 1; i <= 23; i++) series.Add(i);

        Assert.Equal(new double?[] { 19, 20, 21, 22, 23 }, series.ToArray());
    }

    [Fact]
    public void Gaps_are_preserved_rather_than_treated_as_zero()
    {
        var series = new RollingSeries(4);
        series.Add(10);
        series.Add(null);
        series.Add(30);

        Assert.Equal(new double?[] { 10, null, 30 }, series.ToArray());
        Assert.Equal(30, series.Max());
        Assert.Equal(10, series.Min());
    }

    [Fact]
    public void Extremes_are_null_when_nothing_readable_has_arrived()
    {
        var series = new RollingSeries(3);
        Assert.Null(series.Max());

        series.Add(null);
        Assert.Null(series.Max());
        Assert.Null(series.Latest);
    }

    [Fact]
    public void Clear_returns_the_series_to_empty()
    {
        var series = new RollingSeries(3);
        series.Add(1);
        series.Add(2);
        series.Clear();

        Assert.Equal(0, series.Count);
        Assert.Empty(series.ToArray());
    }

    [Theory]
    [InlineData(120, 1000, 120)]
    [InlineData(120, 500, 240)]
    [InlineData(60, 750, 80)]
    public void History_capacity_follows_the_poll_interval(int seconds, int intervalMs, int expected)
    {
        Assert.Equal(expected, HistoryStore.ForWindow(seconds, intervalMs).Capacity);
    }
}

public class SessionStatisticsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Tracks_the_maximum_of_each_channel()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, 61, 45, 55, 120, 200);
        stats.Update(T0.AddSeconds(1), 78, 42, 71, 340, 420);
        stats.Update(T0.AddSeconds(2), 70, 44, 60, 300, 380);

        Assert.Equal(78, stats.PeakCpuTemperature);
        Assert.Equal(45, stats.PeakGpuTemperature);
        Assert.Equal(71, stats.PeakGpuHotspot);
        Assert.Equal(340, stats.PeakComponentPower);
        Assert.Equal(420, stats.PeakWallPower);
    }

    [Fact]
    public void A_missing_reading_does_not_reset_an_established_peak()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, 81, null, null, null, 300);
        stats.Update(T0.AddSeconds(1), null, null, null, null, 300);

        Assert.Equal(81, stats.PeakCpuTemperature);
        Assert.Null(stats.PeakGpuTemperature);
    }

    [Fact]
    public void Integrates_a_constant_draw_into_energy()
    {
        var stats = new SessionStatistics();

        // 360 W held for one hour = 360 Wh.
        for (var i = 0; i <= 3600; i++)
            stats.Update(T0.AddSeconds(i), null, null, null, null, 360);

        Assert.Equal(360, stats.EnergyWattHours, 3);
    }

    [Fact]
    public void Uses_the_trapezoid_rule_across_a_ramp()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, null, null, null, null, 100);
        stats.Update(T0.AddSeconds(2), null, null, null, null, 300);

        // Mean of 100 and 300 over 2 s = 200 W * (2/3600) h.
        Assert.Equal(200 * (2.0 / 3600), stats.EnergyWattHours, 9);
    }

    [Fact]
    public void The_first_sample_contributes_no_energy()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, null, null, null, null, 500);

        Assert.Equal(0, stats.EnergyWattHours);
        Assert.Equal(TimeSpan.Zero, stats.Duration);
    }

    [Fact]
    public void A_long_gap_is_clamped_so_sleep_cannot_invent_energy()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, null, null, null, null, 400);
        // Machine suspended for eight hours between samples.
        stats.Update(T0.AddHours(8), null, null, null, null, 400);

        var clamped = 400 * SessionStatistics.MaxIntegrationGap.TotalHours;
        Assert.Equal(clamped, stats.EnergyWattHours, 9);
    }

    [Fact]
    public void Averages_and_counts_every_sample()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, null, null, null, null, 100);
        stats.Update(T0.AddSeconds(1), null, null, null, null, 200);
        stats.Update(T0.AddSeconds(2), null, null, null, null, 300);

        Assert.Equal(200, stats.AverageWallPower);
        Assert.Equal(3, stats.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(2), stats.Duration);
    }

    [Fact]
    public void Snapshot_costs_the_accumulated_energy()
    {
        var stats = new SessionStatistics();
        for (var i = 0; i <= 3600; i++)
            stats.Update(T0.AddSeconds(i), null, null, null, null, 1000);

        var snapshot = stats.ToSnapshot(0.32);

        Assert.Equal(1000, snapshot.EnergyWattHours, 3);
        Assert.Equal(0.32, snapshot.EstimatedCost, 4);
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var stats = new SessionStatistics();
        stats.Update(T0, 80, 70, 90, 300, 400);
        stats.Update(T0.AddSeconds(1), 80, 70, 90, 300, 400);
        stats.Reset();

        Assert.Null(stats.PeakCpuTemperature);
        Assert.Equal(0, stats.EnergyWattHours);
        Assert.Equal(0, stats.SampleCount);
        Assert.Equal(TimeSpan.Zero, stats.Duration);
    }
}

public class DerivedMetricsTests
{
    private static MetricsSnapshot Snapshot(params (MetricId Id, double Value)[] values) =>
        new(DateTime.UtcNow, values.ToDictionary(v => v.Id, v => v.Value), Array.Empty<double>());

    private static AppConfig Config()
    {
        var config = new AppConfig();
        config.WallPower.BaselineWatts = 50;
        config.WallPower.PsuEfficiency = 0.9;
        config.ElectricityPricePerKWh = 0.30;
        return config;
    }

    [Fact]
    public void Component_power_is_the_sum_of_what_was_measured()
    {
        var derived = DerivedMetrics.Compute(
            Snapshot((MetricId.CpuPackagePower, 80), (MetricId.GpuPower, 250)), Config());

        Assert.Equal(330, derived.ComponentPower);
        Assert.False(derived.IsEstimate);
    }

    [Fact]
    public void Wall_power_adds_the_baseline_and_psu_losses()
    {
        var derived = DerivedMetrics.Compute(
            Snapshot((MetricId.CpuPackagePower, 80), (MetricId.GpuPower, 250)), Config());

        Assert.Equal((80 + 250 + 50) / 0.9, derived.WallPower, 6);
    }

    [Fact]
    public void Substituted_component_power_marks_the_result_an_estimate()
    {
        var config = Config();
        config.WallPower.AssumedCpuWattsWhenUnknown = 45;

        var derived = DerivedMetrics.Compute(Snapshot((MetricId.GpuPower, 250)), config);

        Assert.True(derived.IsEstimate);
        Assert.Null(derived.CpuPower);
        Assert.Equal(250, derived.ComponentPower); // only what was measured
        Assert.Equal((45 + 250 + 50) / 0.9, derived.WallPower, 6);
    }

    [Fact]
    public void Component_power_is_null_only_when_neither_side_reports()
    {
        Assert.Null(DerivedMetrics.Compute(Snapshot(), Config()).ComponentPower);
    }

    [Fact]
    public void Heat_output_restates_wall_power_in_btu()
    {
        var derived = DerivedMetrics.Compute(
            Snapshot((MetricId.CpuPackagePower, 100), (MetricId.GpuPower, 200)), Config());

        Assert.Equal(derived.WallPower, derived.HeatOutputWatts);
        Assert.Equal(derived.WallPower * 3.412142, derived.HeatOutputBtuPerHour, 6);
    }

    [Fact]
    public void Cost_per_hour_follows_the_configured_tariff()
    {
        var config = Config();
        var derived = DerivedMetrics.Compute(
            Snapshot((MetricId.CpuPackagePower, 100), (MetricId.GpuPower, 200)), config);

        Assert.Equal(derived.WallPower / 1000 * 0.30, derived.CostPerHour, 9);
    }

    [Fact]
    public void Vram_total_is_reconstructed_from_used_plus_free()
    {
        var derived = DerivedMetrics.Compute(
            Snapshot((MetricId.GpuMemoryUsed, 2000), (MetricId.GpuMemoryFree, 14000)), Config());

        Assert.Equal(16000, derived.VramTotalMb);
        Assert.Equal(12.5, derived.VramPercent!.Value, 6);
    }

    [Fact]
    public void Vram_percentage_is_null_without_a_total()
    {
        var derived = DerivedMetrics.Compute(Snapshot((MetricId.GpuMemoryUsed, 2000)), Config());

        Assert.Null(derived.VramTotalMb);
        Assert.Null(derived.VramPercent);
    }

    [Fact]
    public void Memory_total_needs_both_halves()
    {
        var both = DerivedMetrics.Compute(
            Snapshot((MetricId.MemoryUsed, 21), (MetricId.MemoryAvailable, 10)), Config());
        Assert.Equal(31, both.MemoryTotalGb);

        var half = DerivedMetrics.Compute(Snapshot((MetricId.MemoryUsed, 21)), Config());
        Assert.Null(half.MemoryTotalGb);
    }
}
