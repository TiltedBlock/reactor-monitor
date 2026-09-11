using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reactor.App.Controls;

/// <summary>
/// Draws corner brackets around a region. Four short L-shaped ticks give a
/// panel a machined, instrumentation-panel edge without the visual weight of a
/// full border on every element.
/// </summary>
public sealed class BracketFrame : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<BracketFrame, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<BracketFrame, double>(nameof(Thickness), 1d);

    public static readonly StyledProperty<double> TickLengthProperty =
        AvaloniaProperty.Register<BracketFrame, double>(nameof(TickLength), 12d);

    static BracketFrame()
    {
        AffectsRender<BracketFrame>(StrokeProperty, ThicknessProperty, TickLengthProperty);
        // Purely decorative: never intercept a click meant for what is beneath.
        IsHitTestVisibleProperty.OverrideDefaultValue<BracketFrame>(false);
    }

    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public double TickLength { get => GetValue(TickLengthProperty); set => SetValue(TickLengthProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Stroke is not { } stroke) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        var t = Thickness;
        var len = Math.Min(TickLength, Math.Min(w, h) / 2);
        if (w <= 0 || h <= 0 || len <= 0) return;

        // Top-left
        context.FillRectangle(stroke, new Rect(0, 0, len, t));
        context.FillRectangle(stroke, new Rect(0, 0, t, len));
        // Top-right
        context.FillRectangle(stroke, new Rect(w - len, 0, len, t));
        context.FillRectangle(stroke, new Rect(w - t, 0, t, len));
        // Bottom-left
        context.FillRectangle(stroke, new Rect(0, h - t, len, t));
        context.FillRectangle(stroke, new Rect(0, h - len, t, len));
        // Bottom-right
        context.FillRectangle(stroke, new Rect(w - len, h - t, len, t));
        context.FillRectangle(stroke, new Rect(w - t, h - len, t, len));
    }
}
