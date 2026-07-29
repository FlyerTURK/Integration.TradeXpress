using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.VariantTemplates;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Varyant şablonunu (katalog demeti) bir entity'nin nitelik grafına KATAR — saf, DB'siz.
///
/// <para><b>Neden ayrı sınıf:</b> şablon uygulaması iki yüzeyden tetikleniyor — nitelik popup'ındaki
/// "Katalogtan Uygula" ve varyant panelindeki hızlı seçim combo'su. Mantık iki yerde ayrı yazılsaydı biri
/// güncellenip diğeri unutulur, aynı şablon iki yüzeyde farklı sonuç verirdi (2026-07-27).</para>
///
/// <para><b>Katma kuralı:</b> mevcut nitelik/değerler SİLİNMEZ — yalnız eksikler eklenir. Aynı ADLI grup varsa
/// değerleri ona katılır (ad/değer bazında, büyük-küçük harf duyarsız tekilleştirme). Entity başına nitelik
/// tavanı aşılmaz.</para>
/// </summary>
public static class VariantTemplateMerger
{
    public static void Merge(List<EntityAttributeGraphDto> attributes, VariantTemplateGetDto template)
    {
        foreach (var group in template.Attributes.OrderBy(x => x.DisplayOrder))
        {
            var name = (group.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var existing = attributes.FirstOrDefault(a =>
                !a.IsDeleted && string.Equals(a.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                if (attributes.Count(a => !a.IsDeleted) >= EntityVariantConsts.MaxAttributesPerEntity)
                {
                    continue;
                }

                var attribute = new EntityAttributeGraphDto { Name = name, DisplayOrder = group.DisplayOrder };
                foreach (var value in group.Values.OrderBy(x => x.DisplayOrder))
                {
                    AddValueIfMissing(attribute, value.Value, value.DisplayOrder);
                }

                attributes.Add(attribute);
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
}
