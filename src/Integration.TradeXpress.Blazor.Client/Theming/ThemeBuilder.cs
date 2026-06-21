using DevExpress.Blazor;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// Turns a <see cref="ThemeSelection"/> into a concrete DevExpress <see cref="ITheme"/>
/// via <see cref="Themes"/>'s built-in clones.
/// </summary>
public static class ThemeBuilder
{
    public static ITheme Build(ThemeSelection selection)
    {
        if (selection.Kind == ThemeKind.Bootstrap)
        {
            return BuildBootstrap(selection.BootstrapName ?? "Blazing Berry");
        }

        return BuildFluent(selection);
    }

    private static ITheme BuildBootstrap(string name) => name switch
    {
        "Office White" => Themes.OfficeWhite,
        "Blazing Dark" => Themes.BlazingDark,
        "Purple"       => Themes.Purple,
        _              => Themes.BlazingBerry,
    };

    private static ITheme BuildFluent(ThemeSelection selection)
    {
        return Themes.Fluent.Clone(properties =>
        {
            var modeName = selection.FluentMode == ThemeMode.Dark ? "Dark" : "Light";
            properties.Mode = selection.FluentMode;

            if (!string.IsNullOrWhiteSpace(selection.FluentCustomColor))
            {
                properties.SetCustomAccentColor(selection.FluentCustomColor);
                properties.Name = $"Fluent {modeName} Custom {selection.FluentCustomColor}";
            }
            else
            {
                properties.AccentColor = selection.FluentAccent;
                properties.Name = $"Fluent {modeName} {selection.FluentAccent}";
            }
        });
    }
}
