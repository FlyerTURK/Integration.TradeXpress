using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Timing;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// Görünmez sonda bileşeni: ilk render'da (yalnız bir kez) kullanıcının tarayıcı IANA saat dilimini
/// JS interop (<c>user-timezone.js → getTimeZone</c>) ile okuyup <see cref="UserTimeZoneAccessor"/>'a yazar.
/// Blazor Server'da JS yalnız render sonrası çalışır (prerender'da yok) → <c>firstRender</c> guard'ı şart.
/// Kök layout'a bir kez yerleştirilir; kendisi hiçbir görünür çıktı üretmez.
/// </summary>
public partial class UserTimeZoneProbe : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private UserTimeZoneAccessor Accessor { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Yalnız ilk render'da ve TZ henüz yakalanmadıysa çalış (devre boyunca tek sefer).
        if (!firstRender || Accessor.IsResolved)
        {
            return;
        }

        try
        {
            await using var module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Integration.Framework.Blazor.Client/js/user-timezone.js");
            var ianaId = await module.InvokeAsync<string?>("getTimeZone");
            Accessor.Set(ianaId);
        }
        catch (JSDisconnectedException)
        {
            // Devre kapandı (yakalamadan) — sessiz geç; fallback (UTC) devrede kalır.
        }
    }
}
