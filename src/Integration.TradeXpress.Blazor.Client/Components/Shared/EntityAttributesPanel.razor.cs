using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>JENERİK nitelik paneli (Nitelik → Değer iç içe drill) — herhangi bir entity'nin varyant eksenleri.
/// Sahip form bir DxTabPage içine koyar. Graf save sahip AppService'te (EntityVariantGraphService.SaveGraph).</summary>
public partial class EntityAttributesPanel
{
    [Parameter, EditorRequired] public List<EntityAttributeGraphDto> Attributes { get; set; } = default!;

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<EntityAttributeGraphDto>? _attributeDrill;
    private DrillList<EntityAttributeValueGraphDto>? _valueDrill;

    // Yeni nitelik/değer eklenince Sıra No OTOMATİK artar (silinmemişlerin max'ı + 1; boşsa 1).
    private int NextAttributeOrder()
    {
        return Attributes.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private static int NextValueOrder(EntityAttributeGraphDto attribute)
    {
        return attribute.Values.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }
}
