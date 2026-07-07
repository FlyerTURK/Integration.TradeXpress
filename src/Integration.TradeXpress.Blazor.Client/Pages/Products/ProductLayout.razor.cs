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
using Integration.TradeXpress.Services;
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
    [Parameter] public IReadOnlyList<ServiceListDto> Services { get; set; } = Array.Empty<ServiceListDto>();
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

    private DrillList<ProductVariantGraphDto>? _variantDrill;
    private DrillList<ProductAttributeGraphDto>? _attributeDrill;
    private DrillList<ProductAttributeValueGraphDto>? _valueDrill;

    /// <summary>Görsel URL'leri ↔ çok-satırlı metin köprüsü (her satır bir URL; sıra korunur). DxMemo @bind-Text
    /// ValueExpression sağlar → form dirty otomatik tetiklenir. Sunucu ayrıca trim + boş-satır ayıklar (SetImageUrls).</summary>
    private string ImageUrlsText
    {
        get => string.Join(Environment.NewLine, Model.ImageUrls);
        set => Model.ImageUrls = (value ?? string.Empty)
            .Split('\n')
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .ToList();
    }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    /// <summary>Reçete değişince CANLI maliyet — host yapar (persistsiz hesap, varyant bazında); tam kayıt gerekmez.</summary>
    [Parameter] public Func<ProductVariantGraphDto, Task>? OnRecipeChanged { get; set; }

    /// <summary>Reçete satırı eklenince/değişince/silinince: önce CANLI maliyet (host), sonra form dirty.</summary>
    private async Task HandleRecipeChangedAsync(ProductVariantGraphDto variant)
    {
        if (OnRecipeChanged is not null)
        {
            await OnRecipeChanged(variant);
        }

        EditChanged?.Invoke();
    }

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
