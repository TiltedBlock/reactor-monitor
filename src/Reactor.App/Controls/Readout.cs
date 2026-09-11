using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Reactor.App.Controls;

/// <summary>
/// The single unit of information on the board: a small caption, a large
/// number, a unit, and an optional footnote. Every value on the dashboard uses
/// this control, which is what keeps the typography and the numeric alignment
/// consistent across modules.
/// </summary>
public sealed class Readout : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<Readout, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<Readout, string?>(nameof(Value));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<Readout, string?>(nameof(Unit));

    /// <summary>Small footnote under the value: "EST", "PEAK 82.4", etc.</summary>
    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<Readout, string?>(nameof(Note));

    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<Readout, IBrush?>(nameof(ValueBrush));

    public static readonly StyledProperty<bool> IsAvailableProperty =
        AvaloniaProperty.Register<Readout, bool>(nameof(IsAvailable), true);

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string? Note { get => GetValue(NoteProperty); set => SetValue(NoteProperty, value); }
    public IBrush? ValueBrush { get => GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
    public bool IsAvailable { get => GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }
}
