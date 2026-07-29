using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.Orders;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

public partial class OrderItemsCell
{
    /// <summary>Hücrede kaç kalem satırı çizilir — fazlası "+N kalem daha" ile özetlenir.
    ///
    /// <para>Sayı keyfi değil: canlı veride sipariş başına azami 3 kalem var ve siparişlerin çoğu tek kalemli,
    /// yani bugünkü verinin tamamı kırpılmadan sığıyor. Tavan yine de sabit — 20 kalemlik bir sipariş geldiğinde
    /// satır yüksekliği patlamasın.</para></summary>
    private const int MaxVisibleItems = 3;

    [Parameter] public List<OrderItemListDto>? Items { get; set; }

    private IReadOnlyList<OrderItemListDto> _visible = Array.Empty<OrderItemListDto>();
    private int _overflowCount;
    private string? _tooltip;

    private bool HasItems
    {
        get
        {
            return _visible.Count > 0;
        }
    }

    private IReadOnlyList<OrderItemListDto> VisibleItems
    {
        get
        {
            return _visible;
        }
    }

    private int OverflowCount
    {
        get
        {
            return _overflowCount;
        }
    }

    private string? TooltipText
    {
        get
        {
            return _tooltip;
        }
    }

    private string MoreText
    {
        get
        {
            return L["Order:Items:More", _overflowCount];
        }
    }

    /// <summary>Hücre şablonu her render'da yeniden çizilir; görünen küme ile tooltip metnini parametre
    /// değişiminde BİR KEZ hesaplayıp saklıyoruz (satır başına yeniden kurmak boşuna ayırma olurdu).</summary>
    protected override void OnParametersSet()
    {
        if (Items is not { Count: > 0 })
        {
            _visible = Array.Empty<OrderItemListDto>();
            _overflowCount = 0;
            _tooltip = null;
            return;
        }

        _visible = Items.Take(MaxVisibleItems).ToList();
        _overflowCount = Math.Max(0, Items.Count - MaxVisibleItems);

        // Tooltip TÜM kalemleri taşır — hücrede kırpılan bilgi hover'da tam görünür.
        _tooltip = string.Join(Environment.NewLine, Items.Select(BuildTooltipLine));
    }

    private string BuildTooltipLine(OrderItemListDto item)
    {
        var line = $"{QuantityText(item)} {item.ProductNameSnapshot}";
        return string.IsNullOrWhiteSpace(item.StockCode) ? line : $"{line} · {item.StockCode}";
    }

    /// <summary>Adet gösterimi — miktar ondalıklı tutulduğu için gereksiz sıfırlar atılır (1,000 → "1×").</summary>
    private static string QuantityText(OrderItemListDto item)
    {
        return item.Quantity.ToString("0.###", CultureInfo.CurrentCulture) + "×";
    }
}
