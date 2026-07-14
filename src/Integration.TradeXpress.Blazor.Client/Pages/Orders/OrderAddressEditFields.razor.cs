using Integration.TradeXpress.Orders;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>Sipariş adresi (fatura/teslimat) editable alanları — DRY paylaşım (OrderDetailView'de iki kez kullanılır).</summary>
public partial class OrderAddressEditFields
{
    [Parameter, EditorRequired] public OrderEditAddressDto Model { get; set; } = default!;
}
