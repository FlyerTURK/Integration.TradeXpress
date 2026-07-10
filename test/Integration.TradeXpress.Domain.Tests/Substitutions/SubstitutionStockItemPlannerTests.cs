using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Saf köprü planlayıcısı karakterizasyonu — <see cref="SubstitutionStockItemPlanner"/> (DB'siz/DI'sız;
/// SubstitutionSolver test deseni). SSOT: konsept 12gr örneği — 6 başarılıdan TopN=3 → 3 plan kaydı;
/// değer metinleri, reçete satırları, paket sayıları ve Rank1 = ANA varyant kilitlenir. Tolerans ticari
/// bildirim metni (konsept madde 3) de burada pinlenir — push entegrasyonu ayrı dilim, metin üretimi bu dilimde.
/// </summary>
public class SubstitutionStockItemPlannerTests
{
    // Kullanıcı örneği madenleri (SubstitutionSolverTests ile aynı evren; 20gr ön-filtrede elenir).
    private static readonly Guid Gr10Id = Guid.NewGuid();
    private static readonly Guid Gr5Id = Guid.NewGuid();
    private static readonly Guid Gr1Id = Guid.NewGuid();
    private static readonly Guid Gr25Id = Guid.NewGuid();

    /// <summary>12gr örneğinin BAŞARILI kombinasyonlarını GERÇEK solver'dan üretir (tek motor zinciri —
    /// planlayıcı testi solver çıktısının birebir devamıdır, elle kurgulanmış paralel veri yok).</summary>
    private static List<SubstitutionPlanCombination> SolveUserExample()
    {
        var commodities = new List<SubstitutionCommodity>
        {
            new(Gr10Id, "GR10", 10m, 3, 40m),
            new(Gr5Id, "GR5", 5m, 7, 21m),
            new(Gr1Id, "GR1", 1m, 20, 4.5m),
            new(Gr25Id, "GR2.5", 2.5m, 3, 11m),
        };
        var nameById = commodities.ToDictionary(c => c.Id, c => c.Code);
        var weightById = commodities.ToDictionary(c => c.Id, c => c.PieceWeight);

        var solved = SubstitutionSolver.Solve(new SubstitutionSolverInput(12m, ToleranceType.Gram, 0m, commodities));
        return solved.All
            .Where(c => c.Success)
            .Select(c => new SubstitutionPlanCombination(
                c.Rank!.Value,
                c.PackageCount,
                c.Lines.Select(l => new SubstitutionPlanCombinationLine(
                    l.CommodityId, nameById[l.CommodityId], weightById[l.CommodityId], l.Count)).ToList()))
            .ToList();
    }

    private static SubstitutionStockItemPlan BuildUserExamplePlan(int topN)
    {
        return SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Gram, 0m, topN, SolveUserExample()));
    }

    // ── 12gr örneği: 6 başarılıdan TopN=3 → 3 plan kaydı ────────────────────────────────────────────

    [Fact]
    public void User_example_topn3_produces_three_plan_items_in_rank_order_with_value_texts_and_packages()
    {
        var plan = BuildUserExamplePlan(topN: 3);

        plan.Items.Count.ShouldBe(3);

        // Skor sırası (maliyet küçük → parça küçük → paket büyük): 49 < 51 < 52.
        plan.Items.Select(i => i.Rank).ShouldBe(new[] { 1, 2, 3 });
        plan.Items.Select(i => i.ValueText).ShouldBe(new[]
        {
            "1×10gr + 2×1gr",
            "2×5gr + 2×1gr",
            "1×5gr + 2×1gr + 2×2,5gr",   // TR ondalık virgül (müşteriye dönük metin)
        });
        plan.Items.Select(i => i.PackageCount).ShouldBe(new[] { 3, 3, 1 });

        // Rank1 = ANA varyant; diğerleri değil. Görsel-atama noktası şimdilik hep boş (konsept AI notu).
        plan.Items[0].IsPrimary.ShouldBeTrue();
        plan.Items.Skip(1).ShouldAllBe(i => !i.IsPrimary);
        plan.Items.ShouldAllBe(i => i.ImageUrl == null);

        // Tolerans 0 → ticari bildirim yok.
        plan.ToleranceNotice.ShouldBeNull();
    }

    [Fact]
    public void User_example_plan_recipe_lines_carry_metal_count_and_amount()
    {
        var plan = BuildUserExamplePlan(topN: 3);

        // Rank1 reçetesi: 1×10gr + 2×1gr → 2 metal satırı, Amount = adet × parça gramı.
        var best = plan.Items[0];
        best.RecipeLines.Count.ShouldBe(2);
        best.RecipeLines[0].ShouldBe(new SubstitutionPlanRecipeLine(Gr10Id, 1, 10m, 10m));
        best.RecipeLines[1].ShouldBe(new SubstitutionPlanRecipeLine(Gr1Id, 2, 1m, 2m));

        // Rank3 reçetesi: 1×5 + 2×1 + 2×2.5 (tüketim önceliği sırası korunur).
        var third = plan.Items[2];
        third.RecipeLines.Select(l => (l.MetalId, l.Count, l.Amount)).ShouldBe(new[]
        {
            (Gr5Id, 1, 5m),
            (Gr1Id, 2, 2m),
            (Gr25Id, 2, 5m),
        });
    }

    [Fact]
    public void Plan_keys_are_metal_id_sorted_and_order_independent()
    {
        var plan = BuildUserExamplePlan(topN: 1);

        // PlanKey = "{MetalId}x{Count}|..." MetalId ARTAN sıralı — satır giriş sırasından bağımsız deterministik.
        var expectedKey = string.Join('|', new[] { (Gr10Id, 1), (Gr1Id, 2) }
            .OrderBy(p => p.Item1)
            .Select(p => $"{p.Item1}x{p.Item2}"));
        plan.Items[0].PlanKey.ShouldBe(expectedKey);
    }

    [Fact]
    public void Topn_larger_than_success_count_returns_all_and_nonpositive_topn_means_no_limit()
    {
        BuildUserExamplePlan(topN: 99).Items.Count.ShouldBe(6);
        BuildUserExamplePlan(topN: 0).Items.Count.ShouldBe(6);
    }

    // ── Guard: başarılı kombinasyon yoksa varyant kurulamaz ─────────────────────────────────────────

    [Fact]
    public void Empty_successful_list_fails_fast_with_no_successful_combination()
    {
        var exception = Should.Throw<BusinessException>(() =>
            SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
                ToleranceType.Gram, 0m, 3, new List<SubstitutionPlanCombination>())));
        exception.Code.ShouldBe("TradeXpress:Substitution:NoSuccessfulCombination");
    }

    [Fact]
    public void Unranked_or_lineless_combination_is_rejected_as_invalid_feed()
    {
        // Planlayıcıya yalnız BAŞARILI (Rank'lı + satırlı) kombinasyon girer — besleme hatası sessiz geçilmez.
        var lineless = new SubstitutionPlanCombination(1, 1, new List<SubstitutionPlanCombinationLine>());
        Should.Throw<BusinessException>(() =>
                SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
                    ToleranceType.Gram, 0m, 3, new List<SubstitutionPlanCombination> { lineless })))
            .Code.ShouldBe("TradeXpress:Substitution:NoSuccessfulCombination");
    }

    // ── Değer metni benzersizliği (aynı gramajlı farklı madenler) ───────────────────────────────────

    [Fact]
    public void Duplicate_value_texts_are_disambiguated_with_metal_names()
    {
        // İki FARKLI maden aynı gramajda (1gr külçe / 1gr sikke) → salt gramaj metni çakışır,
        // çakışanlar maden adıyla ayrıştırılır.
        var bullionId = Guid.NewGuid();
        var coinId = Guid.NewGuid();
        var combinations = new List<SubstitutionPlanCombination>
        {
            new(1, 5, new List<SubstitutionPlanCombinationLine> { new(bullionId, "GRKULCE", 1m, 2) }),
            new(2, 4, new List<SubstitutionPlanCombinationLine> { new(coinId, "GRSIKKE", 1m, 2) }),
        };

        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.Gram, 0m, 2, combinations));

        plan.Items.Select(i => i.ValueText).ShouldBe(new[]
        {
            "2×GRKULCE 1gr",
            "2×GRSIKKE 1gr",
        });
    }

    // ── Ticari tolerans bildirimi (konsept madde 3 — metin üretimi bu dilimde) ──────────────────────

    [Fact]
    public void Tolerance_notice_texts_follow_the_binding_commercial_wording()
    {
        SubstitutionStockItemPlanner.BuildToleranceNotice(ToleranceType.PerMille, 1m)
            .ShouldBe("+/− binde 1 tolerans hakkı saklıdır");
        SubstitutionStockItemPlanner.BuildToleranceNotice(ToleranceType.PerMille, 2.5m)
            .ShouldBe("+/− binde 2,5 tolerans hakkı saklıdır");
        SubstitutionStockItemPlanner.BuildToleranceNotice(ToleranceType.Gram, 0.5m)
            .ShouldBe("+/− 0,5 gram tolerans hakkı saklıdır");
        SubstitutionStockItemPlanner.BuildToleranceNotice(ToleranceType.Gram, 0m).ShouldBeNull();
        SubstitutionStockItemPlanner.BuildToleranceNotice(ToleranceType.PerMille, 0m).ShouldBeNull();
    }

    [Fact]
    public void Plan_carries_tolerance_notice_when_group_tolerance_is_positive()
    {
        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            ToleranceType.PerMille, 1m, 3, SolveUserExample()));
        plan.ToleranceNotice.ShouldBe("+/− binde 1 tolerans hakkı saklıdır");
    }
}
