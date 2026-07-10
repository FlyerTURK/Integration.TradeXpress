using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Substitutions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Substitutions;

public partial class SubstitutionGroupLayout
{
    public SubstitutionGroupLayout()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Parameter, EditorRequired] public SubstitutionGroupGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Maden picker adayları (host'tan) — yalnız adet-hesaplı + standart gramajlı madenler.</summary>
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i (Account deseni).
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<SubstitutionGroupItemGraphDto>? _itemsDrill;

    // Yeni satır listenin SONUNA eklenir (en düşük tüketim önceliği) — mevcut sıra korunur.
    private SubstitutionGroupItemGraphDto NewItem()
    {
        var nextOrder = Model.Items
            .Where(i => !i.IsDeleted)
            .Select(i => i.DisplayOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        return new SubstitutionGroupItemGraphDto { DisplayOrder = nextOrder };
    }

    // Drill popup'ı kaydedince grid'de görünen maden kodunu seçimden tazele (MetalCode display-only).
    private Task OnItemSaved(SubstitutionGroupItemGraphDto item)
    {
        item.MetalCode = Metals.FirstOrDefault(m => m.Id == item.MetalId)?.Code ?? string.Empty;
        return Task.CompletedTask;
    }
}
