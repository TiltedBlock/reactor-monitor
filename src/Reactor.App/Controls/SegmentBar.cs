using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reactor.App.Controls;

/// <summary>
/// A discrete instrumentation bar: fixed cells that light up in sequence rather
/// than a continuous fill. Segmentation makes small changes legible at a glance
/// and reads as a physical gauge instead of a progress bar.
/// </summary>
public sealed class SegmentBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SegmentBar, double>(nameof(Value));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SegmentBar, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SegmentBar, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<int> SegmentsProperty =
        AvaloniaProperty.Register<SegmentBar, int>(nameof(Segments), 40);

    public static readonly StyledProperty<double> SegmentGapProperty =
        AvaloniaProperty.Register<SegmentBar, double>(nameof(SegmentGap), 2d);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(Track));

    /// <summary>Draws a hairline tick at this fraction of the scale, if set.</summary>
    public static readonly StyledProperty<double?> MarkerProperty =
        AvaloniaProperty.Register<SegmentBar, double?>(nameof(Marker));

    public static readonly StyledProperty<bool> IsAvailableProperty =
        AvaloniaProperty.Register<SegmentBar, bool>(nameof(IsAvailable), true);

    static SegmentBar()
    {
        AffectsRender<SegmentBar>(
            ValueProperty, MinimumProperty, MaximumProperty, SegmentsProperty,
            SegmentGapProperty, ForegroundProperty, TrackProperty, MarkerProperty,
            IsAvailableProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public double SegmentGap { get => GetValue(SegmentGapProperty); set => SetValue(SegmentGapProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double? Marker { get => GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }
    public bool IsAvailable { get => GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var count = Math.Max(1, Segments);
        var gap = SegmentGap;
        var cell = (width - gap * (count - 1)) / count;
        if (cell <= 0) return;

        var track = Track ?? Brushes.Transparent;
        var lit = Foreground ?? Brushes.White;

        var range = Maximum - Minimum;
        var ratio = IsAvailable && range > 0
            ? Math.Clamp((Value - Minimum) / range, 0, 1)
            : 0;

        // Round up so any non-zero value lights at least one cell - a bar that
        // reads empty at 0.4 % looks broken.
        var litCount = ratio <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(ratio * count));

        for (var i = 0; i < count; i++)
        {
            var x = i * (cell + gap);
            var rect = new Rect(x, 0, cell, height);
            context.FillRectangle(i < litCount ? lit : track, rect);
        }

        if (Marker is { } marker && range > 0)
        {
            var mx = Math.Clamp((marker - Minimum) / range, 0, 1) * width;
            context.FillRectangle(
                Foreground ?? Brushes.White,
                new Rect(Math.Min(mx, width - 1), 0, 1, height));
        }
    }
}
