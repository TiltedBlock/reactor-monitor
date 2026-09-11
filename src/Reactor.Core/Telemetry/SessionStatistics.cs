namespace Reactor.Core.Telemetry;

/// <summary>Immutable view of session stats handed to the UI each frame.</summary>
public sealed record SessionSnapshot(
    TimeSpan Duration,
    double? PeakCpuTemperature,
    double? PeakGpuTemperature,
    double? PeakGpuHotspot,
    double? PeakComponentPower,
    double PeakWallPower,
    double AverageWallPower,
    double EnergyWattHours,
    double EstimatedCost,
    long SampleCount);

/// <summary>
/// Accumulates peaks and integrates power into energy across the session.
/// Energy uses the trapezoid rule between consecutive samples; a gap longer
/// than <see cref="MaxIntegrationGap"/> (sleep, a stalled poll) is clamped so a
/// suspended machine cannot invent kilowatt-hours.
/// </summary>
public sealed class SessionStatistics
{
    public static readonly TimeSpan MaxIntegrationGap = TimeSpan.FromSeconds(5);

    private DateTime? _startUtc;
    private DateTime? _lastSampleUtc;
    private double? _lastWallPower;
    private double _wallPowerSum;

    public double? PeakCpuTemperature { get; private set; }
    public double? PeakGpuTemperature { get; private set; }
    public double? PeakGpuHotspot { get; private set; }
    public double? PeakComponentPower { get; private set; }
    public double PeakWallPower { get; private set; }
    public double EnergyWattHours { get; private set; }
    public long SampleCount { get; private set; }

    public TimeSpan Duration =>
        _startUtc is null || _lastSampleUtc is null
            ? TimeSpan.Zero
            : _lastSampleUtc.Value - _startUtc.Value;

    public double AverageWallPower => SampleCount == 0 ? 0 : _wallPowerSum / SampleCount;

    public void Update(
        DateTime timestampUtc,
        double? cpuTemperature,
        double? gpuTemperature,
        double? gpuHotspot,
        double? componentPower,
        double wallPower)
    {
        _startUtc ??= timestampUtc;

        PeakCpuTemperature = MaxOf(PeakCpuTemperature, cpuTemperature);
        PeakGpuTemperature = MaxOf(PeakGpuTemperature, gpuTemperature);
        PeakGpuHotspot = MaxOf(PeakGpuHotspot, gpuHotspot);
        PeakComponentPower = MaxOf(PeakComponentPower, componentPower);
        PeakWallPower = Math.Max(PeakWallPower, wallPower);

        if (_lastSampleUtc is { } previous && _lastWallPower is { } previousPower)
        {
            var gap = timestampUtc - previous;
            if (gap > TimeSpan.Zero)
            {
                if (gap > MaxIntegrationGap) gap = MaxIntegrationGap;
                // Trapezoid: average of the two samples over the elapsed hours.
                EnergyWattHours += (previousPower + wallPower) / 2.0 * gap.TotalHours;
            }
        }

        _lastSampleUtc = timestampUtc;
        _lastWallPower = wallPower;
        _wallPowerSum += wallPower;
        SampleCount++;
    }

    public SessionSnapshot ToSnapshot(double pricePerKWh) => new(
        Duration,
        PeakCpuTemperature,
        PeakGpuTemperature,
        PeakGpuHotspot,
        PeakComponentPower,
        PeakWallPower,
        AverageWallPower,
        EnergyWattHours,
        EnergyWattHours / 1000.0 * pricePerKWh,
        SampleCount);

    public void Reset()
    {
        _startUtc = null;
        _lastSampleUtc = null;
        _lastWallPower = null;
        _wallPowerSum = 0;
        PeakCpuTemperature = PeakGpuTemperature = PeakGpuHotspot = PeakComponentPower = null;
        PeakWallPower = 0;
        EnergyWattHours = 0;
        SampleCount = 0;
    }

    private static double? MaxOf(double? current, double? candidate) =>
        candidate is null ? current : current is null ? candidate : Math.Max(current.Value, candidate.Value);
}
