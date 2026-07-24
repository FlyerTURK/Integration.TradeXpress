using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Saf çekirdek karakterizasyonu — <see cref="SubstitutionSolver"/> (DB'siz/DI'sız; VariantCombinationEngine
/// test deseni). SSOT: .claude/research/muadil/konsept.md — kullanıcının çalışılmış 12gr örneği birebir
/// kilitlenir (6 başarılı kombinasyon + numaralandırma sırası). KIRMIZIYSA motor semantiği değişmiş demektir.
/// </summary>
public class SubstitutionSolverTests
{
    // Kullanıcı örneği emtiaları (liste sırası = tüketim önceliği; 2.5gr zor bulunduğu için SONDA).
    private static readonly Guid Gr20Id = Guid.NewGuid();
    private static readonly Guid Gr10Id = Guid.NewGuid();
    private static readonly Guid Gr5Id = Guid.NewGuid();
    private static readonly Guid Gr1Id = Guid.NewGuid();
    private static readonly Guid Gr25Id = Guid.NewGuid();

    // Örnek maliyetler (skor testi): 10gr=40, 5gr=21, 1gr=4.5, 2.5gr=11.
    private static List<SubstitutionCommodity> UserExampleCommodities() => new()
    {
        new SubstitutionCommodity(Gr20Id, "GR20", 20m, 5, 75m),     // ön-filtrede elenmeli (tek parça > 12)
        new SubstitutionCommodity(Gr10Id, "GR10", 10m, 3, 40m),
        new SubstitutionCommodity(Gr5Id, "GR5", 5m, 7, 21m),
        new SubstitutionCommodity(Gr1Id, "GR1", 1m, 20, 4.5m),
        new SubstitutionCommodity(Gr25Id, "GR2.5", 2.5m, 3, 11m),
    };

    private static SubstitutionSolverInput UserExampleInput(
        decimal requestedAmount = 12m,
        ToleranceType toleranceType = ToleranceType.Amount,
        decimal toleranceValue = 0m)
    {
        return new SubstitutionSolverInput(requestedAmount, toleranceType, toleranceValue, UserExampleCommodities());
    }

    private static string Describe(SubstitutionCombination combination)
    {
        // Okunur imza: "1x10+2x1" — satırlar girdi sırasıyla, ağırlık koduna göre.
        var names = new Dictionary<Guid, string>
        {
            [Gr20Id] = "20", [Gr10Id] = "10", [Gr5Id] = "5", [Gr1Id] = "1", [Gr25Id] = "2.5",
        };
        return string.Join("+", combination.Lines.Select(l => $"{l.Count}x{names[l.CommodityId]}"));
    }

    // ── KULLANICI ÖRNEĞİ BİREBİR (en kritik test) ────────────────────────────────────────────────────

    [Fact]
    public void User_example_12gr_produces_exactly_six_successes_in_enumeration_order()
    {
        var result = SubstitutionSolver.Solve(UserExampleInput());

        // 20gr ön-filtrede elendi (tek parçası talebi aşıyor) — hesaba hiç girmez.
        var filtered = result.FilteredOut.ShouldHaveSingleItem();
        filtered.CommodityId.ShouldBe(Gr20Id);
        filtered.Reason.ShouldBe(SubstitutionReasonCodes.PieceWeightExceedsTarget);
        result.All.SelectMany(c => c.Lines).ShouldAllBe(l => l.CommodityId != Gr20Id);

        // 6 BAŞARILI — konsept tablosundaki NUMARALANDIRMA SIRASIYLA (azalan-leksikografik tam sıralama).
        var successes = result.All.Where(c => c.Success).ToList();
        successes.Select(Describe).ShouldBe(new[]
        {
            "1x10+2x1",
            "2x5+2x1",
            "1x5+7x1",
            "1x5+2x1+2x2.5",
            "12x1",
            "7x1+2x2.5",
        });
        successes.ShouldAllBe(c => c.Total == 12m && c.FailureReason == null);

        // TÜM denemeler listede (başarısızlar dahil, numaralandırma sırasıyla). Son-kolon kuralıyla
        // (2026-07-10: son kolonda birer eksiltme yok — ya tek seferde karşılar ya dal kapanır) motor
        // kullanıcının konsept tablosuyla BİREBİR 27 deneme üretir.
        result.All.Count.ShouldBe(27);
        result.All.Count(c => !c.Success).ShouldBe(21);
        result.All.Where(c => !c.Success).ShouldAllBe(c => c.FailureReason != null && c.Rank == null);

        // İlk deneme açgözlü doldurmanın kendisi: 1×10 + 2×1 ✓; konseptteki ilk başarısız da listede
        // (1×10 + 1×1 = 11 → kalan 1, 2.5 sığmaz).
        Describe(result.All[0]).ShouldBe("1x10+2x1");
        var firstFailure = result.All[1];
        Describe(firstFailure).ShouldBe("1x10+1x1");
        firstFailure.Success.ShouldBeFalse();
        firstFailure.FailureReason.ShouldBe(SubstitutionReasonCodes.RemainderPrefix + "1");
    }

    [Fact]
    public void User_example_package_counts_measure_how_many_times_combination_repeats_from_stock()
    {
        var result = SubstitutionSolver.Solve(UserExampleInput());
        var byDescription = result.All.Where(c => c.Success).ToDictionary(Describe);

        // paketSayısı = min(eldekiAdet ÷ kullanılanAdet) tam bölme (konsept madde 1 netleştirmesi).
        byDescription["1x10+2x1"].PackageCount.ShouldBe(3);        // min(3/1, 20/2) = 3
        byDescription["2x5+2x1"].PackageCount.ShouldBe(3);         // min(7/2, 20/2) = 3
        byDescription["1x5+7x1"].PackageCount.ShouldBe(2);         // min(7/1, 20/7) = 2
        byDescription["1x5+2x1+2x2.5"].PackageCount.ShouldBe(1);   // min(7, 10, 3/2) = 1
        byDescription["12x1"].PackageCount.ShouldBe(1);            // 20/12 = 1
        byDescription["7x1+2x2.5"].PackageCount.ShouldBe(1);       // min(20/7, 3/2) = 1
    }

    [Fact]
    public void User_example_scoring_ranks_cheapest_then_fewest_pieces_then_most_packages()
    {
        var result = SubstitutionSolver.Solve(UserExampleInput());
        var byDescription = result.All.Where(c => c.Success).ToDictionary(Describe);

        // Rank1 = ANA varyant adayı: 1×10 + 2×1 → 40 + 2×4.5 = 49.0 (en ucuz).
        var best = byDescription["1x10+2x1"];
        best.Rank.ShouldBe(1);
        best.TotalCost.ShouldBe(49.0m);
        best.PieceCount.ShouldBe(3);

        // Tam skor sırası (maliyet KÜÇÜK → parça KÜÇÜK → paket BÜYÜK):
        // 49.0 → 51.0 → 52.0 → 52.5 → 53.5 → 54.0.
        byDescription["2x5+2x1"].Rank.ShouldBe(2);          // 2×21 + 2×4.5 = 51.0
        byDescription["1x5+2x1+2x2.5"].Rank.ShouldBe(3);    // 21 + 9 + 22 = 52.0
        byDescription["1x5+7x1"].Rank.ShouldBe(4);          // 21 + 31.5 = 52.5
        byDescription["7x1+2x2.5"].Rank.ShouldBe(5);        // 31.5 + 22 = 53.5
        byDescription["12x1"].Rank.ShouldBe(6);             // 12×4.5 = 54.0
    }

    // ── Tolerans (konsept madde 3: Gram mutlak | PerMille göreceli; 0 = mutlak eşitlik) ─────────────

    [Fact]
    public void Unreachable_amount_with_zero_tolerance_produces_no_successes()
    {
        // 37.6 — 0.5 taneciklerle (10/5/1/2.5) tam tutturulamaz; tolerans 0 → hepsi başarısız.
        var result = SubstitutionSolver.Solve(UserExampleInput(requestedAmount: 37.6m));

        result.All.ShouldNotBeEmpty();
        result.All.ShouldAllBe(c => !c.Success);
    }

    [Fact]
    public void Unreachable_amount_with_sufficient_permille_tolerance_accepts_nearest_grid_totals()
    {
        // Binde 3 → efektif tolerans 37.6 × 3 / 1000 = 0.1128 → yalnız 37.5'lik kombinasyonlar geçer.
        var result = SubstitutionSolver.Solve(UserExampleInput(
            requestedAmount: 37.6m, toleranceType: ToleranceType.PerMille, toleranceValue: 3m));

        var successes = result.All.Where(c => c.Success).ToList();
        successes.ShouldNotBeEmpty();
        successes.ShouldAllBe(c => c.Total == 37.5m);
        successes.Select(c => c.Rank).ShouldBe(Enumerable.Range(1, successes.Count).Select(r => (int?)r), ignoreOrder: true);
    }

    [Fact]
    public void Gram_tolerance_accepts_totals_within_absolute_band_in_both_directions()
    {
        // 12 ± 0.5 gram: 11.5 (ör. 1×5 + 4×1 + 1×2.5), 12.0 ve 12.5 (ör. 1×10 + 1×2.5) hepsi geçerli.
        var result = SubstitutionSolver.Solve(UserExampleInput(
            toleranceType: ToleranceType.Amount, toleranceValue: 0.5m));

        var successTotals = result.All.Where(c => c.Success).Select(c => c.Total).Distinct().ToList();
        successTotals.ShouldBe(new[] { 12.5m, 12m, 11.5m }, ignoreOrder: true);
    }

    [Fact]
    public void Permille_and_gram_tolerance_compute_different_effective_bands()
    {
        // Aynı sayısal değer (5): Gram → ±5 (geniş), PerMille → ±12×5/1000 = ±0.06 (yalnız tam 12).
        var gram = SubstitutionSolver.Solve(UserExampleInput(toleranceType: ToleranceType.Amount, toleranceValue: 5m));
        var perMille = SubstitutionSolver.Solve(UserExampleInput(toleranceType: ToleranceType.PerMille, toleranceValue: 5m));

        gram.All.Where(c => c.Success).Select(c => c.Total).ShouldContain(7m);          // 12−5 sınırı dahil
        perMille.All.Where(c => c.Success).ShouldAllBe(c => c.Total == 12m);
    }

    // ── Ön-filtre (konsept madde 4) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Prefilter_removes_overweight_pieces_and_zero_stock_commodities()
    {
        var heavy = Guid.NewGuid();
        var empty = Guid.NewGuid();
        var usable = Guid.NewGuid();
        var input = new SubstitutionSolverInput(10m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
        {
            new(heavy, "HEAVY", 15m, 4, 60m),
            new(empty, "EMPTY", 5m, 0, 20m),
            new(usable, "OK", 5m, 2, 20m),
        });

        var result = SubstitutionSolver.Solve(input);

        result.FilteredOut.Count.ShouldBe(2);
        result.FilteredOut.Single(f => f.CommodityId == heavy).Reason
            .ShouldBe(SubstitutionReasonCodes.PieceWeightExceedsTarget);
        result.FilteredOut.Single(f => f.CommodityId == empty).Reason
            .ShouldBe(SubstitutionReasonCodes.NoStock);

        // Kalan tek emtia ile açgözlü ilk deneme: 2×5 = 10 ✓.
        var success = result.All.Single(c => c.Success);
        var line = success.Lines.ShouldHaveSingleItem();
        line.CommodityId.ShouldBe(usable);
        line.Count.ShouldBe(2);
    }

    [Fact]
    public void Prefilter_upper_bound_includes_tolerance_so_slightly_heavy_piece_stays()
    {
        // Talep 10, Gram tolerans 0.5 → 10.5'lik tek parça ELENMEZ ve tek başına başarılı olur.
        var slightlyHeavy = Guid.NewGuid();
        var input = new SubstitutionSolverInput(10m, ToleranceType.Amount, 0.5m, new List<SubstitutionCommodity>
        {
            new(slightlyHeavy, "GR10.5", 10.5m, 1, 42m),
        });

        var result = SubstitutionSolver.Solve(input);

        result.FilteredOut.ShouldBeEmpty();
        var success = result.All.Single(c => c.Success);
        success.Total.ShouldBe(10.5m);
        success.Rank.ShouldBe(1);
    }

    [Fact]
    public void All_commodities_filtered_out_yields_empty_result()
    {
        var input = new SubstitutionSolverInput(1m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
        {
            new(Guid.NewGuid(), "HEAVY", 5m, 3, 10m),
            new(Guid.NewGuid(), "EMPTY", 1m, 0, 2m),
        });

        var result = SubstitutionSolver.Solve(input);

        result.All.ShouldBeEmpty();
        result.FilteredOut.Count.ShouldBe(2);
    }

    // ── Başarısızlık nedenleri (teknik kod — UI lokalize eder) ───────────────────────────────────────

    [Fact]
    public void Total_stock_shortfall_short_circuits_instead_of_producing_exhausted_trials()
    {
        // ESKİ davranış: tüm stok 2×5 = 10 < 12 → StockExhausted denemesi üretilirdi. YENİ (2026-07-10
        // kullanıcı kararı): toplam kapasite talebin altındaysa numaralandırma HİÇ başlamaz.
        var id = Guid.NewGuid();
        var input = new SubstitutionSolverInput(12m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
        {
            new(id, "GR5", 5m, 2, 20m),
        });

        var result = SubstitutionSolver.Solve(input);

        result.InsufficientStock.ShouldBeTrue();
        result.TotalAvailableWeight.ShouldBe(10m);
        result.All.ShouldBeEmpty();
    }

    // ── Girdi guard'ları + arama-uzayı koruması ─────────────────────────────────────────────────────

    [Fact]
    public void Invalid_inputs_fail_fast_with_business_exceptions()
    {
        Should.Throw<BusinessException>(() => SubstitutionSolver.Solve(UserExampleInput(requestedAmount: 0m)))
            .Code.ShouldBe("TradeXpress:Substitution:RequestedAmountInvalid");

        Should.Throw<BusinessException>(() => SubstitutionSolver.Solve(
                new SubstitutionSolverInput(10m, ToleranceType.Amount, -1m, UserExampleCommodities())))
            .Code.ShouldBe("TradeXpress:Substitution:ToleranceValueInvalid");

        Should.Throw<BusinessException>(() => SubstitutionSolver.Solve(
                new SubstitutionSolverInput(10m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
                {
                    new(Guid.NewGuid(), "BAD", 0m, 5, 1m),
                })))
            .Code.ShouldBe("TradeXpress:Substitution:PieceWeightInvalid");
    }

    [Fact]
    public void Insufficient_total_stock_short_circuits_before_enumeration_starts()
    {
        // 2026-07-10 kullanıcı kararı: envanterin toplam ağırlığı talebin altındaysa numaralandırma
        // HİÇ başlamaz. Kapasite 2×1 + 1×2.5 = 4.5 gr < talep 12 gr → sıfır deneme + bayrak.
        var input = new SubstitutionSolverInput(12m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
        {
            new(Gr1Id, "GR1", 1m, 2, 4.5m),
            new(Gr25Id, "GR2.5", 2.5m, 1, 11m),
        });

        var result = SubstitutionSolver.Solve(input);

        result.InsufficientStock.ShouldBeTrue();
        result.TotalAvailableWeight.ShouldBe(4.5m);
        result.All.ShouldBeEmpty();

        // SINIR: kapasite talebe TAM eşitse hesap KOŞAR (12×1gr = 12 → tek başarı, tüm stok).
        var boundary = SubstitutionSolver.Solve(new SubstitutionSolverInput(
            12m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
            {
                new(Gr1Id, "GR1", 1m, 12, 4.5m),
            }));
        boundary.InsufficientStock.ShouldBeFalse();
        boundary.All.ShouldContain(c => c.Success && c.Total == 12m);

        // Tolerans alt bandı hesaba katılır: talep 12, tolerans 1 gr, kapasite 11 → 11 ≥ 12−1 → koşar.
        var withinBand = SubstitutionSolver.Solve(new SubstitutionSolverInput(
            12m, ToleranceType.Amount, 1m, new List<SubstitutionCommodity>
            {
                new(Gr1Id, "GR1", 1m, 11, 4.5m),
            }));
        withinBand.InsufficientStock.ShouldBeFalse();
        withinBand.All.ShouldContain(c => c.Success && c.Total == 11m);
    }

    [Fact]
    public void Enumeration_completes_fully_without_any_trial_limit()
    {
        // 2026-07-10 kullanıcı kararı: deneme sınırı YOK — eski 100_000 guard'ını aşan uzay bile sonuna
        // kadar numaralandırılır (erken kesim en iyi kombinasyonu kaçırabilirdi: "bininci kombinasyon
        // belki en iyi kombinasyon olabilir"). Girdi, son-kolon kuralı SONRASI deneme sayısı yine eski
        // limiti aşacak şekilde ölçekli: (talep+1)² deneme ≈ 321² = 103.041 > 100.000.
        var input = new SubstitutionSolverInput(320m, ToleranceType.Amount, 0m, new List<SubstitutionCommodity>
        {
            new(Guid.NewGuid(), "GR1", 1m, 320, 4m),
            new(Guid.NewGuid(), "GR05", 0.5m, 640, 2m),
            new(Guid.NewGuid(), "GR025", 0.25m, 1280, 1m),
        });

        var result = SubstitutionSolver.Solve(input);

        result.All.Count.ShouldBeGreaterThan(100_000);          // eski limitin ötesine geçti — sınır gerçekten yok
        result.All.ShouldContain(c => c.Rank == 1);              // tam uzayda en iyi kombinasyon atanmış
        result.All.Where(c => c.Success).ShouldAllBe(c => c.Total == 320m);  // tolerans 0 → tüm başarılılar tam 320gr
    }
}
