using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Inbox;
using Microsoft.AspNetCore.Components;
using Volo.Abp;

namespace Integration.TradeXpress.Blazor.Client.Pages.Inbox;

/// <summary>
/// Ortak gelen kutusu PANOSU — tüm "dikkat bekleyen" türlerin tek ekranda ÖZETİ.
///
/// <para><b>Sayfa hiçbir türü tanımaz.</b> Kartları <see cref="IInboxAppService.GetSummaryAsync"/> üretir;
/// o da kayıtlı <c>IInboxSummaryProvider</c>'ları gezer. Bu yüzden yeni bir tür (yarın kullanıcı mesajlaşması)
/// eklemek YALNIZCA yeni bir sağlayıcı yazmaktır — burada <c>SourceKey</c>'e göre özel dal YOKTUR ve bu dosya
/// değişmez. Kart görünümü de tür-nötr: ikon + başlık + bekleyen sayısı + son hareketler.</para>
///
/// <para><b>Özet + derinlemesine:</b> pano yalnız özet gösterir; kart başlığına, "Tümünü Gör"e ya da bir
/// satıra tıklamak türün KENDİ tam ekranını açar (<see cref="InboxCardDto.TargetUrl"/>). Mevcut Teyit /
/// Ürün Soruları ekranları taşınmaz, değişmez.</para>
///
/// <para><b>Neden TabManager (NavigationManager değil):</b> uygulama MDI kabuğudur — sayfalar Router'la değil
/// <c>DynamicComponent</c> ile sekmelerde açılır (bkz. <see cref="RouteResolver"/> ve MainLayout'un
/// "MDI'da sayfa değişimi sekmelerle olur" notu). Düz <c>NavigateTo</c> yalnız adres çubuğunu değiştirir,
/// hedef ekranı AÇMAZ. NavMenu'nün deseni birebir izlenir: çözümlenebilen rota sekme olur, çözümlenemeyen
/// (ABP admin sayfaları gibi MDI dışı) rota tam navigasyona düşer.</para>
/// </summary>
public partial class InboxPage
{
    #region Constants

    /// <summary>Kart satırında gösterilen metin önizlemesinin azami uzunluğu (tamamı tam ekranda).</summary>
    private const int ItemPreviewLength = 60;

    #endregion

    #region Injected

    [Inject] private IInboxAppService InboxAppService { get; set; } = default!;

    [Inject] private ITabManager TabManager { get; set; } = default!;

    [Inject] private RouteResolver RouteResolver { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private IUiInteractionService Ui { get; set; } = default!;

    #endregion

    #region State

    private IReadOnlyList<InboxCardDto> _cards = Array.Empty<InboxCardDto>();

    /// <summary>Kartlar sunucudan geliyor — ilk açılışta true (boş-kutu mesajı erken yanıp sönmesin).</summary>
    private bool _loading = true;

    /// <summary>Özet alınamadıysa sayfada KALICI hata satırı (toast kaçar, kullanıcı sebebi göremez).</summary>
    private string? _error;

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LoadAsync();
    }

    #endregion

    #region Data

    /// <summary>Panoyu sunucudan tazeler. Toolbar "Yenile" de buraya bağlıdır.</summary>
    private async Task LoadAsync()
    {
        _loading = true;
        _error   = null;
        StateHasChanged();

        try
        {
            _cards = await InboxAppService.GetSummaryAsync();
        }
        catch (Exception ex)
        {
            // Tek bir sağlayıcı patlarsa bile kutu kullanılamaz hâle gelmesin: sebep yazılır, pano boş kalır.
            var message = Describe(ex);
            _cards = Array.Empty<InboxCardDto>();
            _error = message;
            Ui.ShowErrorToast(message);
        }
        finally
        {
            _loading = false;
        }
    }

    #endregion

    #region Navigation

    /// <summary>Kartın tam ekranını açar (MDI sekmesi). Derinlemesine kırılım ileride — bugün kart başlığı,
    /// "Tümünü Gör" ve satır tıklaması AYNI ekrana götürür.</summary>
    private async Task OpenCardAsync(InboxCardDto card)
    {
        if (string.IsNullOrWhiteSpace(card.TargetUrl))
        {
            return;
        }

        if (RouteResolver.IsKnownPage(card.TargetUrl))
        {
            await TabManager.OpenOrActivateAsync(card.TargetUrl, card.Title, card.IconCssClass);
            return;
        }

        // MDI dışı hedef (ABP admin sayfaları gibi) — NavMenu ile aynı fallback.
        Navigation.NavigateTo(card.TargetUrl, forceLoad: true);
    }

    #endregion

    #region Display

    /// <summary>Satır tıklaması tam ekranı açtığı için satırlar üzerinde el (pointer) imleci — tıklanabilirlik
    /// keşfedilebilir olsun (CrudLayout / Ürün Soruları kutusuyla aynı davranış).</summary>
    private static void OnCustomizeGridElement(GridCustomizeElementEventArgs e)
    {
        if (e.ElementType == GridElementType.DataRow)
        {
            e.Style = "cursor: pointer;";
        }
    }

    /// <summary>Bekleyen sayacı rozeti: 0 ise NÖTR gri (yanlış aciliyet hissi vermesin), &gt;0 ise amber
    /// (Teyit / Ürün Soruları kutularıyla aynı "bekliyor" renk dili).</summary>
    private static string PendingBadgeStyle(int pendingCount)
    {
        var background = pendingCount > 0 ? "#f59e0b" : "#94a3b8";

        return "display:inline-block; padding:2px 10px; border-radius:10px; font-size:12px; "
             + $"font-weight:600; color:#fff; background:{background};";
    }

    /// <summary>Bekleyen öğe kalın yazılır — kart içinde göz önce oraya gitsin.</summary>
    private static string ItemTextStyle(bool isPending)
    {
        if (isPending)
        {
            return "font-weight:600;";
        }

        return string.Empty;
    }

    /// <summary>Çok satırlı uzun metni tek satırlık kart önizlemesine indirger.</summary>
    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }

    /// <summary>Sunucu hatasını kullanıcı diline çevirir (lokalize error-code); kod yoksa ham mesaj.</summary>
    private string Describe(Exception ex)
    {
        if (ex is not BusinessException { Code: { } code } || string.IsNullOrWhiteSpace(code))
        {
            return ex.Message;
        }

        return L[code].Value;
    }

    #endregion
}
