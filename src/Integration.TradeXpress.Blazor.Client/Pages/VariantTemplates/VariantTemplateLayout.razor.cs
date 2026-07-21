using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.VariantTemplates;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.VariantTemplates;

/// <summary>VariantTemplate dumb layout code-behind — Model bağlama + iç içe grup/değer drill referansları +
/// otomatik Sıra No + dirty cascade. Plain in-memory graf (save'de AppService tüm listeyi replace eder).</summary>
public partial class VariantTemplateLayout
{
    [Parameter, EditorRequired] public VariantTemplateGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<VariantTemplateAttributeDto>? _attributeDrill;
    private DrillList<VariantTemplateAttributeValueDto>? _valueDrill;

    // Yeni grup eklenince Sıra No OTOMATİK artar (max + 1; boşsa 1).
    private int NextAttributeOrder()
    {
        return Model.Attributes.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Yeni değer eklenince Sıra No OTOMATİK artar (grup içi max + 1; boşsa 1).
    private static int NextValueOrder(VariantTemplateAttributeDto attribute)
    {
        return attribute.Values.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }
}
