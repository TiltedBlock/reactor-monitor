using Reactor.Core.Configuration;
using Reactor.Core.Metrics;
using Reactor.Core.Presentation;
using Reactor.Core.Sensors;
using Reactor.Core.Telemetry;
using Xunit;

namespace Reactor.Core.Tests;

/// <summary>
/// A reading that stops arriving must disappear from the board, not linger.
/// The whole pipeline is built so that each frame carries only what was read
/// this cycle - these tests pin that property down.
/// </summary>
public class AvailabilityTests
{
    private static AppConfig Config() => new();

    private static MetricsSnapshot Snapshot(params (MetricId Id, double Value)[] values) =>
        new(DateTime.UtcNow, values.ToDictionary(v => v.Id, v => v.Value), Array.Empty<double>());

    [Fact]
    public void A_metric_that_stops_reporting_reads_as_unavailable_on_the_next_frame()
    {
        var processor = new TelemetryProcessor(Config());

        var withDrive = processor.Process(
            Snapshot((MetricId.StorageTemperature, 52), (MetricId.CpuLoad, 30)));
        Assert.Equal(52, withDrive.Snapshot.Get(MetricId.StorageTemperature));

        // Drive access lost - the sensor simply is not in the next snapshot.
        var withoutDrive = processor.Process(Snapshot((MetricId.CpuLoad, 30)));

        Assert.Null(withoutDrive.Snapshot.Get(MetricId.StorageTemperature));
        Assert.False(withoutDrive.Snapshot.Has(MetricId.StorageTemperature));
    }

    [Fact]
    public void An_unavailable_value_formats_as_a_dash_rather_than_a_number()
    {
        var processor = new TelemetryProcessor(Config());
        processor.Process(Snapshot((MetricId.StorageTemperature, 52)));
        var frame = processor.Process(Snapshot());

        Assert.Equal(ValueFormat.Missing,
            ValueFormat.Temperature(frame.Snapshot.Get(MetricId.StorageTemperature),
                TemperatureUnit.Celsius, 0));
    }

    [Fact]
    public void A_failed_poll_blanks_every_reading()
    {
        var processor = new TelemetryProcessor(Config());
        processor.Process(Snapshot(
            (MetricId.CpuLoad, 94), (MetricId.GpuLoad, 99), (MetricId.GpuTemperature, 70)));

        var blank = processor.Process(MetricsSnapshot.Unavailable(DateTime.UtcNow));

        Assert.True(blank.Snapshot.IsEmpty);
        Assert.Null(blank.Snapshot.Get(MetricId.CpuLoad));
        Assert.Null(blank.Snapshot.Get(MetricId.GpuTemperature));
        Assert.Equal(SystemState.Offline, blank.Status.State);
    }

    [Fact]
    public void A_gap_is_recorded_in_history_rather_than_a_zero()
    {
        var processor = new TelemetryProcessor(Config());
        processor.Process(Snapshot((MetricId.GpuTemperature, 70)));
        processor.Process(Snapshot());
        processor.Process(Snapshot((MetricId.GpuTemperature, 72)));

        var series = processor.History[HistoryChannel.GpuTemperature].ToArray();

        Assert.Equal(new double?[] { 70, null, 72 }, series);
    }

    [Fact]
    public void Session_peaks_survive_a_dropout_without_being_reset()
    {
        var processor = new TelemetryProcessor(Config());
        processor.Process(Snapshot((MetricId.GpuTemperature, 81)));
        var afterGap = processor.Process(Snapshot());

        // The peak is a record of the session, not a live reading.
        Assert.Equal(81, afterGap.Session.PeakGpuTemperature);
        Assert.Null(afterGap.Snapshot.Get(MetricId.GpuTemperature));
    }

    [Fact]
    public void An_unresolved_metric_never_appears_in_a_snapshot()
    {
        // No storage hardware at all, as when running unelevated.
        var map = SensorMapper.Build(new[]
        {
            new SensorDescriptor("/amdcpu/0/load/0", "CPU Total", SensorKind.Load,
                HardwareKind.Cpu, "AMD Ryzen 7 7800X3D", "/amdcpu/0")
        });

        Assert.Contains(MetricId.StorageTemperature, map.Unresolved);
        Assert.False(map.Has(MetricId.StorageTemperature));
        Assert.Null(map.Binding(MetricId.StorageTemperature));
    }

    [Fact]
    public void An_implausible_reading_is_dropped_rather_than_displayed()
    {
        // Package sensors exist but report a flat zero when the kernel driver
        // is unavailable; that is absence, not a measurement.
        Assert.False(MetricPlausibility.IsPlausible(MetricId.StorageTemperature, 0));
        Assert.False(MetricPlausibility.IsPlausible(MetricId.CpuPackagePower, 0));
    }

    [Fact]
    public void Status_never_escalates_on_a_metric_that_is_unavailable()
    {
        var snapshot = Snapshot((MetricId.CpuLoad, 5));
        var status = new StatusEvaluator(new StatusThresholds())
            .Evaluate(snapshot, DerivedMetrics.Compute(snapshot, Config()));

        // No temperatures at all - must not invent a thermal condition.
        Assert.Equal(SystemState.Nominal, status.State);
    }

    [Fact]
    public void Derived_power_degrades_gracefully_when_cpu_power_is_unavailable()
    {
        var config = Config();
        var derived = DerivedMetrics.Compute(Snapshot((MetricId.GpuPower, 286)), config);

        // Measured component power reports only what was measured...
        Assert.Null(derived.CpuPower);
        Assert.Equal(286, derived.ComponentPower);
        // ...while the wall estimate substitutes and flags itself.
        Assert.True(derived.IsEstimate);
        Assert.Equal((config.WallPower.AssumedCpuWattsWhenUnknown + 286 + config.WallPower.BaselineWatts)
                     / config.WallPower.PsuEfficiency, derived.WallPower, 6);
    }
}
