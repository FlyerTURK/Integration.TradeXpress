using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>N11 dumb layout code-behind — yalnız Model bağlama (I/O yok).</summary>
public partial class SalesChannelTrN11Layout
{
    [Parameter, EditorRequired] public SalesChannelTrN11GetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Düzenlemede sir alanları boş gelir → in-field ipucu "saklı, boş = korunur"; yeni kayıtta placeholder yok.</summary>
    private string? SecretPlaceholder => IsNew ? null : L["SalesChannel:SecretKept"].Value;

    private bool _sideCostsWereNull;

    /// <summary>Giderler formu daima dolu DTO'ya bağlanır — null (hiç yapılandırılmamış) ilk erişimde boş DTO olur;
    /// commit'te Create/Update input'una map'lenip sunucuda VO'ya çevrilir. Null'dan geldiği BİLGİSİ saklanır:
    /// tohum önerisi yalnız bu durumda (bilerek boşaltılmış {"Items":[]} kaydı yeniden tohumlanmaz).</summary>
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
