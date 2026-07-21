using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.VariantTemplates;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>JENERİK nitelik paneli (Nitelik → Değer iç içe drill) — herhangi bir entity'nin varyant eksenleri.
/// Sahip form bir DxTabPage içine koyar. Graf save sahip AppService'te (EntityVariantGraphService.SaveGraph).
/// Toolbar "Katalogtan Uygula" ile VariantTemplate demeti seçilip gruplar+değerler bu grafa MERGE edilir
/// (mevcut nitelik/değerler KORUNUR — ad/değer bazında dedup).</summary>
public partial class EntityAttributesPanel
{
    [Parameter, EditorRequired] public List<EntityAttributeGraphDto> Attributes { get; set; } = default!;

    [Inject] private IVariantTemplateAppService VariantTemplateAppService { get; set; } = default!;

    // "Katalogtan Uygula" şablon seçici popup durumu.
    private bool _templatePopupVisible;
    private IReadOnlyList<VariantTemplateListDto> _templates = Array.Empty<VariantTemplateListDto>();
    private Guid? _selectedTemplateId;

    // Nitelik gridi toolbar'ına eklenen custom aksiyon — varyant tanım katalogundan (demet) uygula.
    private IReadOnlyList<CrudToolbarAction> TemplateActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 120,   // Ekle(?)/Sil(100) ile Arama arası
            Text = L["ApplyFromCatalog"].Value,
            Tooltip = L["ApplyFromCatalog"].Value,
            IconCssClass = TradeXpressIcons.VariantTemplate,
            OnClick = OpenTemplatePickerAsync,
        },
    };

    // Popup'ı aç → aktif şablonları yükle (picker combo'su).
    private async Task OpenTemplatePickerAsync()
    {
        _selectedTemplateId = null;
        _templates = await VariantTemplateAppService.GetPickerListAsync();
        _templatePopupVisible = true;
        StateHasChanged();
    }

    // Seçili şablonun tam grafını çek → gruplar+değerleri mevcut grafa MERGE et → dirty bildir + kapat.
    private async Task ApplyTemplateAsync()
    {
        if (_selectedTemplateId is not { } id)
        {
            return;
        }

        var template = await VariantTemplateAppService.GetAsync(id);
        MergeTemplate(template);
        _templatePopupVisible = false;
        await NotifyChangedAsync();
        StateHasChanged();
    }

    // Şablon gruplarını mevcut grafa ekler: aynı ADLI grup varsa değerleri ona kat (dedup), yoksa yeni grup ekle
    // (MaxAttributesPerEntity sınırına saygı). Mevcut nitelik/değerler SİLİNMEZ — yalnız eksikler eklenir.
    private void MergeTemplate(VariantTemplateGetDto template)
    {
        foreach (var group in template.Attributes.OrderBy(x => x.DisplayOrder))
        {
            var name = (group.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var existing = Attributes.FirstOrDefault(a =>
                !a.IsDeleted && string.Equals(a.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                if (Attributes.Count(a => !a.IsDeleted) >= EntityVariantConsts.MaxAttributesPerEntity)
                {
                    continue;
                }

                var attribute = new EntityAttributeGraphDto { Name = name, DisplayOrder = group.DisplayOrder };
                foreach (var value in group.Values.OrderBy(x => x.DisplayOrder))
                {
                    AddValueIfMissing(attribute, value.Value, value.DisplayOrder);
                }

                Attributes.Add(attribute);
            }
            else
            {
                foreach (var value in group.Values.OrderBy(x => x.DisplayOrder))
                {
                    AddValueIfMissing(existing, value.Value, value.DisplayOrder);
                }
            }
        }
    }

    private static void AddValueIfMissing(EntityAttributeGraphDto attribute, string? value, int displayOrder)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        var duplicate = attribute.Values.Any(x =>
            !x.IsDeleted && string.Equals(x.Value.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (!duplicate)
        {
            attribute.Values.Add(new EntityAttributeValueGraphDto { Value = normalized, DisplayOrder = displayOrder });
        }
    }

    /// <summary>Nitelik VEYA değeri eklenir/düzenlenir/silinirse tetiklenir — sahip layout bunu host'un otomatik varyant
    /// senkronuna (regen + merge) bağlar. Böylece varyantlar "Oluştur" butonuna bağlı kalmadan anında güncellenir.</summary>
    [Parameter] public EventCallback OnAttributesChanged { get; set; }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<EntityAttributeGraphDto>? _attributeDrill;
    private DrillList<EntityAttributeValueGraphDto>? _valueDrill;

    // Nitelik/değer add/edit/delete → forma dirty bildir (EditChanged) + otomatik varyant senkronunu tetikle (OnAttributesChanged).
    private async Task NotifyChangedAsync()
    {
        EditChanged?.Invoke();
        await OnAttributesChanged.InvokeAsync();
    }

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
