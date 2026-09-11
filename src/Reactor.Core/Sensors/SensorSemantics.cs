using System.Text.RegularExpressions;

namespace Reactor.Core.Sensors;

/// <summary>What a sensor actually is, as opposed to what it measures.</summary>
public enum SensorRole
{
    /// <summary>A live reading that changes as the machine runs.</summary>
    Telemetry,

    /// <summary>A device-reported limit or rating. Constant, and never a measurement.</summary>
    Threshold
}

/// <summary>
/// Distinguishes live readings from device-reported limits.
///
/// This exists because of a real failure: an NVMe drive publishes
/// "Composite Temperature" (52 &#176;C, live), "Temperature #2" (65 &#176;C, live),
/// "Warning Temperature" (83 &#176;C) and "Critical Temperature" (88 &#176;C). The last
/// two are constants baked into the drive firmware, but they are ordinary
/// temperature sensors as far as the monitoring library is concerned. A rule
/// that matched "Temperature" loosely and aggregated with Max reported the
/// drive as sitting at its own critical limit, which then drove the whole
/// dashboard into WARNING.
///
/// Threshold sensors are therefore excluded from every telemetry rule by name,
/// generically, rather than by blacklisting a particular drive model.
/// </summary>
public static class SensorSemantics
{
    /// <summary>
    /// Words that, at the start of a sensor name, mark it as a limit rather
    /// than a reading. Anchored deliberately: vendors use a trailing "Max" for
    /// live maxima ("CPU Core Max" is the busiest core right now), so only a
    /// leading qualifier counts.
    /// </summary>
    private static readonly Regex LeadingQualifier = new(
        @"^(warning|critical|alarm|shutdown|throttle|rated|nominal|maximum|minimum|target|default)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Words that mark a limit wherever they appear in the name.</summary>
    private static readonly Regex LimitWord = new(
        @"\b(limit|threshold|setpoint|set\s?point)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool LooksLikeThreshold(string sensorName) =>
        !string.IsNullOrWhiteSpace(sensorName) &&
        (LeadingQualifier.IsMatch(sensorName) || LimitWord.IsMatch(sensorName));

    public static SensorRole RoleOf(string sensorName) =>
        LooksLikeThreshold(sensorName) ? SensorRole.Threshold : SensorRole.Telemetry;
}
