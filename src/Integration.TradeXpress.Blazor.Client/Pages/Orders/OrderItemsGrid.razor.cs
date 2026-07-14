using System.Linq;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>
/// Master-detail DETAIL grid'i — bir siparişin KALEMLERİ (<see cref="OrderListDto.Items"/>). Zengin kalem kolonları;
/// durum kanal-farkında N11 kodu → etiket. Veri master satırda hazır (ekstra sorgu yok).
/// </summary>
public partial class OrderItemsGrid
{
    /// <summary>Kalemleri gösterilecek MASTER sipariş satırı.</summary>
    [Parameter] public OrderListDto? Order { get; set; }

    // Kalem durumu — N11 için tam sayı kodu (ör. "10") → etikete; diğer kanal ham.
    private string? ItemStatusText(OrderItemListDto item)
        => item.ChannelType == SalesChannelType.TrN11 ? N11OrderStatusCatalog.ItemStatusLabel(item.RemoteLineStatus) : item.RemoteLineStatus;

    // Kargo = firma + yöntem (ör. "Aras · Kargo"); yöntem N11 kodu (1 Kargo · 2 Diğer), bilinmeyen kod ham.
    private string? ShipmentText(OrderItemDetailDto? detail)
    {
        if (detail is null)
        {
            return null;
        }

        string? method = detail.ShipmentMethod switch
        {
            1 => L["Enum:N11ShipmentMethod:Cargo"],
            2 => L["Enum:N11ShipmentMethod:Other"],
            { } code => code.ToString(),
            _ => null,
        };
        return string.Join(" · ", new[] { detail.ShipmentCompany, method }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
