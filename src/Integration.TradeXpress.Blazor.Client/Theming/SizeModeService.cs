using System;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.TradeXpress.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// Boyut modunun TEK doğruluk kaynağı SUNUCU per-user ayarıdır (GetSizeModeAsync/SetSizeModeAsync —
/// ThemeService ile aynı desen); tarayıcıdaki <c>tx.last_size</c> cookie'si yalnız "son oturum açan
/// kullanıcı" AYNASIDIR: kimlikli akışta YAZILIR, anonim login akışı (App.razor SSR + EmptyLayout init)
/// onu yalnız OKUR. Eski localStorage kaydı (<c>tx.size</c>) sunucu ayarı boşken TEK SEFERLİK tohum
/// olarak devralınır (mevcut kullanıcıların boyutu kaybolmasın). Değişimde <c>data-erp-size</c>
/// attribute'u <c>&lt;html&gt;</c> üzerinde güncellenir.
/// </summary>
public sealed class SizeModeService : ISizeModeService
{
    /// <summary>Legacy localStorage anahtarı — yalnız tek seferlik tohumlama için okunur.</summary>
    public const string StorageKey = "tx.size";

    /// <summary>Anonim login akışının okuduğu boyut aynası cookie'si (enum adı: Small/Medium/Large).</summary>
    public const string LastSizeCookieName = "tx.last_size";

    private readonly IJSRuntime _js;
    private readonly IUserUiSettingAppService _uiSettings;
    private readonly AuthenticationStateProvider _authStateProvider;
    private IJSObjectReference? _module;
    private SizeMode _current = SizeMode.Medium;

    public SizeModeService(
        IJSRuntime js,
        IUserUiSettingAppService uiSettings,
        AuthenticationStateProvider authStateProvider)
    {
        _js = js;
        _uiSettings = uiSettings;
        _authStateProvider = authStateProvider;
    }

    public SizeMode CurrentSizeMode => _current;

    public event EventHandler? SizeModeChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var module = await GetModuleAsync();

            SizeMode? mode;
            if (await IsAuthenticatedAsync())
            {
                mode = await ReadServerSizeModeAsync(module);
                if (mode is { } m)
                {
                    // Ayna cookie'si oturum açan kullanıcının değerine döner (login ekranı bunu okur).
                    await module.InvokeVoidAsync("writeCookie", LastSizeCookieName, m.ToString(), 365);
                }
            }
            else
            {
                // Anonim (login/EmptyLayout): yalnız ayna cookie'si okunur — sunucu ayarına dokunulmaz.
                mode = TryParse(await module.InvokeAsync<string?>("getCookie", LastSizeCookieName));
            }

            if (mode is { } resolved)
            {
                _current = resolved;
                await module.InvokeVoidAsync("setSizeModeAttribute", resolved.ToString());
                SizeModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (JSDisconnectedException)    { }
        catch (TaskCanceledException)      { }
        catch (OperationCanceledException) { }
    }

    public async Task SetAsync(SizeMode sizeMode)
    {
        if (_current == sizeMode) return;
        _current = sizeMode;

        try
        {
            var module = await GetModuleAsync();
            try { await _uiSettings.SetSizeModeAsync(sizeMode.ToString()); }
            catch { /* sunucu yazılamazsa cookie + görünüm yine güncellenir */ }
            await module.InvokeVoidAsync("writeCookie", LastSizeCookieName, sizeMode.ToString(), 365);
            await module.InvokeVoidAsync("setSizeModeAttribute", sizeMode.ToString());
        }
        // Swallowed by design: circuit kapandı / sayfadan ayrıldı.
        catch (JSDisconnectedException)    { }
        catch (TaskCanceledException)      { }
        catch (OperationCanceledException) { }

        SizeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sunucu ayarını okur; boşsa legacy localStorage değerini TEK SEFERLİK devralıp sunucuya yazar.</summary>
    private async Task<SizeMode?> ReadServerSizeModeAsync(IJSObjectReference module)
    {
        string? stored = null;
        try { stored = await _uiSettings.GetSizeModeAsync(); }
        catch { /* API hatası → aşağıdaki tohum/varsayılan yolu */ }

        if (TryParse(stored) is { } fromServer)
        {
            return fromServer;
        }

        // Legacy tohum: eski sürüm boyutu yalnız localStorage'da tutuyordu.
        var legacy = TryParse(await module.InvokeAsync<string?>("getLocal", StorageKey));
        if (legacy is { } seeded)
        {
            try { await _uiSettings.SetSizeModeAsync(seeded.ToString()); }
            catch { /* tohum yazılamazsa bir sonraki oturumda tekrar denenir */ }
            return seeded;
        }
        return null;
    }

    private async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            var state = await _authStateProvider.GetAuthenticationStateAsync();
            return state.User.Identity?.IsAuthenticated == true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/settings.js");

    private static SizeMode? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<SizeMode>(value, ignoreCase: true, out var parsed) ? parsed : null;
    }
}
