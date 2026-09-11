using Reactor.Core.Metrics;
using Reactor.Core.Sensors;

namespace Reactor.Hardware;

/// <summary>
/// The one seam between the application and whatever library reads the
/// hardware. Everything above this interface speaks in <see cref="MetricId"/>.
/// </summary>
public interface ISensorSource : IDisposable
{
    SystemIdentity Identity { get; }

    /// <summary>Every sensor discovered, for the diagnostics view.</summary>
    IReadOnlyList<SensorDescriptor> Sensors { get; }

    SensorMap Map { get; }

    void Open();

    /// <summary>Refreshes hardware and returns one normalized snapshot.</summary>
    MetricsSnapshot Poll();

    /// <summary>Full hardware tree with current values, for diagnostics.</summary>
    IReadOnlyList<HardwareNode> BuildTree();
}
