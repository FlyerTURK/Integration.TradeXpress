using System.Threading.Tasks;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>Sipariş edit formu içeriği (dumb Layout) — görüntü yardımcıları N11 kod → etiket (kanal-farkında).</summary>
public partial class OrderEditLayout
{
    [Parameter, EditorRequired] public OrderDto Model { get; set; } = default!;

    private OrderItemsDrill? _itemsDrill;

    /// <summary>OrderEditHost'un edit toolbar'ındaki toplu Kabul Et/Reddet aksiyonundan SONRA kalem drill'ini
    /// tazeler (drill kendi verisini kendi yükler — burada veri kopyalanmaz, yalnız tetiklenir).</summary>
    public async Task ReloadItemsAsync()
    {
        if (_itemsDrill is not null)
        {
            await _itemsDrill.ReloadAsync();
        }
    }

    private string ChannelText(SalesChannelType type, string? code)
        => !string.IsNullOrWhiteSpace(code) ? code! : ChannelTypeLabel(type);

    private string ChannelTypeLabel(SalesChannelType type) => type switch
    {
        SalesChannelType.TrTrendyol => L["SalesChannelType:TrTrendyol"],
        SalesChannelType.Etsy => L["SalesChannelType:Etsy"],
        _ => L["SalesChannelType:TrN11"],
    };

    private string StatusLabel(OrderStatus status) => L[$"Enum:OrderStatus:{status}"];

    private string? RemoteOrderStatusText(OrderDto order)
        => order.ChannelType == SalesChannelType.TrN11 ? N11OrderStatusCatalog.OrderStatusLabel(order.RemoteStatus) : order.RemoteStatus;

    private string? PaymentTypeText(string? code) => N11OrderStatusCatalog.PaymentTypeLabel(code);

    private string InvoiceTypeLabel(int? type) => type switch
    {
        1 => L["Enum:N11InvoiceType:Individual"],
        2 => L["Enum:N11InvoiceType:Corporate"],
        _ => type?.ToString() ?? string.Empty,
    };

    private static string Money(decimal? value) => value.HasValue ? value.Value.ToString("N2") : string.Empty;

    // N11 kargo ücretini AYRI VERMİYOR (serviceItem.price hatalı 0 dönüyor — canlı doğrulandı order 136043971) →
    // tahsil edilecek (dueAmount) − satıcı fatura tutarı (sellerInvoiceAmount) farkından TÜRETİLİR (pozitifse).
    private static decimal? DerivedShipping(OrderDetailTotalsDto totals)
    {
        if (totals.DueAmount is not { } due || totals.SellerInvoiceAmount is not { } invoice)
        {
            return null;
        }

        var shipping = due - invoice;
        return shipping > 0m ? shipping : null;
    }
}
