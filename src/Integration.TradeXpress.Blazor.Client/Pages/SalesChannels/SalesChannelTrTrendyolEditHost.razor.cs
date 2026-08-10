using System;
using System.Collections.Generic;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Mdi;
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

    [Inject] private ChannelImportRunner ImportRunner { get; set; } = default!;
    [Inject] private IMdiTabOpener TabOpener { get; set; } = default!;
    [Inject] private IUiInteractionService Ui { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private bool _importing;

    /// <summary>
    /// Kanala özgü İŞLEM düğmeleri MEVCUT araç çubuğuna girer (2026-08-10 Hakan uyarısı: "form altı toolbar
    /// eklemeyi alışkanlık haline getirme, bir toolbarımız varken buna gerek yok"). Önceki hâl formun altında
    /// ayrı bir düğme şeridiydi: uygulamanın geri kalanında işlemler araç çubuğunda dururken bu yüzey istisna
    /// oluşturuyordu ve dikey yeri de boşa harcıyordu. Uzatma noktası zaten vardı — <c>BuildCustomActions</c>.
    ///
    /// <para>YENİ KAYITTA GÖRÜNMEZ: her ikisi de kaydedilmiş bir kanalın kimliğine bağlıdır.</para>
    /// </summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions(SalesChannelTrTrendyolGetDto model)
    {
        if (model.Id == Guid.Empty)
        {
            return System.Array.Empty<CrudToolbarAction>();
        }

        return new List<CrudToolbarAction>
        {
            new()
            {
                SortIndex = 300,
                Text = L["SalesChannel:ImportProducts"],
                Tooltip = L["SalesChannel:ImportProducts"],
                IconCssClass = TradeXpressIcons.Swap + " xaf-toolbar-item-icon",
                Enabled = !_importing,
                OnClick = () => RunImportAsync(model.Id),
            },

            // GEÇİCİ (2026-08-10): sihirbaza başka ulaşılabilir giriş yok. Yeni tasarım oturunca kaldırılacak.
            new()
            {
                SortIndex = 310,
                Text = L["SalesChannel:OpenSetupWizard"],
                Tooltip = L["SalesChannel:OpenSetupWizard"],
                IconCssClass = TradeXpressIcons.SalesChannel + " xaf-toolbar-item-icon",
                OnClick = () => OpenWizardAsync(model.Id),
            },
        };
    }

    /// <summary>Mağazadan içe aktarım — tür dağıtımı ortak koşucuda (<c>ChannelImportRunner</c>), kanal
    /// listesindeki düğmeyle AYNI yol. Çalışırken düğme pasif: ikinci tıklama paralel bir içe aktarım
    /// başlatırdı.</summary>
    private async Task RunImportAsync(Guid channelId)
    {
        if (_importing)
        {
            return;
        }

        _importing = true;
        StateHasChanged();
        try
        {
            var outcome = await ImportRunner.RunAsync(channelId, SalesChannelType.TrTrendyol);
            Ui.ShowSuccessToast(outcome.Supported
                ? L["SalesChannel:ImportDone", outcome.Created, outcome.Updated].Value
                : L["SalesChannel:ImportUnsupported"].Value);
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
        finally
        {
            _importing = false;
            StateHasChanged();
        }
    }

    /// <summary>MDI kabuğunda <c>NavigationManager</c> NO-OP'tur → sihirbaz <c>IMdiTabOpener</c> ile
    /// sekmede açılır.</summary>
    private Task OpenWizardAsync(Guid channelId)
    {
        return TabOpener.OpenOrActivateAsync(
            "/sales-channels/trendyol/wizard/" + channelId,
            L["SalesChannelTrTrendyol:Wizard:Title"].Value,
            TradeXpressIcons.SalesChannel);
    }
}
