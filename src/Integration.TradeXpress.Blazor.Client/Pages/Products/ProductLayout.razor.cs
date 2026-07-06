using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Stones;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product dumb layout code-behind — Model bağlama + varyant drill referansı + dirty cascade.</summary>
public partial class ProductLayout
{
    [Parameter, EditorRequired] public ProductGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    // Reçete drill'inin katalog lookup verisi — host yükler (DUMB layout servis çağırmaz).
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();
    [Parameter] public IReadOnlyList<ScrapListDto> Scraps { get; set; } = Array.Empty<ScrapListDto>();
    [Parameter] public IReadOnlyList<FutureListDto> Futures { get; set; } = Array.Empty<FutureListDto>();
    [Parameter] public IReadOnlyList<JewelryListDto> Jewelries { get; set; } = Array.Empty<JewelryListDto>();
    [Parameter] public IReadOnlyList<StoneListDto> Stones { get; set; } = Array.Empty<StoneListDto>();
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

    private DrillList<ProductVariantGraphDto>? _variantDrill;
    private DrillList<ProductAttributeGraphDto>? _attributeDrill;
    private DrillList<ProductAttributeValueGraphDto>? _valueDrill;

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    /// <summary>"Varyantları Oluştur" tıklandı — layout DUMB kalır (servis çağırmaz): işi host yapar
    /// (ProductAppService.GenerateVariantsAsync → Model.Variants). Sonrasında form dirty işaretlenir.</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }

    private async Task GenerateVariantsClickedAsync()
    {
        await OnGenerateVariants.InvokeAsync();
        EditChanged?.Invoke();
    }

    // Yeni nitelik/değer eklenince Sıra No OTOMATİK artar (silinmemişlerin max'ı + 1; boşsa 1).
    private static int NextOrder(IEnumerable<ProductAttributeGraphDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private static int NextOrder(IEnumerable<ProductAttributeValueGraphDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }
}
