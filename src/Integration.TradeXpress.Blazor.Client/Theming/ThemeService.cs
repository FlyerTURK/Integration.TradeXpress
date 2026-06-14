using System;
using System.Text.Json;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// WASM uyarlaması: DevExpress <see cref="IThemeChangeService"/> üzerine,
/// seçimi tarayıcı localStorage'ında saklayan ince bir katman. Sunucu tarafı
/// referanstaki cookie + IHttpContextAccessor yerine, tekil (single-user) WASM
/// için localStorage kullanılır. Açılışta <see cref="InitializeAsync"/> kaydı
/// okuyup uygulayarak kullanıcının önceki temasını geri yükler.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly IJSRuntime _js;
    private readonly IThemeChangeService _devExpressThemeService;
    private IJSObjectReference? _module;
    private ThemeSelection _selection = ThemeSelection.Default;
    private ITheme _currentTheme;

    public ThemeService(IJSRuntime js, IThemeChangeService devExpressThemeService)
    {
        _js = js;
        _devExpressThemeService = devExpressThemeService;
        _currentTheme = ThemeBuilder.Build(_selection);
    }

    public ThemeSelection CurrentSelection => _selection;

    public ITheme CurrentTheme => _currentTheme;

    public string BootstrapColorMode => ResolveBootstrapColorMode(_selection);

    public event EventHandler? CurrentThemeChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            var json = await module.InvokeAsync<string?>("getLocal", ThemeCatalog.StorageKey);
            var saved = TryReadSelection(json);
            if (saved is not null)
            {
                await ApplyAsync(saved, persist: false);
            }
            else
            {
                // Kayıt yoksa varsayılanın data-bs-theme'i de doğru yazılsın.
                await module.InvokeVoidAsync("setBootstrapColorMode", BootstrapColorMode);
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

        try
        {
            var module = await GetModuleAsync();
            if (persist)
            {
                var json = JsonSerializer.Serialize(next);
                await module.InvokeVoidAsync("setLocal", ThemeCatalog.StorageKey, json);
            }
            // Bootstrap 5.3 CSS değişkenleri mod ile senkron olsun diye <html data-bs-theme>.
            await module.InvokeVoidAsync("setBootstrapColorMode", BootstrapColorMode);
        }
        // Swallowed by design: kullanıcı sayfadan ayrıldı / circuit kapandı.
        catch (JSDisconnectedException)    { }
        catch (TaskCanceledException)      { }
        catch (OperationCanceledException) { }

        CurrentThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ResolveBootstrapColorMode(ThemeSelection selection)
    {
        if (selection.Kind == ThemeKind.Fluent)
        {
            return selection.FluentMode == ThemeMode.Dark ? "dark" : "light";
        }
        // Koyu yüzey ile gelen Bootstrap teması.
        return selection.BootstrapName == "Blazing Dark" ? "dark" : "light";
    }

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/settings.js");

    private static ThemeSelection? TryReadSelection(string? json)
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
}
