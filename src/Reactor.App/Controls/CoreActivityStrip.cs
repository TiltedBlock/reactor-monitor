using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reactor.App.Controls;

/// <summary>
/// One narrow vertical bar per logical core. Deliberately understated: it is
/// texture that tells you how work is spread across the package, not a readout
/// anyone is meant to read a number off.
/// </summary>
public sealed class CoreActivityStrip : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<CoreActivityStrip, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<CoreActivityStrip, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<CoreActivityStrip, IBrush?>(nameof(Track));

    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<CoreActivityStrip, int>(nameof(Rows), 8);

    static CoreActivityStrip()
    {
        AffectsRender<CoreActivityStrip>(ValuesProperty, ForegroundProperty, TrackProperty, RowsProperty);
    }

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public int Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is null || values.Count == 0) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var columns = values.Count;
        const double columnGap = 3;
        var columnWidth = (w - columnGap * (columns - 1)) / columns;
        if (columnWidth <= 0) return;

        var rows = Math.Max(1, Rows);
        const double rowGap = 2;
        var rowHeight = (h - rowGap * (rows - 1)) / rows;
        if (rowHeight <= 0) return;

        var lit = Foreground ?? Brushes.White;
        var track = Track ?? Brushes.Transparent;

        for (var c = 0; c < columns; c++)
        {
            var ratio = Math.Clamp(values[c] / 100.0, 0, 1);
            var litRows = ratio <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(ratio * rows));
            var x = c * (columnWidth + columnGap);

            for (var r = 0; r < rows; r++)
            {
                // Row 0 is the bottom of the column.
                var y = h - (r + 1) * rowHeight - r * rowGap;
                context.FillRectangle(r < litRows ? lit : track, new Rect(x, y, columnWidth, rowHeight));
            }
        }
    }
}
