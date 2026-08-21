using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// Kanal ürünü aksiyon düğmeleri — TEK bileşen (2026-08-19). Ürün formu kanal sekmesi, ürün satışa hazırlık paneli
/// ve kanal ürünleri listesi aynı bileşeni çizer; push/senkron/yenile/sorgula mantığı ikinci bir yerde yazılmaz.
///
/// <para><b>Davranış sözleşmesi (eski panellerden AYNEN):</b> N11 gönderimi onaysız; Trendyol gönderimi ÖNCE onay
/// diyaloğu (gerçek push pazaryerinde ürün açar, geri alınamaz). Her başarılı aksiyonun <c>SyncWarnings</c>'i uyarı
/// toast'ı olarak gösterilir — Trendyol'da bu uyarılar bugüne kadar hiç gösterilmiyordu (harita §4 bulgusu); artık
/// iki kanal aynı yoldan geçer. Hata → dostane mesaj (<see cref="CrudErrorPresenter"/>), yoksa "Beklenmeyen hata".</para>
///
/// <para><b>Sonuç geri bildirimi iki kanaldan:</b> tipli <see cref="OnN11Updated"/>/<see cref="OnTrendyolUpdated"/>
/// (ürün formu grafındaki satıra salt-okunur durum alanlarını kopyalamak için — o panel in-memory çalışır, reload
/// yapmaz) ve nötr <see cref="OnChanged"/> (satışa hazırlık paneli/liste yeniden yükler). İkisi de bağlanmak zorunda değil.</para>
/// </summary>
public partial class ChannelProductActions : CrudComponentBase
{
    /// <summary>Hangi kanal ürünü, hangi düğmeler açık — kural bu bileşende değil bağlamı kuranda (sunucu ya da From(...)).</summary>
    [Parameter, EditorRequired] public ChannelProductActionContext Subject { get; set; } = default!;

    /// <summary>Herhangi bir aksiyon sunucuda BAŞARIYLA bitti → çağıran kendi görünümünü tazeler.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>N11 aksiyonu sonucu dönen güncel kanal ürünü — in-memory graf tutan panel durumu buradan kopyalar.</summary>
    [Parameter] public EventCallback<SalesChannelTrN11ProductDto> OnN11Updated { get; set; }

    /// <summary>Trendyol aksiyonu sonucu dönen güncel kanal ürünü — in-memory graf tutan panel durumu buradan kopyalar.</summary>
    [Parameter] public EventCallback<SalesChannelTrTrendyolProductDto> OnTrendyolUpdated { get; set; }

    [Inject] private ISalesChannelTrN11ProductAppService N11AppService { get; set; } = default!;
    [Inject] private ISalesChannelTrTrendyolProductAppService TrendyolAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Aynı satırda iki aksiyon üst üste tıklanmasın — çağrı sürerken düğmeler pasif.
    private bool _busy;

    private string PushText
    {
        get
        {
            return Subject.ChannelType == SalesChannelType.TrN11
                ? L["N11Product:Push"].Value
                : L["TrendyolProduct:Push"].Value;
        }
    }

    private string SyncStockPriceText
    {
        get
        {
            return Subject.ChannelType == SalesChannelType.TrN11
                ? L["N11Product:SyncStockPrice"].Value
                : L["TrendyolProduct:SyncStockPrice"].Value;
        }
    }

    // ── Gönder ──────────────────────────────────────────────────────────────────────────────────────

