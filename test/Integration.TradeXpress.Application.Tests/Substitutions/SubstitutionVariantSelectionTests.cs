using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Products;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// <see cref="SubstitutionVariantSelection"/> testleri — 2026-07-27'de değişen ANA VARYANT kuralını çiviler:
/// ana varyant artık toplam maliyete göre değil GRAM BAŞINA maliyete göre seçilir.
/// </summary>
public class SubstitutionVariantSelectionTests
{
    private static SubstitutionTrialDto Trial(
        int rank, decimal totalWeight, decimal totalCost, bool success = true)
    {
        return new SubstitutionTrialDto
        {
            Rank = rank,
            Success = success,
            TotalWeight = totalWeight,
            TotalCost = totalCost,
        };
    }

    /// <summary>
    /// KURALIN ÖZÜ: toplam maliyeti en düşük olan kombinasyon (Rank 1) gram başına PAHALI olabilir — az gram
    /// taşıdığı için toplamı küçüktür. Müşteriye "en uygun" diye sunulan varyant birim fiyatı en düşük olandır.
    /// </summary>
    [Fact]
    public void Main_variant_is_the_cheapest_per_gram_not_the_lowest_total()
    {
        var pahaliAmaKucuk = Trial(rank: 1, totalWeight: 5m, totalCost: 600m);    // 120 / gram
        var ucuzBirimFiyat = Trial(rank: 2, totalWeight: 10m, totalCost: 1000m);  // 100 / gram

        var selected = SubstitutionVariantSelection.Select(
            new[] { pahaliAmaKucuk, ucuzBirimFiyat }, SubstitutionVariantMode.Multi);

        selected.First().ShouldBeSameAs(ucuzBirimFiyat);
    }

    /// <summary>Ana varyant başa alınır ama kalanların Rank sırası KORUNUR — değişen yalnız kimin başa geçtiği.</summary>
    [Fact]
    public void Remaining_candidates_keep_their_rank_order()
    {
        var r1 = Trial(rank: 1, totalWeight: 5m, totalCost: 600m);     // 120 / gram
        var r2 = Trial(rank: 2, totalWeight: 4m, totalCost: 520m);     // 130 / gram
        var r3 = Trial(rank: 3, totalWeight: 10m, totalCost: 1000m);   // 100 / gram → ana

        var selected = SubstitutionVariantSelection.Select(new[] { r1, r2, r3 }, SubstitutionVariantMode.Multi);

        selected[0].ShouldBeSameAs(r3);
        selected[1].ShouldBeSameAs(r1);
        selected[2].ShouldBeSameAs(r2);
    }

    [Fact]
    public void Single_mode_keeps_only_the_cheapest_per_gram()
    {
        var r1 = Trial(rank: 1, totalWeight: 5m, totalCost: 600m);
        var r2 = Trial(rank: 2, totalWeight: 10m, totalCost: 1000m);

        var selected = SubstitutionVariantSelection.Select(new[] { r1, r2 }, SubstitutionVariantMode.Single);

        selected.Count.ShouldBe(1);
        selected[0].ShouldBeSameAs(r2);
    }

    [Fact]
    public void Failed_and_unranked_trials_never_become_variants()
    {
        var basarisiz = Trial(rank: 1, totalWeight: 10m, totalCost: 100m, success: false);
        var siralanmamis = new SubstitutionTrialDto { Rank = null, Success = true, TotalWeight = 10m, TotalCost = 90m };
        var gecerli = Trial(rank: 2, totalWeight: 10m, totalCost: 1000m);

        var selected = SubstitutionVariantSelection.Select(
            new[] { basarisiz, siralanmamis, gecerli }, SubstitutionVariantMode.Multi);

        selected.ShouldHaveSingleItem().ShouldBeSameAs(gecerli);
    }

    /// <summary>Ağırlığı 0 olan (olmaması gereken) kayıt sıfıra bölmeye yol açmaz ve ana varyant OLMAZ.</summary>
    [Fact]
    public void Zero_weight_trial_does_not_become_main_variant()
    {
        var sifirAgirlik = Trial(rank: 1, totalWeight: 0m, totalCost: 10m);
        var normal = Trial(rank: 2, totalWeight: 10m, totalCost: 1000m);

        var selected = SubstitutionVariantSelection.Select(
            new[] { sifirAgirlik, normal }, SubstitutionVariantMode.Multi);

        selected.First().ShouldBeSameAs(normal);
    }

    [Fact]
    public void No_successful_trial_yields_empty_selection()
    {
        var selected = SubstitutionVariantSelection.Select(
            new List<SubstitutionTrialDto>(), SubstitutionVariantMode.Multi);

        selected.ShouldBeEmpty();
    }
}
