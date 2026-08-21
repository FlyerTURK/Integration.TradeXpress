using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Services;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.RecipeTemplates;

/// <summary>
/// Reçete şablonu dumb layout code-behind — satır drill'i, enum combo kaynakları, otomatik sıra.
///
/// <para>UI bu sürümde HİZMET satırlarına odaklanır (paketleme/kargo/sigorta/işçilik — Hakan'ın "orta reçete"
/// tanımı). Veri modeli yarı mamul (katalog emtiası) satırını da taşır; onun düzenleme formu ürün reçete
/// paneliyle aynı zenginlikte lookup zinciri gerektirdiğinden ayrı bir adımda eklenecek.</para>
/// </summary>
public partial class RecipeTemplateLayout
{
    [Parameter, EditorRequired] public RecipeTemplateGetDto Model { get; set; } = default!;

    /// <summary>Hizmet katalogu — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<ServiceListDto> Services { get; set; } = Array.Empty<ServiceListDto>();

    /// <summary>Para birimleri — yalnız "sabit tutar ekle" satırlarında kullanılır.</summary>
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<RecipeTemplateLineDto>? _lineDrill;

    private List<OperationItem> _operationItems = new();
    private List<SideCostKindItem> _sideCostItems = new();

    protected override void OnInitialized()
    {
        // Şablon satırında YALNIZ bu üç işlem anlamlıdır: Multiply (çarpan) reçetede özel bir kullanım olup
        // şablon bağlamında karşılığı yok — göstermek kullanıcıyı yanıltırdı.
        _operationItems = new List<OperationItem>
        {
            new(RecipeDerivedOperation.Add, L["Enum:RecipeDerivedOperation:Add"].Value),
            new(RecipeDerivedOperation.Percent, L["Enum:RecipeDerivedOperation:Percent"].Value),
            new(RecipeDerivedOperation.GrossUp, L["Enum:RecipeDerivedOperation:GrossUp"].Value),
        };

        _sideCostItems = Enum.GetValues<SideCostKind>()
            .Select(k => new SideCostKindItem(k, L[$"SideCost:Kind:{k}"].Value))
            .ToList();
    }

    private void OnLineServiceChanged(RecipeTemplateLineDto line, Guid? serviceId)
    {
        line.CommodityId = serviceId;
        EditChanged?.Invoke();
    }

    private void OnLineCurrencyChanged(RecipeTemplateLineDto line, Guid? currencyUnitId)
    {
        line.PayUnitId = currencyUnitId;
        EditChanged?.Invoke();
    }

    /// <summary>Satır başlığı — hizmet adı varsa o, yoksa gider türü, o da yoksa açıklama. Grid'de satırın
    /// ne olduğu tek bakışta anlaşılsın (kod alanı olmadığından başlık türetilir).</summary>
    private string LineTitle(RecipeTemplateLineDto line)
    {
        if (line.CommodityId is { } serviceId)
        {
            var service = Services.FirstOrDefault(s => s.Id == serviceId);
            if (service is not null)
            {
                return service.Name;
            }
        }

        if (line.SideCostKind is { } kind)
        {
            return L[$"SideCost:Kind:{kind}"].Value;
        }

        return string.IsNullOrWhiteSpace(line.Description) ? L["RecipeTemplate:Line"].Value : line.Description!;
    }

    private string OperationText(RecipeTemplateLineDto line)
    {
        return line.DerivedOperation is { } operation ? L[$"Enum:RecipeDerivedOperation:{operation}"].Value : string.Empty;
    }

    private string SideCostText(RecipeTemplateLineDto line)
    {
        return line.SideCostKind is { } kind ? L[$"SideCost:Kind:{kind}"].Value : string.Empty;
    }

    // Yeni satır eklenince sıra OTOMATİK artar (max + 1; boşsa 1).
    private int NextLineOrder()
    {
        return Model.Lines.Select(x => x.LineOrder).DefaultIfEmpty(0).Max() + 1;
    }

    /// <summary>Türev işlem combo satırı.</summary>
    public sealed record OperationItem(RecipeDerivedOperation? Value, string Text);

    /// <summary>Yan-maliyet türü combo satırı.</summary>
    public sealed record SideCostKindItem(SideCostKind? Value, string Text);
}
