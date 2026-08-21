using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Etsy dumb layout code-behind — yalnız Model bağlama (I/O yok; OAuth başlatma host'a delege).</summary>
public partial class SalesChannelEtsyLayout
{
    [Parameter, EditorRequired] public SalesChannelEtsyGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success) — import paneline akar; panel ilk görünümünde
    /// importu otomatik başlatır (Trendyol deseni). Update yolunda host bunu asla kurmaz.</summary>
    [Parameter] public bool AutoImportProducts { get; set; }

    /// <summary>"Etsy'ye Bağlan" — host StartOAuthAsync çağırıp onay sayfasını yeni sekmede açar.</summary>
    [Parameter] public EventCallback OnConnectClick { get; set; }

    /// <summary>Düzenlemede sir alanı (SharedSecret) boş gelir → in-field ipucu; yeni kayıtta placeholder yok.</summary>
    private string? SecretPlaceholder => IsNew ? null : L["SalesChannel:SecretKept"].Value;

    /// <summary>Mağaza alanları OAuth bağlantısında Etsy'den çözülür → salt-okunur; boşken bunu anlatan ipucu.</summary>
    private string ShopPlaceholder => L["SalesChannel:Etsy:ShopUnresolved"].Value;

    private string EtsySecretCaption => $"{L["SalesChannel:Etsy:SharedSecret"]} {(IsNew ? "*" : string.Empty)}".TrimEnd();

    private string ConnectionStatusText => Model.IsConnected
        ? L["SalesChannel:Etsy:Connected"].Value
        : L["SalesChannel:Etsy:NotConnected"].Value;

    /// <summary>Bağlıyken buton "Yeniden Bağlan" olur (scope/keystring değişimi ya da 90-gün kopmasında tazeler).</summary>
    private string ConnectButtonText => Model.IsConnected
        ? L["SalesChannel:Etsy:Reconnect"].Value
        : L["SalesChannel:Etsy:Connect"].Value;

    // Giderler formu ve onu besleyen SideCosts getter'ı 2026-07-28'de KALDIRILDI. Bu getter'ın kalması
    // TEHLİKELİYDİ: ayarı hiç yapılandırılmamış (null) kanalda boş bir DTO üretip kayda {"Items":[]} yazdırıyordu
    // ve o değer "kullanıcı komisyon satırını sildi" anlamına geldiği için komisyon fiyata HİÇ girmiyordu —
    // hatasız, logsuz, yalnız ~%23 ucuz fiyat. Ayar artık null kalıyor; komisyonu kategori oranından örtük olarak
    // SideCostPlan.From üretiyor (SideCostRecipeComposerTests'teki iki test bunu sabitliyor).
}
