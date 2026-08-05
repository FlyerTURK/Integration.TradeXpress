using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

using System.Threading.Tasks;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Trendyol satış kanalı edit host code-behind — coordinator kurulumu (tipe-özel ISalesChannelTrTrendyolAppService)
/// + create-anı otomatik ürün importu işareti (2026-07-11 kullanıcı kararı: "ürünleri çek, yeni kanal eklenirken olsun").</summary>
public partial class SalesChannelTrTrendyolEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected ISalesChannelTrTrendyolAppService AppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<SalesChannelTrTrendyolGetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto>? _coordinator;

    // Create-success işareti — layout'a akar; TrendyolMarketplaceImportPanel İLK görünümünde importu kendisi başlatır
    // (spinner + rapor panelde). Kimlik create'te sunucuda ZATEN doğrulanır (verifier geçemezse kayıt açılmaz →
    // bu işaret hiç kurulamaz); update yolunda ASLA tetiklenmez (OnAfterCreate yalnız yeni kayıtta çalışır).
    private bool _autoImportProducts;

    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<SalesChannelTrTrendyolGetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto, SalesChannelTrTrendyolCreateDto, SalesChannelTrTrendyolUpdateDto>(
            AppService, Mapper);
    }

    /// <summary>Kanal BAŞARIYLA oluşturuldu (create-success) — import işaretini kur; re-render'da panel görünür olur
    /// ve importu otomatik başlatır. İşin kendisi panelde (iptal edilebilirlik + UoW süresi için UI seviyesi doğru).
    ///
    /// <para><b>Pratikte artık tetiklenmiyor</b> (2026-08-04): yeni Trendyol kanalı kurulum SİHİRBAZINDAN açılıyor
    /// ve çekim orada AYRI bir adım. Bu host yalnız DÜZENLEME yolunda kullanılıyor, orada da <c>OnAfterCreate</c>
    /// çalışmaz. Kod SİLİNMEDİ: host doğrudan (sihirbaz baypas edilerek) kullanılırsa create-anı çekimi hâlâ
    /// doğru davranıştır — sihirbaz kaldırılırsa da eski akış kendiliğinden geri gelir.</para></summary>
    private Task OnChannelCreatedAsync(SalesChannelTrTrendyolGetDto _)
    {
        _autoImportProducts = true;
        return Task.CompletedTask;
    }
}
