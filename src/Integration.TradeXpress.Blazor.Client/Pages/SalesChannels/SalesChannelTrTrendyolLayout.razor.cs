using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolShipments;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Trendyol dumb layout code-behind — yalnız Model bağlama (I/O yok).</summary>
public partial class SalesChannelTrTrendyolLayout
{
    [Parameter, EditorRequired] public SalesChannelTrTrendyolGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success). <b>Layout artık bu bilgiyi KULLANMIYOR</b>
    /// (2026-08-10): create-anı otomatik içe aktarımı yürüten <c>TrendyolMarketplaceImportPanel</c> bu yüzeyden
    /// kaldırıldı. Parametre, edit host'un bağını KIRMAMAK için duruyor — akış yeniden tasarlandığında ya
    /// tüketilecek ya host'la birlikte sökülecek.</summary>
    [Parameter] public bool AutoImportProducts { get; set; }

    [Inject] private ITrendyolCargoProviderAppService CargoProviderAppService { get; set; } = default!;

    /// <summary>Kargo firması combo'sunun verisi — host-global seed'den (Trendyol hesabından BAĞIMSIZ dolu).</summary>
    private List<TrendyolCargoProviderDto> _cargoProviders = new();

    /// <summary>
    /// Firmaları yükler ve YENİ kanalda seçimi ÖN-DOLDURUR — kullanıcı formu açar açmaz varsayılanı GÖRÜR
    /// (2026-08-10 Hakan talebi: "default kargo firması seçili olsun").
    ///
    /// <para><b>Varsayılanı burada SEÇMİYORUZ, SORUYORUZ:</b> karar sunucudadır
    /// (<c>TrendyolDefaultCargoProviderResolver</c>). Sebebi, kanalın üç yoldan da (form · sihirbaz ·
    /// doğrudan API) aynı firmayı almasıdır; istemcide "listenin ilkini seç" deseydik API'den açılan kanal
    /// başka bir firmaya düşerdi ve fark ancak ilk gönderimde görülürdü.</para>
    ///
    /// <para><b>Yalnız BOŞ alan doldurulur:</b> kayıtlı kanalın seçimi ezilmez ve kullanıcının bilinçli
    /// olarak temizlediği alan bir sonraki açılışta geri gelmez.</para>
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        _cargoProviders = await CargoProviderAppService.GetListAsync();

        if (Model.DefaultCargoProviderId is null && IsNew)
        {
            Model.DefaultCargoProviderId = await CargoProviderAppService.GetDefaultIdAsync();
        }
    }

    /// <summary>Düzenlemede sir alanları (ApiKey/ApiSecret) boş gelir → in-field ipucu; yeni kayıtta placeholder yok.</summary>
    private string? SecretPlaceholder => IsNew ? null : L["SalesChannel:SecretKept"].Value;

    // Giderler formu ve onu besleyen SideCosts getter'ı 2026-07-28'de KALDIRILDI. Bu getter'ın kalması
    // TEHLİKELİYDİ: ayarı hiç yapılandırılmamış (null) kanalda boş bir DTO üretip kayda {"Items":[]} yazdırıyordu
    // ve o değer "kullanıcı komisyon satırını sildi" anlamına geldiği için komisyon fiyata HİÇ girmiyordu —
    // hatasız, logsuz, yalnız ~%23 ucuz fiyat. Ayar artık null kalıyor; komisyonu kategori oranından örtük olarak
    // SideCostPlan.From üretiyor (SideCostRecipeComposerTests'teki iki test bunu çiviliyor).
}
