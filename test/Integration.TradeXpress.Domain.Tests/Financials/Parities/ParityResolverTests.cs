using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// <see cref="ParityResolver"/> saf testleri — parite çiftleri grafiğinde çok-seviyeli
/// (BFS, çift yönlü) bağlantı + kanonik base seçimi. Öncelik sıraları senaryo sabiti:
/// USD=10 &lt; SAR=20 &lt; TRY=30 &lt; EUR=40 &lt; GBP=50 (küçük = güçlü = base);
/// rank haritasında olmayan birim 999 (zayıf) sayılır.
/// </summary>
public class ParityResolverTests
{
    private static readonly Guid Usd = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Sar = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Trl = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Eur = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Gbp = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static int Rank(Guid unitId)
    {
        if (unitId == Usd) { return 10; }
        if (unitId == Sar) { return 20; }
        if (unitId == Trl) { return 30; }
        if (unitId == Eur) { return 40; }
        if (unitId == Gbp) { return 50; }
        return 999;   // bilinmeyen = en zayıf
    }

    private static IReadOnlyCollection<(Guid Base, Guid Quote)> Pairs(params (Guid, Guid)[] pairs)
    {
        return pairs;
    }

    // ── Doğrudan parite ───────────────────────────────────────────────────────

    [Fact]
    public void Direct_pair_resolves_to_higher_priority_unit()
    {
        var pairs = Pairs((Usd, Trl));

        ParityResolver.ResolveBaseId(pairs, Usd, Trl, Rank).ShouldBe(Usd);
    }

    [Fact]
    public void Direct_pair_is_argument_order_independent()
    {
        // TRYSAR de SARTRY de aynı base'i verir (rank'lar farklı olduğu sürece).
        var pairs = Pairs((Sar, Trl));

        ParityResolver.ResolveBaseId(pairs, Sar, Trl, Rank).ShouldBe(Sar);
        ParityResolver.ResolveBaseId(pairs, Trl, Sar, Rank).ShouldBe(Sar);
    }

    [Fact]
    public void Pair_storage_direction_is_ignored_edges_are_undirected()
    {
        // Kayıt (TRY,USD) diye TERS saklansa bile bağlantı kurulur; base'i RANK belirler, kolon değil.
        var pairs = Pairs((Trl, Usd));

        ParityResolver.ResolveBaseId(pairs, Usd, Trl, Rank).ShouldBe(Usd);
    }

    // ── Zincirleme (2-3 seviye) ───────────────────────────────────────────────

    [Fact]
    public void Two_level_chain_connects_via_hub_unit()
    {
        // SARUSD + USDTRY → SAR ile TRY, USD hub'ı üzerinden bağlı; base = öncelikli SAR.
        var pairs = Pairs((Usd, Sar), (Usd, Trl));

        ParityResolver.ResolveBaseId(pairs, Sar, Trl, Rank).ShouldBe(Sar);
        ParityResolver.ResolveBaseId(pairs, Trl, Sar, Rank).ShouldBe(Sar);
    }

    [Fact]
    public void Three_level_chain_resolves_at_default_max_levels()
    {
        // SAR—USD—EUR—GBP: 3 kenar = varsayılan sınırın tam içi.
        var pairs = Pairs((Sar, Usd), (Usd, Eur), (Eur, Gbp));

        ParityResolver.ResolveBaseId(pairs, Sar, Gbp, Rank).ShouldBe(Sar);
    }

    [Fact]
    public void Four_level_chain_exceeds_default_but_resolves_with_higher_limit()
    {
        // SAR—USD—EUR—GBP—TRY: 4 kenar → varsayılan (3) yetmez, maxLevels=4 ile çözülür.
        var pairs = Pairs((Sar, Usd), (Usd, Eur), (Eur, Gbp), (Gbp, Trl));

        ParityResolver.ResolveBaseId(pairs, Sar, Trl, Rank).ShouldBeNull();
        ParityResolver.ResolveBaseId(pairs, Sar, Trl, Rank, maxLevels: 4).ShouldBe(Sar);
    }

    [Fact]
    public void Cycle_in_graph_does_not_prevent_or_break_resolution()
    {
        // Döngü (USD—TRY—EUR—USD): visited kümesi sonsuz turu engeller; bağlantısız hedef null döner.
        var pairs = Pairs((Usd, Trl), (Trl, Eur), (Eur, Usd));

        ParityResolver.ResolveBaseId(pairs, Usd, Eur, Rank).ShouldBe(Usd);
        ParityResolver.ResolveBaseId(pairs, Usd, Gbp, Rank).ShouldBeNull();
    }

    // ── Base seçim önceliği ───────────────────────────────────────────────────

    [Fact]
    public void Unknown_unit_ranks_weakest_so_known_unit_becomes_base()
    {
        var unknown = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var pairs = Pairs((unknown, Gbp));

        // GBP (50) < bilinmeyen (999) → base GBP.
        ParityResolver.ResolveBaseId(pairs, unknown, Gbp, Rank).ShouldBe(Gbp);
    }

    [Fact]
    public void Equal_rank_tie_returns_first_argument()
    {
        // Karakterizasyon: eşit rank'ta ilk argüman kazanır → sıra-bağımsızlık YALNIZ
        // rank'lar farklıyken garantidir (üretimde öncelikler benzersiz beklenir).
        var pairs = Pairs((Usd, Trl));
        static int FlatRank(Guid _) { return 1; }

        ParityResolver.ResolveBaseId(pairs, Usd, Trl, FlatRank).ShouldBe(Usd);
        ParityResolver.ResolveBaseId(pairs, Trl, Usd, FlatRank).ShouldBe(Trl);
    }

    // ── Çözülemeyen / geçersiz durumlar ───────────────────────────────────────

    [Fact]
    public void Disconnected_units_return_null()
    {
        var pairs = Pairs((Usd, Trl));

        ParityResolver.ResolveBaseId(pairs, Sar, Eur, Rank).ShouldBeNull();
    }

    [Fact]
    public void Empty_pair_set_returns_null()
    {
        ParityResolver.ResolveBaseId(Pairs(), Usd, Trl, Rank).ShouldBeNull();
    }

    [Fact]
    public void Same_unit_returns_null()
    {
        var pairs = Pairs((Usd, Trl));

        ParityResolver.ResolveBaseId(pairs, Usd, Usd, Rank).ShouldBeNull();
    }

    [Fact]
    public void Empty_guid_arguments_return_null()
    {
        var pairs = Pairs((Usd, Trl));

        ParityResolver.ResolveBaseId(pairs, Guid.Empty, Trl, Rank).ShouldBeNull();
        ParityResolver.ResolveBaseId(pairs, Usd, Guid.Empty, Rank).ShouldBeNull();
    }

    [Fact]
    public void Null_inputs_return_null_instead_of_throwing()
    {
        ParityResolver.ResolveBaseId(null!, Usd, Trl, Rank).ShouldBeNull();
        ParityResolver.ResolveBaseId(Pairs((Usd, Trl)), Usd, Trl, null!).ShouldBeNull();
    }

    [Fact]
    public void Zero_max_levels_finds_nothing_even_for_direct_pair()
    {
        // Karakterizasyon: seviye sınırı kenar sayısıdır; 0 → hiç genişleme yok → null.
        var pairs = Pairs((Usd, Trl));

        ParityResolver.ResolveBaseId(pairs, Usd, Trl, Rank, maxLevels: 0).ShouldBeNull();
    }
}
