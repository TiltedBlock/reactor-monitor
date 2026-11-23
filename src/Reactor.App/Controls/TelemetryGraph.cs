using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Reactor.Core.Presentation;
using Reactor.Core.Telemetry;

namespace Reactor.App.Controls;

public sealed class TelemetryGraph : Control
{
    public static readonly StyledProperty<GraphTimeline?> TimelineProperty =
        AvaloniaProperty.Register<TelemetryGraph, GraphTimeline?>(nameof(Timeline));
    public static readonly StyledProperty<HistoryChannel> PrimaryChannelProperty =
        AvaloniaProperty.Register<TelemetryGraph, HistoryChannel>(nameof(PrimaryChannel));
    public static readonly StyledProperty<HistoryChannel?> SecondaryChannelProperty =
        AvaloniaProperty.Register<TelemetryGraph, HistoryChannel?>(nameof(SecondaryChannel));
    public static readonly StyledProperty<string> SecondaryUnitProperty =
        AvaloniaProperty.Register<TelemetryGraph, string>(nameof(SecondaryUnit), "°C");
    public static readonly StyledProperty<double> PrimaryMinimumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(PrimaryMinimum));
    public static readonly StyledProperty<double> PrimaryMaximumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(PrimaryMaximum), 100);
    public static readonly StyledProperty<double> SecondaryMinimumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(SecondaryMinimum), 20);
    public static readonly StyledProperty<double> SecondaryMaximumProperty =
        AvaloniaProperty.Register<TelemetryGraph, double>(nameof(SecondaryMaximum), 100);
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
    public static readonly StyledProperty<string?> ScaleUnitProperty =
        AvaloniaProperty.Register<TelemetryGraph, string?>(nameof(ScaleUnit));
    public static readonly StyledProperty<IBrush?> ScaleBrushProperty =
        AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(ScaleBrush));
    public static readonly StyledProperty<FontFamily> ScaleFontFamilyProperty =
        AvaloniaProperty.Register<TelemetryGraph, FontFamily>(nameof(ScaleFontFamily), FontFamily.Default);

    public GraphTimeline? Timeline { get => GetValue(TimelineProperty); set => SetValue(TimelineProperty, value); }
    public HistoryChannel PrimaryChannel { get => GetValue(PrimaryChannelProperty); set => SetValue(PrimaryChannelProperty, value); }
    public HistoryChannel? SecondaryChannel { get => GetValue(SecondaryChannelProperty); set => SetValue(SecondaryChannelProperty, value); }
    public string SecondaryUnit { get => GetValue(SecondaryUnitProperty); set => SetValue(SecondaryUnitProperty, value); }
    public double PrimaryMinimum { get => GetValue(PrimaryMinimumProperty); set => SetValue(PrimaryMinimumProperty, value); }
    public double PrimaryMaximum { get => GetValue(PrimaryMaximumProperty); set => SetValue(PrimaryMaximumProperty, value); }
    public double SecondaryMinimum { get => GetValue(SecondaryMinimumProperty); set => SetValue(SecondaryMinimumProperty, value); }
    public double SecondaryMaximum { get => GetValue(SecondaryMaximumProperty); set => SetValue(SecondaryMaximumProperty, value); }
    public bool AutoScalePrimary { get => GetValue(AutoScalePrimaryProperty); set => SetValue(AutoScalePrimaryProperty, value); }
    public IBrush? PrimaryStroke { get => GetValue(PrimaryStrokeProperty); set => SetValue(PrimaryStrokeProperty, value); }
    public IBrush? PrimaryFill { get => GetValue(PrimaryFillProperty); set => SetValue(PrimaryFillProperty, value); }
    public IBrush? SecondaryStroke { get => GetValue(SecondaryStrokeProperty); set => SetValue(SecondaryStrokeProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public string? ScaleUnit { get => GetValue(ScaleUnitProperty); set => SetValue(ScaleUnitProperty, value); }
    public IBrush? ScaleBrush { get => GetValue(ScaleBrushProperty); set => SetValue(ScaleBrushProperty, value); }
    public FontFamily ScaleFontFamily { get => GetValue(ScaleFontFamilyProperty); set => SetValue(ScaleFontFamilyProperty, value); }

    public static readonly StyledProperty<int> GridRowsProperty =
        AvaloniaProperty.Register<TelemetryGraph, int>(nameof(GridRows), 4);
    public static readonly StyledProperty<int> GridColumnsProperty =
        AvaloniaProperty.Register<TelemetryGraph, int>(nameof(GridColumns), 8);
    public int GridRows { get => GetValue(GridRowsProperty); set => SetValue(GridRowsProperty, value); }
    public int GridColumns { get => GetValue(GridColumnsProperty); set => SetValue(GridColumnsProperty, value); }

    private Point? _hover;
    private double ScaleWidth => SecondaryChannel.HasValue ? 94 : 44;

    static TelemetryGraph() => AffectsRender<TelemetryGraph>(
        TimelineProperty, PrimaryChannelProperty, SecondaryChannelProperty, SecondaryUnitProperty,
        PrimaryMinimumProperty, PrimaryMaximumProperty, SecondaryMinimumProperty, SecondaryMaximumProperty,
        AutoScalePrimaryProperty, PrimaryStrokeProperty, PrimaryFillProperty, SecondaryStrokeProperty,
        GridBrushProperty, GridRowsProperty, GridColumnsProperty, ScaleUnitProperty, ScaleBrushProperty, ScaleFontFamilyProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TimelineProperty) return;
        if (change.GetOldValue<GraphTimeline?>() is { } old) old.Changed -= InvalidateVisual;
        if (change.GetNewValue<GraphTimeline?>() is { } timeline) timeline.Changed += InvalidateVisual;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Timeline is not { } timeline || e.Delta.Y == 0) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? timeline.WindowSeconds : timeline.WindowSeconds / 5;
        timeline.Scroll(-e.Delta.Y * step);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount != 2 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Timeline?.GoLive();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        double height = Bounds.Height;
        double width = Bounds.Width - ScaleWidth;
        if (height <= 1 || width <= 1 || Timeline is not { } timeline) return;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (GridBrush is { } grid)
        {
            var pen = new Pen(grid);
            for (int i = 1; i < GridColumns; i++)
            {
                double x = width * i / GridColumns;
                context.DrawLine(pen, new Point(x, 0), new Point(x, height));
            }
            for (int i = 1; i < GridRows; i++)
            {
                double y = height * i / GridRows;
                context.DrawLine(pen, new Point(0, y), new Point(width, y));
            }
        }

        var samples = timeline.VisibleSamples().ToArray();
        double start = timeline.End - timeline.WindowSeconds;
        double X(double seconds) => (seconds - start) / timeline.WindowSeconds * width;
        double max = PrimaryMaximum;
        if (AutoScalePrimary && samples.Length > 0)
            max = Math.Max(max, NiceCeiling((samples.Max(s => s.Read(PrimaryChannel)) ?? 0) * 1.15));

        if (SecondaryChannel is { } secondary && SecondaryStroke is { } warm)
            DrawTrace(context, samples, secondary, X, width, height, SecondaryMinimum, SecondaryMaximum, warm, null);
        if (PrimaryStroke is { } primary)
            DrawTrace(context, samples, PrimaryChannel, X, width, height, PrimaryMinimum, max, primary, PrimaryFill);

        if (ScaleBrush is not { } ink) return;
        DrawAxis(context, width, height, PrimaryMinimum, max, ScaleUnit, ink);
        if (SecondaryChannel.HasValue && SecondaryStroke is { } secondaryInk)
            DrawAxis(context, width + 44, height, DisplayTemp(SecondaryMinimum),
                DisplayTemp(SecondaryMaximum), SecondaryUnit, secondaryInk);

        string mode = timeline.IsLive ? $"LIVE · {timeline.WindowSeconds:0} S" :
            $"REVIEW · {Duration(timeline.Latest - timeline.End)} BACK";
        DrawBadge(context, mode, 6, 4, width, ink);

        if (_hover is not { } pointer || pointer.X < 0 || pointer.X > width || height < 30) return;
        double seconds = start + pointer.X / width * timeline.WindowSeconds;
        if (seconds < 0) return;
        var nearest = samples.Length > 0 ? samples.MinBy(s => Math.Abs(s.Seconds - seconds)) : default;
        bool found = samples.Length > 0 && Math.Abs(nearest.Seconds - seconds) <= timeline.SampleTolerance;
        double cursor = found ? X(nearest.Seconds) : pointer.X;
        context.DrawLine(new Pen(ink), new Point(cursor, 0), new Point(cursor, height));

        string detail = Duration(found ? nearest.Seconds : seconds);
        if (found)
        {
            detail += $"   {ValueFormat.Number(nearest.Read(PrimaryChannel))} {ScaleUnit}";
            if (SecondaryChannel is { } temperature)
            {
                var value = nearest.Read(temperature);
                detail += $"   {ValueFormat.Number(value.HasValue ? DisplayTemp(value.Value) : null)} {SecondaryUnit}";
            }
        }
        else detail += "   NO SAMPLE";
        DrawBadge(context, detail, cursor + 8, height - 18, width, ink);
    }

    private static string Duration(double seconds) => ValueFormat.Duration(TimeSpan.FromSeconds(seconds));
    private double DisplayTemp(double celsius) => SecondaryUnit == "°F" ? celsius * 1.8 + 32 : celsius;

    private FormattedText Text(string text, IBrush ink, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(ScaleFontFamily), size, ink);

    private void DrawBadge(DrawingContext context, string value, double x, double y, double width, IBrush ink)
    {
        var text = Text(value, ink, 10);
        x = Math.Clamp(x, 4, Math.Max(4, width - text.Width - 4));
        context.FillRectangle(Brushes.Black, new Rect(x - 3, y, text.Width + 6, 14));
        context.DrawText(text, new Point(x, y));
    }

    private void DrawAxis(DrawingContext context, double x, double height, double min, double max, string? unit, IBrush ink)
    {
        context.DrawLine(new Pen(GridBrush ?? ink), new Point(x, 0), new Point(x, height));
        context.DrawText(Text($"{max:0.#} {unit}", ink, 9), new Point(x + 4, 2));
        context.DrawText(Text($"{min:0.#} {unit}", ink, 9), new Point(x + 4, height - 14));
    }

    private static void DrawTrace(DrawingContext context, GraphSample[] samples, HistoryChannel channel,
        Func<double, double> xFor, double width, double height, double min, double max, IBrush stroke, IBrush? fill)
    {
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        Point? latest = null;
        using (var trace = line.Open())
        using (var shape = area.Open())
        {
            Point? last = null;
            foreach (var sample in samples)
            {
                double? value = sample.Read(channel);
                if (sample.BreakBefore || value is null) EndSegment();
                latest = null;
                if (value is not { } v) continue;

                double x = xFor(sample.Seconds);
                var point = new Point(x, height - Math.Clamp((v - min) / Math.Max(1, max - min), 0, 1) * height);
                if (last is null)
                {
                    trace.BeginFigure(point, false);
                    shape.BeginFigure(new Point(x, height), true);
                }
                else trace.LineTo(point);
                shape.LineTo(point);
                last = latest = point;
            }
            EndSegment();

            void EndSegment()
            {
                if (last is not { } point) return;
                trace.EndFigure(false);
                shape.LineTo(new Point(point.X, height));
                shape.EndFigure(true);
                last = null;
            }
        }
        if (fill is not null) context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(stroke, 1.35), line);
        if (latest is { } marker && width >= 3 && height >= 3)
            context.FillRectangle(stroke, new Rect(
                Math.Clamp(marker.X - 1.5, 0, width - 3), Math.Clamp(marker.Y - 1.5, 0, height - 3), 3, 3));
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 0) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double normalized = value / magnitude;
        return (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
    }
}
