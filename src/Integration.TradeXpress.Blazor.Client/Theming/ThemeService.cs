using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Microsoft.JSInterop;
using Integration.TradeXpress.Settings;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// DevExpress <see cref="IThemeChangeService"/> üzerine ince katman. Seçimin TEK doğruluk kaynağı
/// SUNUCU per-user ayarıdır (GetThemeAsync/SetThemeAsync — ABP SettingManager); tarayıcıdaki
/// <c>tx.last_theme</c> cookie'si yalnız "son oturum açan kullanıcı" AYNASIDIR: kimlikli akışta
/// YAZILIR (her uygulama/değişimde), anonim login SSR'ı (App.razor) onu yalnız OKUR — böylece giriş
/// ekranı son kullanıcının temasıyla flaşsız açılır. Açılışta <see cref="InitializeAsync"/> sunucu
/// kaydını okuyup uygular.
/// </summary>
public sealed class ThemeService : IThemeService
{
    /// <summary>Anonim login SSR'ının okuduğu tema aynası cookie'si (encode'lu ThemeSelection JSON).</summary>
    public const string LastThemeCookieName = "tx.last_theme";
    private readonly IJSRuntime _js;
    private readonly IUserUiSettingAppService _uiSettings;
    private readonly IThemeChangeService _devExpressThemeService;
    private IJSObjectReference? _module;
    private ThemeSelection _selection = ThemeSelection.Default;
    private ITheme _currentTheme;

    public ThemeService(IJSRuntime js, IUserUiSettingAppService uiSettings, IThemeChangeService devExpressThemeService)
    {
        _js = js;
        _uiSettings = uiSettings;
        _devExpressThemeService = devExpressThemeService;
        _currentTheme = ThemeBuilder.Build(_selection);
    }

    public ThemeSelection CurrentSelection => _selection;

    public ITheme CurrentTheme => _currentTheme;

    public string BootstrapColorMode => ThemeSelectionResolver.GetBootstrapColorMode(_selection);

    public string PrimaryColorHex => ThemeSelectionResolver.GetPrimaryColorHex(_selection);

    public event EventHandler? CurrentThemeChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            string? json = null;
            try { json = await _uiSettings.GetThemeAsync(); } catch { /* Ignore API error if backend not updated */ }
            var saved = ThemeSelectionResolver.TryParse(json);
            if (saved is not null)
            {
                await ApplyAsync(saved, persist: false);
            }
            else
            {
                // Kayıt yoksa varsayılanın data-bs-theme'i de doğru yazılsın; ayna cookie'si de VARSAYILANA
                // çekilir — bu kullanıcının tema tercihi yokken login'de önceki kullanıcının teması kalmasın.
                await module.InvokeVoidAsync("setBootstrapColorMode", BootstrapColorMode);
                await module.InvokeVoidAsync("setPrimaryColorHex", PrimaryColorHex);
                await WriteLastThemeCookieAsync(module, _selection);
            }
        }
        catch (JSDisconnectedException)    { }
        catch (TaskCanceledException)      { }
        catch (OperationCanceledException) { }
    }

    public Task SetBootstrapAsync(string bootstrapName)
        => ApplyAsync(_selection with
        {
            Kind = ThemeKind.Bootstrap,
            BootstrapName = bootstrapName,
            FluentCustomColor = null
        });

    public Task SetFluentAsync(ThemeMode mode, ThemeFluentAccentColor accent)
        => ApplyAsync(_selection with
        {
            Kind = ThemeKind.Fluent,
            FluentMode = mode,
            FluentAccent = accent,
            FluentCustomColor = null
        });

    public Task SetFluentCustomAsync(ThemeMode mode, string hexColor)
        => ApplyAsync(_selection with
        {
            Kind = ThemeKind.Fluent,
            FluentMode = mode,
            FluentCustomColor = hexColor
        });

    private async Task ApplyAsync(ThemeSelection next, bool persist = true)
    {
        _selection = next;
        _currentTheme = ThemeBuilder.Build(next);

        // DevExpress stil dosyalarını runtime'da takas eder; link tag'larını kendi yönetir.
        _ = _devExpressThemeService.SetTheme(_currentTheme);

        // SIRA ÖNEMLİ — ÖNCE GÖRSEL, SONRA KALICILIK. Sunucu yazımı (SetThemeAsync) bir HTTP turu; önce
        // beklenirse DevExpress stili çoktan takas edilmişken Bootstrap değişkenleri geride kalır ve kullanıcı
        // saniyelerce YARIM temada oturur (koyu zemin + açık tema seçili). Görsel adımlar kalıcılığı beklemez.
        try
        {
            var module = await GetModuleAsync();

            // Bootstrap 5.3 CSS değişkenleri mod ile senkron olsun diye <html data-bs-theme>.
            await module.InvokeVoidAsync("setBootstrapColorMode", BootstrapColorMode);
            await module.InvokeVoidAsync("setPrimaryColorHex", PrimaryColorHex);
            // Ayna cookie'si HER uygulamada tazelenir (persist'ten bağımsız): giriş sonrası Initialize da
            // (persist:false) buradan geçer → cookie oturum açan KULLANICININ temasına döner.
            await WriteLastThemeCookieAsync(module, next);
        }
        // Swallowed by design: kullanıcı sayfadan ayrıldı / circuit kapandı.
        catch (JSDisconnectedException)    { }
        catch (TaskCanceledException)      { }
        catch (OperationCanceledException) { }

        // Ekran artık doğru temada; dinleyiciler (ayarlar paneli vb.) hemen tazelensin.
        CurrentThemeChanged?.Invoke(this, EventArgs.Empty);

        // Kalıcılık EN SON: gecikirse yalnız kayıt gecikir, görüntü değil.
        if (persist)
        {
            try
            {
                await _uiSettings.SetThemeAsync(JsonSerializer.Serialize(next));
            }
            catch { /* Ignore API error if backend not updated */ }
        }
    }

    /// <summary>Anonim login SSR'ının okuyacağı ayna cookie'sini yazar. JSON cookie-illegal karakterler
    /// (çift tırnak, virgül) içerdiğinden ENCODE'lu yazıcı kullanılır (App.razor UrlDecode ile okur).</summary>
    private static async Task WriteLastThemeCookieAsync(IJSObjectReference module, ThemeSelection selection)
    {
        try
        {
            var json = JsonSerializer.Serialize(selection);
            await module.InvokeVoidAsync("writeEncodedCookie", LastThemeCookieName, json, 365);
        }
        catch { /* ayna cookie'si yazılamazsa yalnız login görünümü etkilenir — akışı bozma */ }
    }

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/settings.js");
}
