using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Reactor.App.Controls;

/// <summary>
/// A framed section of the board: designation on the left, hardware identity on
/// the right, a rule underneath, then content. Using one panel type everywhere
/// is what makes the dashboard read as a single console rather than a grid of
/// unrelated cards.
/// </summary>
public sealed class ModulePanel : ContentControl
{
    public static readonly StyledProperty<string?> DesignationProperty =
        AvaloniaProperty.Register<ModulePanel, string?>(nameof(Designation));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<ModulePanel, string?>(nameof(Subtitle));

    /// <summary>Small status word shown at the far right of the header rule.</summary>
    public static readonly StyledProperty<string?> BadgeProperty =
        AvaloniaProperty.Register<ModulePanel, string?>(nameof(Badge));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<ModulePanel, IBrush?>(nameof(Accent));

    public string? Designation { get => GetValue(DesignationProperty); set => SetValue(DesignationProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
}
