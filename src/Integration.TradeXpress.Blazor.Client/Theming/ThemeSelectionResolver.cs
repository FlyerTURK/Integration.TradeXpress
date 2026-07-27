using System;
using System.Linq;
using System.Text.Json;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// ThemeSelection'dan türetilen görsel değerlerin TEK kaynağı — hem ThemeService (canlı tema takası)
/// hem App.razor (anonim SSR ilk boyama: tx.last_theme cookie'sinden) kullanır; mantık iki yerde yaşamaz.
/// </summary>
public static class ThemeSelectionResolver
{
    /// <summary>JSON'dan ThemeSelection okur; boş/bozuk kayıt → null (çağıran Default'a düşer).</summary>
    public static ThemeSelection? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<ThemeSelection>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Bootstrap 5.3 renk modu (&lt;html data-bs-theme&gt;) — koyu yüzeyli temalarda "dark".</summary>
    public static string GetBootstrapColorMode(ThemeSelection selection)
    {
        if (selection.Kind == ThemeKind.Fluent)
        {
            return selection.FluentMode == DevExpress.Blazor.ThemeMode.Dark ? "dark" : "light";
        }
        // Koyu yüzey ile gelen Bootstrap teması.
        return selection.BootstrapName == "Blazing Dark" ? "dark" : "light";
    }

    /// <summary>Temanın vurgu rengi (--tx-theme-primary CSS değişkeni için hex).</summary>
    public static string GetPrimaryColorHex(ThemeSelection selection)
    {
        if (selection.Kind == ThemeKind.Bootstrap)
        {
            return ThemeCatalog.BootstrapThemes.FirstOrDefault(x => x.Name == selection.BootstrapName)?.SwatchColor ?? "#0d6efd";
        }
        if (!string.IsNullOrEmpty(selection.FluentCustomColor))
        {
            return selection.FluentCustomColor;
        }
        return ThemeCatalog.FluentAccents.FirstOrDefault(x => x.Accent == selection.FluentAccent)?.SwatchColor ?? "#0f6cbd";
    }
}
