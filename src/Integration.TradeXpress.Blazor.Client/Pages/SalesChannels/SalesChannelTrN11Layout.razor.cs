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
}
