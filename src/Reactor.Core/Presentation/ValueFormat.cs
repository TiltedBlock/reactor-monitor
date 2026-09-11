using System.Globalization;
using Reactor.Core.Configuration;

namespace Reactor.Core.Presentation;

/// <summary>
/// Number formatting for the readouts. Kept out of the views so the exact
/// string a gauge shows can be asserted in a test.
/// </summary>
public static class ValueFormat
{
    /// <summary>Shown wherever a sensor is unavailable.</summary>
    public const string Missing = "\u2014";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Number(double? value, int decimals = 1) =>
        value is { } v && !double.IsNaN(v) && !double.IsInfinity(v)
            ? v.ToString("F" + decimals, Culture)
            : Missing;

    public static string Percent(double? value, int decimals = 0) => Number(value, decimals);

    public static string Watts(double? value, int decimals = 1) => Number(value, decimals);

    /// <summary>Converts to the configured unit; returns the numeric part only.</summary>
    public static string Temperature(double? celsius, TemperatureUnit unit, int decimals = 1) =>
        Number(ConvertTemperature(celsius, unit), decimals);

    public static double? ConvertTemperature(double? celsius, TemperatureUnit unit) =>
        celsius is { } c ? unit == TemperatureUnit.Fahrenheit ? c * 9.0 / 5.0 + 32.0 : c : null;

    public static string TemperatureUnitSymbol(TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? "\u00b0F" : "\u00b0C";

    /// <summary>MHz in, GHz out once the number gets large enough to deserve it.</summary>
    public static string Clock(double? megahertz, out string unit)
    {
        if (megahertz is not { } mhz) { unit = "GHz"; return Missing; }
        if (mhz >= 1000) { unit = "GHz"; return (mhz / 1000.0).ToString("F2", Culture); }
        unit = "MHz";
        return mhz.ToString("F0", Culture);
    }

    /// <summary>Megabytes in, GB out with one decimal (VRAM, memory).</summary>
    public static string Gigabytes(double? megabytes, int decimals = 1) =>
        megabytes is { } mb ? (mb / 1024.0).ToString("F" + decimals, Culture) : Missing;

    public static string Throughput(double? bytesPerSecond, out string unit)
    {
        if (bytesPerSecond is not { } b) { unit = "MB/s"; return Missing; }

        if (b >= 1024 * 1024) { unit = "MB/s"; return (b / (1024.0 * 1024.0)).ToString("F1", Culture); }
        unit = "KB/s";
        return (b / 1024.0).ToString("F0", Culture);
    }

    /// <summary>Wh below a kilowatt-hour, kWh above it.</summary>
    public static string Energy(double wattHours, out string unit)
    {
        if (wattHours >= 1000) { unit = "kWh"; return (wattHours / 1000.0).ToString("F3", Culture); }
        unit = "Wh";
        return wattHours.ToString("F1", Culture);
    }

    public static string Money(double amount, int decimals = 3) =>
        amount.ToString("F" + decimals, Culture);

    public static string Duration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
}
