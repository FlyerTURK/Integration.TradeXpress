using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Planlayıcının VARYANT boyutu (Dilim-2) — <see cref="SubstitutionStockItemPlanner"/>:
/// plan reçete satırları seçilen varyantı taşır; PlanKey varyantlı satırda "{MetalId}:{VariantId}x{Count}"
/// biçimiyle ayrışır (varyantsız satır ESKİ biçimi korur — anahtar statükosu); yalnız varyantla ayrışan
/// kombinasyonların değer metinleri Rank son-ekiyle benzersiz kalır (gramaj metni müşteriye dönük sözleşme —
/// varyant UI kolonunda gösterilir, metne sızmaz).
/// </summary>
public class SubstitutionPlannerVariantTests
{
    private static readonly Guid MetalId = Guid.NewGuid();
    private static readonly Guid MainVariantId = Guid.NewGuid();
    private static readonly Guid AltVariantId = Guid.NewGuid();

    [Fact]
    public void Plan_recipe_lines_carry_selected_variant_id_and_code()
    {
        var combination = new SubstitutionPlanCombination(1, 2, new List<SubstitutionPlanCombinationLine>
        {
            new(MetalId, "GR5", 5m, 2, AltVariantId, "GR5-ESKI"),
        });

        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Gram, 0m, 1, new List<SubstitutionPlanCombination> { combination }));

        var line = plan.Items[0].RecipeLines.ShouldHaveSingleItem();
        line.ShouldBe(new SubstitutionPlanRecipeLine(MetalId, 2, 5m, 10m, AltVariantId, "GR5-ESKI"));
    }

    [Fact]
    public void Plan_key_disambiguates_variants_of_the_same_metal_and_keeps_legacy_format_for_variantless_lines()
    {
        // Aynı madenin İKİ varyantı aynı kombinasyonda + varyantsız (legacy) ikinci maden.
        var legacyMetalId = Guid.NewGuid();
        var combination = new SubstitutionPlanCombination(1, 1, new List<SubstitutionPlanCombinationLine>
        {
            new(MetalId, "GR5", 5m, 1, MainVariantId, "GR5-MAIN"),
            new(MetalId, "GR5", 5m, 2, AltVariantId, "GR5-ESKI"),
            new(legacyMetalId, "GR1", 1m, 2),
        });

        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Gram, 0m, 1, new List<SubstitutionPlanCombination> { combination }));

        var expectedSegments = new[]
            {
                (MetalId, (Guid?)MainVariantId, 1),
                (MetalId, (Guid?)AltVariantId, 2),
                (legacyMetalId, (Guid?)null, 2),
            }
            .OrderBy(s => s.Item1)
            .ThenBy(s => s.Item2 ?? Guid.Empty)
            .Select(s => s.Item2 is { } variantId
                ? $"{s.Item1}:{variantId}x{s.Item3}"
                : $"{s.Item1}x{s.Item3}");

        plan.Items[0].PlanKey.ShouldBe(string.Join('|', expectedSegments));
    }

    [Fact]
    public void Value_texts_of_variant_only_differing_combinations_stay_unique_via_rank_suffix()
    {
        // Aynı gramaj bileşimi (2×5gr) yalnız VARYANTLA ayrışıyor → gramaj VE maden-adı metinleri çakışır;
        // benzersizlik Rank son-ekiyle korunur (kanal özellik değerleri metin bazında benzersiz kalmalı).
        var combinations = new List<SubstitutionPlanCombination>
        {
            new(1, 3, new List<SubstitutionPlanCombinationLine> { new(MetalId, "GR5", 5m, 2, MainVariantId, "GR5-MAIN") }),
            new(2, 2, new List<SubstitutionPlanCombinationLine> { new(MetalId, "GR5", 5m, 2, AltVariantId, "GR5-ESKI") }),
        };

        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Gram, 0m, 2, combinations));

        plan.Items.Select(i => i.ValueText).ShouldBe(new[]
        {
            "2×GR5 5gr",
            "2×GR5 5gr #2",
        });
        plan.Items.Select(i => i.PlanKey).Distinct().Count().ShouldBe(2);   // anahtarlar varyantla ayrışır
    }
}
