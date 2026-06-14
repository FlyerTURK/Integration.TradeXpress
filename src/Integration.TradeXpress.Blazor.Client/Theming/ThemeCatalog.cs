using System.Collections.Generic;
using DevExpress.Blazor;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// Display catalog of selectable themes. Holds metadata only — the actual
/// <see cref="ITheme"/> is rebuilt on demand from this metadata via
/// <see cref="ThemeBuilder.Build"/>, so storage stays portable and the
/// catalog can be safely serialized.
/// </summary>
public static class ThemeCatalog
{
    public const string StorageKey = "tx.theme";

    public sealed record BootstrapEntry(string Name, string SwatchColor);

    public sealed record FluentAccentEntry(string Caption, ThemeFluentAccentColor Accent, string SwatchColor);

    public static readonly IReadOnlyList<BootstrapEntry> BootstrapThemes = new[]
    {
        new BootstrapEntry("Blazing Berry", "#5c2d91"),
        new BootstrapEntry("Blazing Dark",  "#46444a"),
        new BootstrapEntry("Office White",  "#fe7109"),
        new BootstrapEntry("Purple",        "#7989ff"),
    };

    /// <summary>
    /// Eleven preset accent colors shipped with <c>Themes.Fluent</c>. Hex swatches
    /// match DevExpress's documented palette for the corresponding accent name.
    /// </summary>
    public static readonly IReadOnlyList<FluentAccentEntry> FluentAccents = new[]
    {
        new FluentAccentEntry("Blue",      ThemeFluentAccentColor.Blue,     "#0f6cbd"),
        new FluentAccentEntry("Cool Blue", ThemeFluentAccentColor.CoolBlue, "#2d7d9a"),
        new FluentAccentEntry("Desert",   ThemeFluentAccentColor.Desert,   "#847545"),
        new FluentAccentEntry("Mint",     ThemeFluentAccentColor.Mint,     "#018574"),
        new FluentAccentEntry("Moss",     ThemeFluentAccentColor.Moss,     "#486860"),
        new FluentAccentEntry("Orchid",   ThemeFluentAccentColor.Orchid,   "#c239b3"),
        new FluentAccentEntry("Purple",   ThemeFluentAccentColor.Purple,   "#5b5fc7"),
        new FluentAccentEntry("Rose",     ThemeFluentAccentColor.Rose,     "#ea005e"),
        new FluentAccentEntry("Rust",     ThemeFluentAccentColor.Rust,     "#da3b01"),
        new FluentAccentEntry("Steel",    ThemeFluentAccentColor.Steel,    "#68768a"),
        new FluentAccentEntry("Storm",    ThemeFluentAccentColor.Storm,    "#4c4a48"),
    };
}
