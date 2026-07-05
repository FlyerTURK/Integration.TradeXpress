using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Ürün varyantı drill edit alanları (Code/Name/Description/Status) — graf düğümüne bind.</summary>
public partial class ProductVariantEditFields
{
    [Parameter, EditorRequired] public ProductVariantGraphDto Model { get; set; } = default!;
}