    private async Task PushAsync()
    {
        if (Subject.ChannelType == SalesChannelType.TrN11)
        {
            await RunN11Async(
                () => N11AppService.PushToN11Async(Subject.ChannelProductId),
                L["N11Product:PushSuccess"].Value);
            return;
        }

        // Gerçek Trendyol gönderimi geri-dönüşsüz (pazaryerinde ürün açılır) — düğme kilidi yerine İNSAN ONAYI.
        // Onay, servis çağrısından ÖNCE ve her tıklamada sorulur; "bir kere onayladım" hafızası YOK.
        var confirmed = await UiService.ConfirmAsync(
            L["TrendyolProduct:PushConfirm"].Value,
            title: null, yesText: L["Yes"].Value, noText: L["Cancel"].Value,
            showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunTrendyolAsync(
            () => TrendyolAppService.PushToTrendyolAsync(Subject.ChannelProductId),
            L["TrendyolProduct:PushSuccess"].Value);
    }

    // ── Stok-Fiyat senkronu ─────────────────────────────────────────────────────────────────────────

    private async Task SyncStockPriceAsync()
    {
        if (Subject.ChannelType == SalesChannelType.TrN11)
        {
            await RunN11Async(
                () => N11AppService.SyncStockAndPriceAsync(Subject.ChannelProductId),
                L["N11Product:SyncStockPriceSuccess"].Value);
            return;
        }

        // Trendyol stok-fiyat senkronu sunucuda vardı (15 dk'lık otonom tur onu çağırır) ama UI'dan İLK KEZ
        // tetiklenebiliyor — kullanıcı bir fiyat düzeltmesini turu beklemeden kanala yazabilsin.
        await RunTrendyolAsync(
            () => TrendyolAppService.SyncStockAndPriceAsync(Subject.ChannelProductId),
            L["TrendyolProduct:SyncStockPriceSuccess"].Value);
    }

    // ── Durumu yenile (Trendyol batch) ──────────────────────────────────────────────────────────────

    private async Task RefreshStatusAsync()
    {
        await RunTrendyolAsync(
            () => TrendyolAppService.RefreshStatusAsync(Subject.ChannelProductId),
            L["TrendyolProduct:RefreshSuccess"].Value);
    }

    // ── Kuyruk sonucunu sorgula (N11 bekleyen task) ─────────────────────────────────────────────────

    private async Task ResolvePendingPushAsync()
    {
        // Task HÂLÂ kuyruktaysa "çözüldü" toast'ı YALAN olur (yanına "hâlâ kuyrukta" uyarısı gelir — çelişkili mesaj).
        // Başarı toast'ı yalnız kimlik gerçekten temizlendiyse; aksi hâlde yalnız SyncWarnings ("hâlâ kuyrukta") görünür.
        await RunN11Async(
            () => N11AppService.ResolvePendingPushAsync(Subject.ChannelProductId),
            L["N11Product:ResolvePendingPushDone"].Value,
            showSuccess: result => string.IsNullOrEmpty(result.PendingPushTaskId));
    }

    // ── Ortak yürütücüler ───────────────────────────────────────────────────────────────────────────

    private async Task RunN11Async(
        Func<Task<SalesChannelTrN11ProductDto>> action,
        string successMessage,
        Func<SalesChannelTrN11ProductDto, bool>? showSuccess = null)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var result = await action();
            if (showSuccess is null || showSuccess(result))
            {
                UiService.ShowSuccessToast(successMessage);
            }

            ShowSyncWarnings(result.SyncWarnings);
            await OnN11Updated.InvokeAsync(result);
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            PresentError(ex);
        }
        finally
        {
            End();
        }
    }

    private async Task RunTrendyolAsync(Func<Task<SalesChannelTrTrendyolProductDto>> action, string successMessage)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var result = await action();
            UiService.ShowSuccessToast(successMessage);
            ShowSyncWarnings(result.SyncWarnings);
            await OnTrendyolUpdated.InvokeAsync(result);
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            PresentError(ex);
        }
        finally
        {
            End();
        }
    }

    private bool Begin()
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        StateHasChanged();
        return true;
    }

    private void End()
    {
        _busy = false;
        StateHasChanged();
    }

    /// <summary>Eşitleme uyarıları (lokalize; ör. "N11 kategoriyi değiştirdi") — başarı toast'ından SONRA, her biri
    /// ayrı uyarı toast'ı. Sessiz geçilmez: kanal bizim gönderdiğimizden farklı bir şey yazmışsa kullanıcı bunu
    /// tek yerden, o anda görmeli.</summary>
    private void ShowSyncWarnings(IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            UiService.ShowWarningToast(warning);
        }
    }

    /// <summary>Hata sunumu — dostane mesaj (BusinessException kodu → lokalize metin), yoksa "Beklenmeyen hata".
    /// Dostane karşılığı olmayan istisnanın sebebi toast'ta KAYBOLUR (UI katmanında yakalandı, sunucu logu görmez)
    /// → teşhis için burada loglanır (2026-08-16 canlı test dersi, Trendyol durum yenilemesinden).</summary>
    private void PresentError(Exception ex)
    {
        Logger.LogWarning(ex, "Kanal ürünü aksiyonu başarısız (kanal {ChannelType}, kanal ürünü {ChannelProductId}).",
            Subject.ChannelType, Subject.ChannelProductId);
        UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
    }
}
