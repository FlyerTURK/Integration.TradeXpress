using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.SalesChannels.Variants;

/// <summary>
/// Saf çekirdek karakterizasyonu (S2, 2026-07-09) — <see cref="VariantCombinationEngine"/>'in semantik
/// sözleşmesini kilitler (kombinatorik + sıra + BuildKey format snapshot). KIRMIZIYSA çekirdek davranışı
/// değişmiş demektir — ERP synchronizer VE kanal reconcile'ları aynı motora bağlı, testi gevşetme.
/// </summary>
public class VariantCombinationEngineTests
{
    // ── BuildCartesian (eksen, değer) çiftli overload ────────────────────────────────────────────────

    [Fact]
    public void Cartesian_of_2x3_axes_produces_six_pair_combinations_in_axis_and_value_order()
    {
        var axes = new List<(string Axis, IReadOnlyList<string> Values)>
        {
            ("Renk", new[] { "Red", "Blue" }),
            ("Beden", new[] { "S", "M", "L" }),
        };

        var combos = VariantCombinationEngine.BuildCartesian<string, string>(axes);

        combos.Count.ShouldBe(6);
        // Sıra deterministik: soldan sağa çarpım (ilk eksen yavaş, son eksen hızlı döner).
        combos.Select(c => string.Join("+", c.Select(p => p.Value))).ShouldBe(
            new[] { "Red+S", "Red+M", "Red+L", "Blue+S", "Blue+M", "Blue+L" });
        // Her kombinasyonda çiftler eksen giriş sırasıyla durur.
        combos.ShouldAllBe(c => c[0].Axis == "Renk" && c[1].Axis == "Beden");
    }

    [Fact]
    public void Cartesian_of_empty_axis_list_produces_single_empty_combination()
    {
        var axes = new List<(string Axis, IReadOnlyList<string> Values)>();

        var combos = VariantCombinationEngine.BuildCartesian<string, string>(axes);

        // Çarpımın birim elemanı — "0 eksen = kombinasyon yok" yorumu ÇAĞIRANIN guard'ıdır.
        var single = combos.ShouldHaveSingleItem();
        single.ShouldBeEmpty();
    }

    [Fact]
    public void Cartesian_with_valueless_axis_produces_no_combinations()
    {
        var axes = new List<(string Axis, IReadOnlyList<string> Values)>
        {
            ("Renk", new[] { "Red", "Blue" }),
            ("Beden", Array.Empty<string>()),
        };

        var combos = VariantCombinationEngine.BuildCartesian<string, string>(axes);

        // Çarpanlardan biri 0 → sonuç boş (mevcut ERP/N11 semantiği; "seti koru" kararı çağıranda).
        combos.ShouldBeEmpty();
    }

    [Fact]
    public void Cartesian_of_single_axis_yields_one_combination_per_value()
    {
        var axes = new List<(string Axis, IReadOnlyList<string> Values)>
        {
            ("Renk", new[] { "Red", "Blue", "Green" }),
        };

        var combos = VariantCombinationEngine.BuildCartesian<string, string>(axes);

        combos.Count.ShouldBe(3);
        combos.Select(c => c.ShouldHaveSingleItem().Value).ShouldBe(new[] { "Red", "Blue", "Green" });
    }

    [Fact]
    public void Cartesian_of_three_axes_multiplies_counts()
    {
        var axes = new List<(int Axis, IReadOnlyList<int> Values)>
        {
            (1, new[] { 10, 11 }),
            (2, new[] { 20, 21, 22 }),
            (3, new[] { 30, 31, 32, 33 }),
        };

        var combos = VariantCombinationEngine.BuildCartesian<int, int>(axes);

        combos.Count.ShouldBe(2 * 3 * 4);
        combos.Select(c => string.Join("|", c.Select(p => p.Value))).Distinct().Count().ShouldBe(24);
        combos.ShouldAllBe(c => c.Count == 3);
    }

    // ── BuildCartesian değer-listesi overload'u (DTO eksenleri) ─────────────────────────────────────

    [Fact]
    public void Valuelist_cartesian_matches_pair_overload_combinatorics()
    {
        var axes = new List<IReadOnlyList<string>>
        {
            new[] { "Red", "Blue" },
            new[] { "S", "M", "L" },
        };

        var combos = VariantCombinationEngine.BuildCartesian<string>(axes);

        combos.Count.ShouldBe(6);
        combos.Select(c => string.Join("+", c)).ShouldBe(
            new[] { "Red+S", "Red+M", "Red+L", "Blue+S", "Blue+M", "Blue+L" });
    }

    [Fact]
    public void Valuelist_cartesian_of_empty_list_produces_single_empty_combination()
    {
        var combos = VariantCombinationEngine.BuildCartesian<string>(new List<IReadOnlyList<string>>());

        combos.ShouldHaveSingleItem().ShouldBeEmpty();
    }

    [Fact]
    public void Valuelist_cartesian_with_valueless_axis_produces_no_combinations()
    {
        var axes = new List<IReadOnlyList<string>>
        {
            new[] { "Red", "Blue" },
            Array.Empty<string>(),
        };

        VariantCombinationEngine.BuildCartesian<string>(axes).ShouldBeEmpty();
    }

    // ── BuildKey format snapshot ────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildKey_sorts_ids_ascending_and_joins_with_pipe()
    {
        // Deterministik id'ler — format snapshot (ERP BuildKey birebiri, S1 testleriyle hizalı).
        var low = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var high = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var key = VariantCombinationEngine.BuildKey(new[] { high, low });

        key.ShouldBe("11111111-1111-1111-1111-111111111111|99999999-9999-9999-9999-999999999999");
    }

    [Fact]
    public void BuildKey_is_order_independent()
    {
        var a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var b = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var c = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var key1 = VariantCombinationEngine.BuildKey(new[] { c, a, b });
        var key2 = VariantCombinationEngine.BuildKey(new[] { a, b, c });

        key1.ShouldBe(key2);
    }

    [Fact]
    public void BuildKey_of_single_id_is_the_id_itself()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        VariantCombinationEngine.BuildKey(new[] { id }).ShouldBe(id.ToString());
    }

    [Fact]
    public void BuildKey_of_empty_sequence_is_empty_string()
    {
        VariantCombinationEngine.BuildKey(Array.Empty<Guid>()).ShouldBe(string.Empty);
    }
}
