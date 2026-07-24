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
            ToleranceType.Amount, 0m, 1, new List<SubstitutionPlanCombination> { combination }));

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
            ToleranceType.Amount, 0m, 1, new List<SubstitutionPlanCombination> { combination }));

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

    /// <summary>Yalnız VARYANTLA ayrışan kombinasyonlar metinde de VARYANT KODUYLA ayrışır (Rank son-ekiyle DEĞİL).
    /// Kanal eşleştirmesi normalize METİN bazlı olduğundan, ayrımın rank'a bırakılması kimliği canlı veriye
    /// (maliyet/stok) bağlıyordu — bkz. bir sonraki test.</summary>
    [Fact]
    public void Value_texts_of_variant_only_differing_combinations_are_disambiguated_by_variant_code()
    {
        var combinations = new List<SubstitutionPlanCombination>
        {
            new(1, 3, new List<SubstitutionPlanCombinationLine> { new(MetalId, "GR5", 5m, 2, MainVariantId, "GR5-MAIN") }),
            new(2, 2, new List<SubstitutionPlanCombinationLine> { new(MetalId, "GR5", 5m, 2, AltVariantId, "GR5-ESKI") }),
        };

        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Amount, 0m, 2, combinations));

        plan.Items.Select(i => i.ValueText).ShouldBe(new[]
        {
            "2×GR5 5gr (GR5-MAIN)",
            "2×GR5 5gr (GR5-ESKI)",
        });
        plan.Items.Select(i => i.PlanKey).Distinct().Count().ShouldBe(2);   // anahtarlar varyantla ayrışır
    }

    /// <summary>KİMLİK KARARLILIĞI — bir kombinasyonun değer metni RANK'tan bağımsızdır. Regresyon koruması
    /// (kod-inceleme bulgusu): ayrım "#{Rank}" son-ekine bırakıldığında, ranklar takas olunca (bir işçilik
    /// düzenlemesi ya da stok değişimi yeter) metin çoklu-kümesi AYNI kalıyor ama artık KARŞI kombinasyonlara
    /// işaret ediyordu; kanal diff'i (normalize metin bazlı) sıfır fark raporlarken canlı bir pazaryeri
    /// seçeneğinin reçetesi/stoğu diğer kombinasyonla eziliyordu — aynı SKU, aynı sipariş geçmişi, FARKLI mal.</summary>
    [Fact]
    public void Value_text_identity_survives_a_rank_swap()
    {
        SubstitutionPlanCombinationLine Main(int count) => new(MetalId, "GR5", 5m, count, MainVariantId, "GR5-MAIN");
        SubstitutionPlanCombinationLine Alt(int count) => new(MetalId, "GR5", 5m, count, AltVariantId, "GR5-ESKI");

        var before = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Amount, 0m, 2, new List<SubstitutionPlanCombination>
            {
                new(1, 3, new List<SubstitutionPlanCombinationLine> { Main(2) }),
                new(2, 2, new List<SubstitutionPlanCombinationLine> { Alt(2) }),
            }));

        // Ranklar TAKAS oldu (ESKİ varyant artık daha ucuz/bol) — bileşimler aynı.
        var after = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Amount, 0m, 2, new List<SubstitutionPlanCombination>
            {
                new(1, 2, new List<SubstitutionPlanCombinationLine> { Alt(2) }),
                new(2, 3, new List<SubstitutionPlanCombinationLine> { Main(2) }),
            }));

        string TextOfKey(SubstitutionStockItemPlan plan, string planKey) =>
            plan.Items.Single(i => i.PlanKey == planKey).ValueText;

        var mainKey = before.Items.Single(i => i.ValueText.Contains("GR5-MAIN")).PlanKey;
        var altKey = before.Items.Single(i => i.ValueText.Contains("GR5-ESKI")).PlanKey;

        // Aynı PlanKey → takas SONRASI da AYNI metin (kanal değer kimliği kaymaz).
        TextOfKey(after, mainKey).ShouldBe(TextOfKey(before, mainKey));
        TextOfKey(after, altKey).ShouldBe(TextOfKey(before, altKey));
    }
}
