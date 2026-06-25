using System;
using System.Threading.Tasks;
using DevExpress.Blazor;

namespace Integration.TradeXpress.Blazor.Client.Theming;

public interface IThemeService
{
    /// <summary>Current selection metadata (kind, mode, accent, custom color).</summary>
    ThemeSelection CurrentSelection { get; }

    /// <summary>Concrete <see cref="ITheme"/> rebuilt from <see cref="CurrentSelection"/>.</summary>
    ITheme CurrentTheme { get; }

    /// <summary>"light" or "dark" — emitted as <c>data-bs-theme</c> on the document root so Bootstrap 5.3 CSS variables flip in sync with the active theme.</summary>
    string BootstrapColorMode { get; }

    /// <summary>Hex code for the currently selected theme's primary/accent color.</summary>
    string PrimaryColorHex { get; }

    /// <summary>localStorage'dan kaydedilmiş seçimi okuyup uygular (uygulama açılışında bir kez).</summary>
    Task InitializeAsync();

    Task SetBootstrapAsync(string bootstrapName);

    Task SetFluentAsync(ThemeMode mode, ThemeFluentAccentColor accent);

    Task SetFluentCustomAsync(ThemeMode mode, string hexColor);

    event EventHandler? CurrentThemeChanged;
}
