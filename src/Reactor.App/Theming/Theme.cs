using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Reactor.Core.Telemetry;

namespace Reactor.App.Theming;

/// <summary>How hot / how alarming a single readout currently is.</summary>
public enum Severity
{
    Unavailable,
    Normal,
    Elevated,
    Critical
}

/// <summary>
/// Typed access to the brushes declared in Palette.axaml, so view models and
/// custom controls do not carry hardcoded colour literals.
/// </summary>
public static class Theme
{
    private static readonly Dictionary<string, IBrush> Cache = new();

    public static IBrush Brush(string key)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var app = Application.Current;
        if (app is not null && app.TryFindResource(key, out var value) && value is IBrush brush)
        {
            Cache[key] = brush;
            return brush;
        }

        return Brushes.Magenta; // deliberately loud: a missing key should be seen
    }

    public static IBrush Accent => Brush("AccentBrush");
    public static IBrush Good => Brush("GoodBrush");
    public static IBrush Warm => Brush("WarmBrush");
    public static IBrush Hot => Brush("HotBrush");
    public static IBrush Alert => Brush("AlertBrush");
    public static IBrush TextBright => Brush("TextBrightBrush");
    public static IBrush TextMid => Brush("TextMidBrush");
    public static IBrush TextDim => Brush("TextDimBrush");
    public static IBrush TextFaint => Brush("TextFaintBrush");

    public static IBrush ForSeverity(Severity severity) => severity switch
    {
        Severity.Critical => Alert,
        Severity.Elevated => Warm,
        Severity.Normal => Accent,
        _ => TextFaint
    };

    public static IBrush WashForSeverity(Severity severity) => severity switch
    {
        Severity.Critical => Brush("AlertWashBrush"),
        Severity.Elevated => Brush("WarmWashBrush"),
        _ => Brush("AccentWashBrush")
    };

    public static IBrush ForState(SystemState state) => state switch
    {
        SystemState.Warning => Alert,
        SystemState.ThermalLoad => Hot,
        SystemState.PowerLimited => Warm,
        SystemState.HighLoad => Warm,
        SystemState.Active => Accent,
        SystemState.Nominal => Good,
        _ => TextFaint
    };

    /// <summary>Classifies a value against a two-step threshold pair.</summary>
    public static Severity Classify(double? value, double elevated, double critical) => value switch
    {
        null => Severity.Unavailable,
        var v when v >= critical => Severity.Critical,
        var v when v >= elevated => Severity.Elevated,
        _ => Severity.Normal
    };
}
