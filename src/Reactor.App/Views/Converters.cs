using System.Globalization;
using Avalonia.Data.Converters;
using Reactor.App.Theming;

namespace Reactor.App.Views;

/// <summary>The few value conversions the board needs, kept out of the markup.</summary>
public static class Converters
{
    /// <summary>Elevated -> accent, limited -> amber. Used by the privilege chip.</summary>
    public static readonly IValueConverter BoolToAccentOrWarm =
        new FuncValueConverter<bool, object>(elevated => elevated ? Theme.Accent : Theme.Warm);

    public static readonly IValueConverter BoolToPrivilege =
        new FuncValueConverter<bool, string>(elevated => elevated ? "FULL ACCESS" : "USER MODE");
}
