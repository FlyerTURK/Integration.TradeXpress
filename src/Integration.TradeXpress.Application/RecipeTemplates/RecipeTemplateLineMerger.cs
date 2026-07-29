using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Gelen şablon satırlarını şablona MERGE eder — kimlikler korunarak (<c>ProductCategoryAttributeMerger</c> ile
/// aynı semantik ve aynı gerekçe: satırı yeniden yaratmak düzenleme geçmişini ve ileride kurulacak referansları
/// koparırdı). AppService'ten AYRI ve <c>static</c>: kural DB'siz sınanabilsin.
/// </summary>
public static class RecipeTemplateLineMerger
{
    public static void Apply(RecipeTemplate template, IReadOnlyList<RecipeTemplateLineDto>? lines)
    {
        var incoming = (lines ?? Array.Empty<RecipeTemplateLineDto>())
            .OrderBy(l => l.LineOrder)
            .ToList();

        template.RemoveLinesExcept(incoming.Select(l => l.Id).Where(id => id != Guid.Empty).ToHashSet());

        // Sıra 0..n-1 yeniden numaralanır: hizmet satırları "üstümdeki her şey" üzerinden hesapladığından
        // pozisyon anlam taşır; boşluklu/çakışan sıra numarası uygulamada belirsizlik üretirdi.
        for (var index = 0; index < incoming.Count; index++)
        {
            var dto = incoming[index];
            var line = dto.Id == Guid.Empty ? null : template.FindLine(dto.Id);

            // TÜR DEĞİŞİMİ = YENİ SATIR: ComponentType satırın kimliğinin parçasıdır (reçete satırında da
            // set-once'tır) ve alan setleri ayrışır. Var olan satırın türünü yerinde değiştirmek, karşı türün
            // artık alanlarını taşıyan melez bir satır bırakırdı. Bu yüzden tür değişince eski satır düşer,
            // yerine temiz bir satır açılır.
            if (line is not null && line.ComponentType != dto.ComponentType)
            {
                template.RemoveLine(line);
                line = null;
            }

            if (line is null)
            {
                // Id gelmiş ama bu şablonda yoksa: başka şablonun satırını buraya ÇEKMEK yerine yeni satır açılır.
                line = template.AddLine(dto.ComponentType, index);
            }
            else
            {
                line.SetOrder(index);
            }

            if (dto.ComponentType == RecipeComponentType.CatalogCommodity && dto.CommodityProcessType is { } family)
            {
                line.SetCatalogCommodity(
                    family,
                    dto.CommodityId,
                    dto.CommodityVariantId,
                    dto.Quantity,
                    dto.Amount,
                    dto.Factor,
                    dto.ValuationUnitId,
                    dto.PaymentType,
                    dto.PayFactor,
                    dto.PayUnitId);
            }
            else
            {
                line.SetService(
                    dto.CommodityId,
                    dto.DerivedOperation ?? RecipeDerivedOperation.Percent,
                    dto.DerivedOperand,
                    dto.PayUnitId,
                    dto.SideCostKind);
            }

            line.SetDescription(dto.Description);
        }
    }
}
