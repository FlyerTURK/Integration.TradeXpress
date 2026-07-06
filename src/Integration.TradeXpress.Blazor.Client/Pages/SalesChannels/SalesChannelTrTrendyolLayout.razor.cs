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
}
