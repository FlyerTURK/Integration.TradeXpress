using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Etsy dumb layout code-behind — yalnız Model bağlama (I/O yok; OAuth başlatma host'a delege).</summary>
public partial class SalesChannelEtsyLayout
{
    [Parameter, EditorRequired] public SalesChannelEtsyGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

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

    private bool _sideCostsWereNull;

    /// <summary>Giderler formu daima dolu DTO'ya bağlanır — null ilk erişimde boş DTO olur; null'dan geldiği
    /// BİLGİSİ saklanır: tohum önerisi yalnız bu durumda (N11 layout paritesi).</summary>
    private SideCostSettingsDto SideCosts
    {
        get
        {
            if (Model.SideCosts is null)
            {
                _sideCostsWereNull = true;
                Model.SideCosts = new SideCostSettingsDto();
            }

            return Model.SideCosts;
        }
    }

    /// <summary>Tohum önerisi bayrağı — attribute sırasından bağımsız: önce getter null→boş dönüşümünü işler.</summary>
    private bool SuggestSideCostDefaults
    {
        get
        {
            _ = SideCosts;
            return _sideCostsWereNull;
        }
    }
}
