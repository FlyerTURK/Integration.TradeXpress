using System;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// WASM uyarlaması: boyut modunu localStorage'da saklar (referanstaki cookie +
/// IHttpContextAccessor yerine). Değişimde <c>data-erp-size</c> attribute'unu da
/// <c>&lt;html&gt;</c> üzerinde günceller; isteğe bağlı CSS kuralları bunu izleyebilir.
/// </summary>
public sealed class SizeModeService : ISizeModeService
{
    public const string StorageKey = "tx.size";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private SizeMode _current = SizeMode.Medium;

    public SizeModeService(IJSRuntime js)
    {
        _js = js;
    }

    public SizeMode CurrentSizeMode => _current;

    public event EventHandler? SizeModeChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            var value = await module.InvokeAsync<string?>("getLocal", StorageKey);
            var parsed = TryParse(value);
            if (parsed is { } mode)
            {
                _current = mode;
                await module.InvokeVoidAsync("setSizeModeAttribute", mode.ToString());
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
            await module.InvokeVoidAsync("setLocal", StorageKey, sizeMode.ToString());
            await module.InvokeVoidAsync("setSizeModeAttribute", sizeMode.ToString());
        }
        // Swallowed by design: circuit kapandı / sayfadan ayrıldı.
        catch (JSDisconnectedException)    { }
        catch (TaskCanceledException)      { }
        catch (OperationCanceledException) { }

        SizeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/settings.js");

    private static SizeMode? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<SizeMode>(value, ignoreCase: true, out var parsed) ? parsed : null;
    }
}
