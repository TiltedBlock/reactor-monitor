using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reactor.App.Controls;

/// <summary>
/// A compact two-series history strip: a filled area for the primary channel
/// and a thin line for the secondary one, over a fixed reference grid.
///
/// Each series carries its own scale, because the two channels a module graphs
/// (load in percent, temperature in degrees) share an axis but not a range.
/// Gaps in the data break the line instead of being drawn as a drop to zero.
/// </summary>
public sealed class TelemetryGraph : Control
{
    public static readonly StyledProperty<double?[]?> PrimaryProperty =
        AvaloniaProperty.Register<TelemetryGraph, double?[]?>(nameof(Primary));

    public static readonly StyledProperty<double?[]?> SecondaryProperty =
        AvaloniaProperty.Register<TelemetryGraph, double?[]?>(nameof(Secondary));

    public static readonly StyledProperty<double> PrimaryMinimumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(PrimaryMinimum));

    public static readonly StyledProperty<double> PrimaryMaximumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(PrimaryMaximum), 100d);

    public static readonly StyledProperty<double> SecondaryMinimumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(SecondaryMinimum), 20d);

    public static readonly StyledProperty<double> SecondaryMaximumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(SecondaryMaximum), 100d);

    /// <summary>Grows the primary scale to fit the data (used for power).</summary>
    public static readonly StyledProperty<bool> AutoScalePrimaryProperty =
        AvaloniaProperty.Register<TelemetryGraph, bool>(nameof(AutoScalePrimary));

    public static readonly StyledProperty<IBrush?> PrimaryStrokeProperty =
        AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(PrimaryStroke));

    public static readonly StyledProperty<IBrush?> PrimaryFillProperty =
        AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(PrimaryFill));

    public static readonly StyledProperty<IBrush?> SecondaryStrokeProperty =
        AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(SecondaryStroke));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<int> GridRowsProperty =
        AvaloniaProperty.Register<TelemetryGraph, int>(nameof(GridRows), 4);

    public static readonly StyledProperty<int> GridColumnsProperty =
        AvaloniaProperty.Register<TelemetryGraph, int>(nameof(GridColumns), 8);

    /// <summary>Number of slots on the time axis; keeps the trace advancing at a
    /// constant speed while the buffer is still filling up.</summary>
    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<TelemetryGraph, int>(nameof(Capacity), 120);

    /// <summary>Reserves a right-hand gutter and prints the primary scale in it.</summary>
    public static readonly StyledProperty<bool> ShowScaleProperty =
        AvaloniaProperty.Register<TelemetryGraph, bool>(nameof(ShowScale), true);

    public static readonly StyledProperty<string?> ScaleUnitProperty =
        AvaloniaProperty.Register<TelemetryGraph, string?>(nameof(ScaleUnit));

    public static readonly StyledProperty<IBrush?> ScaleBrushProperty =
        AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(ScaleBrush));

    public static readonly StyledProperty<FontFamily> ScaleFontFamilyProperty =
        AvaloniaProperty.Register<TelemetryGraph, FontFamily>(nameof(ScaleFontFamily), FontFamily.Default);

    static TelemetryGraph()
    {
        AffectsRender<TelemetryGraph>(
            PrimaryProperty, SecondaryProperty, PrimaryMinimumProperty, PrimaryMaximumProperty,
            SecondaryMinimumProperty, SecondaryMaximumProperty, AutoScalePrimaryProperty,
            PrimaryStrokeProperty, PrimaryFillProperty, SecondaryStrokeProperty,
            GridBrushProperty, GridRowsProperty, GridColumnsProperty, CapacityProperty,
            ShowScaleProperty, ScaleUnitProperty, ScaleBrushProperty, ScaleFontFamilyProperty);
    }

    public double?[]? Primary { get => GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public double?[]? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public double PrimaryMinimum { get => GetValue(PrimaryMinimumProperty); set => SetValue(PrimaryMinimumProperty, value); }
    public double PrimaryMaximum { get => GetValue(PrimaryMaximumProperty); set => SetValue(PrimaryMaximumProperty, value); }
    public double SecondaryMinimum { get => GetValue(SecondaryMinimumProperty); set => SetValue(SecondaryMinimumProperty, value); }
    public double SecondaryMaximum { get => GetValue(SecondaryMaximumProperty); set => SetValue(SecondaryMaximumProperty, value); }
    public bool AutoScalePrimary { get => GetValue(AutoScalePrimaryProperty); set => SetValue(AutoScalePrimaryProperty, value); }
    public IBrush? PrimaryStroke { get => GetValue(PrimaryStrokeProperty); set => SetValue(PrimaryStrokeProperty, value); }
    public IBrush? PrimaryFill { get => GetValue(PrimaryFillProperty); set => SetValue(PrimaryFillProperty, value); }
    public IBrush? SecondaryStroke { get => GetValue(SecondaryStrokeProperty); set => SetValue(SecondaryStrokeProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public int GridRows { get => GetValue(GridRowsProperty); set => SetValue(GridRowsProperty, value); }
    public int GridColumns { get => GetValue(GridColumnsProperty); set => SetValue(GridColumnsProperty, value); }
    public int Capacity { get => GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    public bool ShowScale { get => GetValue(ShowScaleProperty); set => SetValue(ShowScaleProperty, value); }
    public string? ScaleUnit { get => GetValue(ScaleUnitProperty); set => SetValue(ScaleUnitProperty, value); }
    public IBrush? ScaleBrush { get => GetValue(ScaleBrushProperty); set => SetValue(ScaleBrushProperty, value); }
    public FontFamily ScaleFontFamily { get => GetValue(ScaleFontFamilyProperty); set => SetValue(ScaleFontFamilyProperty, value); }

    private const double ScaleGutter = 40;
    private const double ScaleFontSize = 8.5;

    public override void Render(DrawingContext context)
    {
        var h = Bounds.Height;
        var gutter = ShowScale && ScaleBrush is not null ? ScaleGutter : 0;
        var w = Bounds.Width - gutter;
        if (w <= 1 || h <= 1) return;

        DrawGrid(context, w, h);

        var slots = Math.Max(2, Capacity);

        var primaryMax = PrimaryMaximum;
        if (AutoScalePrimary && Primary is { Length: > 0 })
        {
            var peak = 0.0;
            foreach (var v in Primary) if (v is { } d && d > peak) peak = d;
            // Round the ceiling up to a readable step so the trace does not
            // rescale on every sample.
            if (peak > 0) primaryMax = Math.Max(PrimaryMaximum, NiceCeiling(peak * 1.15));
        }

        if (Secondary is { Length: > 1 } && SecondaryStroke is { } secondStroke)
        {
            // Drawn first so the primary area never hides behind it.
            var geometry = BuildGeometry(Secondary, slots, w, h, SecondaryMinimum, SecondaryMaximum, out _, out _);
            context.DrawGeometry(null, new Pen(secondStroke, 1.1), geometry);
        }

        if (Primary is { Length: > 1 })
        {
            var geometry = BuildGeometry(Primary, slots, w, h, PrimaryMinimum, primaryMax, out var fill, out var last);
            if (PrimaryFill is { } fillBrush && fill is not null)
                context.DrawGeometry(fillBrush, null, fill);
            if (PrimaryStroke is { } stroke)
            {
                context.DrawGeometry(null, new Pen(stroke, 1.4), geometry);

                // A small square on the newest sample: tells you at a glance
                // where "now" is when the trace is flat.
                if (last is { } point)
                    context.FillRectangle(stroke,
                        new Rect(Math.Min(point.X, w - 3) - 1.5, point.Y - 1.5, 3, 3));
            }
        }

        if (gutter > 0) DrawScale(context, w, h, gutter, primaryMax);
    }

    /// <summary>Prints the primary axis bounds in the right-hand gutter.</summary>
    private void DrawScale(DrawingContext context, double w, double h, double gutter, double max)
    {
        if (ScaleBrush is not { } brush) return;

        var typeface = new Typeface(ScaleFontFamily);
        var unit = string.IsNullOrEmpty(ScaleUnit) ? "" : " " + ScaleUnit;

        void Print(string text, double top)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, ScaleFontSize, brush);
            context.DrawText(formatted, new Point(w + gutter - 4 - formatted.Width, top));
        }

        Print(FormatScale(max) + unit, 2);
        Print(FormatScale(PrimaryMinimum) + unit, h - ScaleFontSize - 5);

        context.DrawLine(new Pen(brush, 1) { }, new Point(w + 0.5, 0), new Point(w + 0.5, h));
    }

    private static string FormatScale(double value) =>
        Math.Abs(value) >= 1000
            ? (value / 1000).ToString("0.#k", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);

    private void DrawGrid(DrawingContext context, double w, double h)
    {
        if (GridBrush is not { } brush) return;
        var pen = new Pen(brush, 1);

        for (var r = 1; r < GridRows; r++)
        {
            var y = Math.Round(h * r / GridRows) + 0.5;
            context.DrawLine(pen, new Point(0, y), new Point(w, y));
        }

        for (var c = 1; c < GridColumns; c++)
        {
            var x = Math.Round(w * c / GridColumns) + 0.5;
            context.DrawLine(pen, new Point(x, 0), new Point(x, h));
        }
    }

    /// <summary>
    /// Builds the trace, right-aligned so the newest sample sits at the right
    /// edge. Returns a closed area geometry too, for the filled series.
    /// </summary>
    private static StreamGeometry BuildGeometry(
        double?[] values, int slots, double w, double h, double min, double max,
        out StreamGeometry? area, out Point? lastPoint)
    {
        var line = new StreamGeometry();
        var fill = new StreamGeometry();

        var range = max - min;
        if (range <= 0) range = 1;

        var step = w / (slots - 1);
        var offset = slots - values.Length; // newest sample pinned to the right

        using var lineCtx = line.Open();
        using var fillCtx = fill.Open();

        var lineOpen = false;
        var fillOpen = false;
        var lastX = 0.0;
        Point? newest = null;

        for (var i = 0; i < values.Length; i++)
        {
            var x = (offset + i) * step;

            if (values[i] is not { } value)
            {
                // Break both traces across the gap.
                if (lineOpen) { lineCtx.EndFigure(false); lineOpen = false; }
                if (fillOpen) { fillCtx.LineTo(new Point(lastX, h)); fillCtx.EndFigure(true); fillOpen = false; }
                continue;
            }

            var y = h - Math.Clamp((value - min) / range, 0, 1) * h;
            var point = new Point(x, y);

            if (!lineOpen) { lineCtx.BeginFigure(point, false); lineOpen = true; }
            else lineCtx.LineTo(point);

            if (!fillOpen) { fillCtx.BeginFigure(new Point(x, h), true); fillCtx.LineTo(point); fillOpen = true; }
            else fillCtx.LineTo(point);

            lastX = x;
            newest = point;
        }

        if (lineOpen) lineCtx.EndFigure(false);
        if (fillOpen) { fillCtx.LineTo(new Point(lastX, h)); fillCtx.EndFigure(true); }

        area = fill;
        lastPoint = newest;
        return line;
    }

    /// <summary>Rounds up to 1/2/5 x 10^n so the auto-scaled axis stays stable.</summary>
    private static double NiceCeiling(double value)
    {
        if (value <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return step * magnitude;
    }
}
