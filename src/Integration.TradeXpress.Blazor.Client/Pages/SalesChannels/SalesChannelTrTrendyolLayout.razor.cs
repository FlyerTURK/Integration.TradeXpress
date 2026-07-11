using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Trendyol dumb layout code-behind — yalnız Model bağlama (I/O yok).</summary>
public partial class SalesChannelTrTrendyolLayout
{
    [Parameter, EditorRequired] public SalesChannelTrTrendyolGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Düzenlemede sir alanları (ApiKey/ApiSecret) boş gelir → in-field ipucu; yeni kayıtta placeholder yok.</summary>
    private string? SecretPlaceholder => IsNew ? null : L["SalesChannel:SecretKept"].Value;

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
