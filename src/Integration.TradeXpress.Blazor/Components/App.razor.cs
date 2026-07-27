using System;
using System.Globalization;
using DevExpress.Blazor;
using Integration.TradeXpress.Blazor.Client.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Integration.TradeXpress.Blazor.Components;

/// <summary>
/// İlk boyama (statik SSR) tema/boyut/dil çözümü: "son oturum açan kullanıcı" ayna cookie'leri
/// (tx.last_theme / tx.last_size — kimlikli akışta ThemeService/SizeModeService yazar) okunur ve
/// DevExpress teması + &lt;html&gt; attribute'ları DOĞRUDAN doğru değerlerle render edilir → login
/// ekranı son kullanıcının temasında FLAŞSIZ açılır (eskiden sabit Blazing Berry Light'tı).
/// Cookie güvenilmez veridir: her parse toleranslıdır, bozuk/yok → varsayılan (Blazing Berry Light).
/// Kültür için ek iş gerekmez — UseAbpRequestLocalization cookie'yi SSR'dan önce uygulamıştır.
/// </summary>
public partial class App
{
    [CascadingParameter] public HttpContext? HttpContext { get; set; }

    private ITheme _initialTheme = ThemeBuilder.Build(ThemeSelection.Default);
    private string _colorMode = "light";
    private string _sizeAttr = nameof(SizeMode.Medium);
    private string _primaryHex = "#dc3545";
    private string _lang = "tr";

    protected override void OnInitialized()
    {
        var selection = ReadThemeSelectionFromCookie();
        _initialTheme = ThemeBuilder.Build(selection);
        _colorMode    = ThemeSelectionResolver.GetBootstrapColorMode(selection);
        _primaryHex   = ThemeSelectionResolver.GetPrimaryColorHex(selection);
        _sizeAttr     = ReadSizeModeFromCookie();
        _lang         = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }

    private ThemeSelection ReadThemeSelectionFromCookie()
    {
        try
        {
            // Request.Cookies zaten URL-decode edilmiş değer döner (ASP.NET Core) — ikinci bir UrlDecode
            // burada '+' karakterini boşluğa çevirip literal %XX dizilerini yeniden çözerek JSON'u sessizce
            // bozabilirdi (bugünkü katalog adlarında '+'/'%' yok, o yüzden fark etmiyordu — kanıtlandı).
            var raw = HttpContext?.Request.Cookies[ThemeService.LastThemeCookieName];
            if (string.IsNullOrEmpty(raw))
            {
                return ThemeSelection.Default;
            }
            return ThemeSelectionResolver.TryParse(raw) ?? ThemeSelection.Default;
        }
        catch
        {
            return ThemeSelection.Default;
        }
    }

    private string ReadSizeModeFromCookie()
    {
        try
        {
            var raw = HttpContext?.Request.Cookies[SizeModeService.LastSizeCookieName];
            return Enum.TryParse<SizeMode>(raw, ignoreCase: true, out var parsed)
                ? parsed.ToString()
                : nameof(SizeMode.Medium);
        }
        catch
        {
            return nameof(SizeMode.Medium);
        }
    }
}
