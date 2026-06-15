using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;

namespace Integration.Framework.Blazor.Client.Resilience;

/// <summary>
/// Tek-seferlik otomatik kurtaran ErrorBoundary. Geçici bir render hatası boundary'i trip ettirirse
/// bir kez otomatik <see cref="Microsoft.AspNetCore.Components.Web.ErrorBoundaryBase.Recover"/> dener
/// (ör. anlık bir veri/ağ hatasından sonra ekran kendini toparlar). Hata kısa süre (5 sn) içinde
/// tekrar gelirse KALICI kabul edilir; otomatik kurtarma yapılmaz ve ErrorContent gösterilir
/// (kullanıcı manuel "Yenile" ile kurtarabilir). Böylece kurtarma-döngüsü riski engellenir.
///
/// base.OnErrorAsync hatayı ErrorBoundaryLogger ile loglar → WASM'da console.error → Developer
/// Error Panel yakalar. Yani otomatik kurtarılan hatalar da panelde iz bırakır.
/// </summary>
public sealed class AutoRecoverErrorBoundary : ErrorBoundary
{
    // D1 HTTP retry bütçesi (~7sn) nedeniyle boundary hataları ~7sn arayla gelebilir; pencere bunun
    // üstünde (20sn) olmalı ki API kapalıyken ikinci hata "kalıcı" sayılıp kurtarma döngüsü dursun.
    private static readonly TimeSpan PersistentWindow = TimeSpan.FromSeconds(20);
    private DateTime _lastRecoverUtc = DateTime.MinValue;

    protected override async Task OnErrorAsync(Exception exception)
    {
        await base.OnErrorAsync(exception);

        var now = DateTime.UtcNow;
        if (now - _lastRecoverUtc > PersistentWindow)
        {
            // Geçici kabul → bir kez otomatik kurtar. Reentrancy'den kaçınmak için
            // mevcut hata akışı bittikten sonra renderer context'inde çalıştır.
            _lastRecoverUtc = now;
            await InvokeAsync(Recover);
        }
        // aksi halde (5 sn içinde tekrar) kalıcı → ErrorContent göster, kullanıcı manuel kurtarsın.
    }
}
