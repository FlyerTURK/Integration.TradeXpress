using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product dumb layout code-behind — Model bağlama + varyant drill referansı + dirty cascade.</summary>
public partial class ProductLayout
{
    [Parameter, EditorRequired] public ProductGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

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
