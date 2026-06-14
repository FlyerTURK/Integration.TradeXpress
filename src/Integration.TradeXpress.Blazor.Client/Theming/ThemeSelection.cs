using DevExpress.Blazor;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// Persistable shape of the user's theme choice. We don't persist <see cref="ITheme"/>
/// directly; instead we store the metadata required to rebuild it deterministically with
/// <see cref="Themes.BlazingBerry"/>, <see cref="Themes.Fluent"/>, etc.
/// </summary>
public sealed record ThemeSelection(
    ThemeKind Kind,
    string? BootstrapName,
    ThemeMode FluentMode,
    ThemeFluentAccentColor FluentAccent,
    string? FluentCustomColor)
{
    public static readonly ThemeSelection Default = new(
        Kind: ThemeKind.Bootstrap,
        BootstrapName: "Blazing Berry",
        FluentMode: ThemeMode.Light,
        FluentAccent: ThemeFluentAccentColor.Blue,
        FluentCustomColor: null);
}
