using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>N11 satış kanalı edit host code-behind — coordinator kurulumu (tipe-özel ISalesChannelTrN11AppService)
/// + create-anı otomatik ürün içe aktarımı işareti (Trendyol'daki 2026-07-11 kararıyla hizalandı: "ürünleri çek,
/// yeni kanal eklenirken olsun").</summary>
public partial class SalesChannelTrN11EditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected ISalesChannelTrN11AppService AppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<SalesChannelTrN11GetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto>? _coordinator;

    // Create-success işareti — layout'a akar; N11MarketplaceImportPanel İLK görünümünde içe aktarımı kendisi
    // başlatır (spinner + rapor panelde). Kimlik create'te sunucuda ZATEN doğrulanır (verifier geçemezse kayıt
    // açılmaz → bu işaret hiç kurulamaz); update yolunda ASLA tetiklenmez (OnAfterCreate yalnız yeni kayıtta).
    private bool _autoImportProducts;

    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<SalesChannelTrN11GetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto, SalesChannelTrN11CreateDto, SalesChannelTrN11UpdateDto>(
            AppService, Mapper);
    }

    /// <summary>Kanal BAŞARIYLA oluşturuldu — içe aktarım işaretini kur; re-render'da panel görünür olur ve çekimi
    /// otomatik başlatır. İşin kendisi panelde (iptal edilebilirlik + UoW süresi için UI seviyesi doğru).</summary>
    private Task OnChannelCreatedAsync(SalesChannelTrN11GetDto _)
    {
        _autoImportProducts = true;
        return Task.CompletedTask;
    }
}
