using Integration.TradeXpress.SalesChannels;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Trendyol dumb layout code-behind — yalnız Model bağlama (I/O yok).</summary>
public partial class SalesChannelTrTrendyolLayout
{
    [Parameter, EditorRequired] public SalesChannelTrTrendyolGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success) — import paneline akar; panel ilk görünümünde
    /// importu otomatik başlatır (2026-07-11 kullanıcı kararı). Update yolunda host bunu asla kurmaz.</summary>
    [Parameter] public bool AutoImportProducts { get; set; }

    /// <summary>Kurulum sihirbazını MDI SEKMESİNDE açar (mevcut kanal kipinde).
    ///
    /// <para><b>NavigationManager KULLANILMAZ</b> (2026-08-06 düzeltmesi): bu kabukta rotalar sekme olarak
    /// açılır ve adres çubuğu kökte tutulur — <c>NavigateTo</c> sessizce hiçbir şey yapmıyordu. Düğme
    /// tıklanıyor, hiçbir şey olmuyordu; sihirbaz da "Yeni ▾" tek-kanal kuralıyla kapalı olduğundan
    /// fiilen ULAŞILAMAZ durumdaydı.</para></summary>
    [Inject] private IMdiTabOpener TabOpener { get; set; } = default!;

    private Task OpenWizardAsync()
    {
        return TabOpener.OpenOrActivateAsync(
            $"/sales-channels/trendyol/wizard/{Model.Id}",
            L["SalesChannelTrTrendyol:Wizard:Title"].Value,
            TradeXpressIcons.SalesChannel);
    }

    /// <summary>Düzenlemede sir alanları (ApiKey/ApiSecret) boş gelir → in-field ipucu; yeni kayıtta placeholder yok.</summary>
    private string? SecretPlaceholder => IsNew ? null : L["SalesChannel:SecretKept"].Value;

    // Giderler formu ve onu besleyen SideCosts getter'ı 2026-07-28'de KALDIRILDI. Bu getter'ın kalması
    // TEHLİKELİYDİ: ayarı hiç yapılandırılmamış (null) kanalda boş bir DTO üretip kayda {"Items":[]} yazdırıyordu
    // ve o değer "kullanıcı komisyon satırını sildi" anlamına geldiği için komisyon fiyata HİÇ girmiyordu —
    // hatasız, logsuz, yalnız ~%23 ucuz fiyat. Ayar artık null kalıyor; komisyonu kategori oranından örtük olarak
    // SideCostPlan.From üretiyor (SideCostRecipeComposerTests'teki iki test bunu çiviliyor).
}
