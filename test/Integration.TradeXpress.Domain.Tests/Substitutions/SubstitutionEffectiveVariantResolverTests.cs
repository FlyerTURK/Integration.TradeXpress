using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Etkin varyant kümesi çözümleyicisi (Dilim-2) — <see cref="SubstitutionEffectiveVariantResolver"/> saf sözleşmesi:
/// <c>override ?? IncludedVariantIds(doluysa) ?? {ana varyant}</c>. Boş liste = yalnız ana varyant (statüko
/// değişmezi); ana varyantı olmayan maden TEK null adaya iner (legacy varyantsız yol).
/// </summary>
public class SubstitutionEffectiveVariantResolverTests
{
    [Fact]
    public void Override_when_present_wins_over_included_set_and_main()
    {
        var overrideA = Guid.NewGuid();
        var overrideB = Guid.NewGuid();
        var included = new[] { Guid.NewGuid() };
        var mainId = Guid.NewGuid();

        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            new[] { overrideA, overrideB }, included, mainId);

        effective.ShouldBe(new List<Guid?> { overrideA, overrideB });
    }

    [Fact]
    public void Included_set_when_present_wins_over_main_and_preserves_user_order()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var mainId = Guid.NewGuid();

        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: null, new[] { first, second }, mainId);

        effective.ShouldBe(new List<Guid?> { first, second });
    }

    [Fact]
    public void Empty_included_set_falls_back_to_main_variant_status_quo()
    {
        var mainId = Guid.NewGuid();

        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: null, includedVariantIds: new List<Guid>(), mainId);

        effective.ShouldBe(new List<Guid?> { mainId });
    }

    [Fact]
    public void No_catalog_variant_yields_single_null_candidate_for_legacy_metal()
    {
        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: null, includedVariantIds: null, mainVariantId: null);

        var candidate = effective.ShouldHaveSingleItem();
        candidate.ShouldBeNull();
    }

    [Fact]
    public void Empty_override_is_treated_as_absent_and_falls_through_to_included_set()
    {
        var included = Guid.NewGuid();

        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: Array.Empty<Guid>(), new[] { included }, Guid.NewGuid());

        effective.ShouldBe(new List<Guid?> { included });
    }

    [Fact]
    public void Duplicates_and_empty_guids_are_removed_defensively_preserving_order()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: null, new[] { first, Guid.Empty, second, first }, mainVariantId: null);

        effective.ShouldBe(new List<Guid?> { first, second });
    }

    [Fact]
    public void Override_of_only_empty_guids_is_absent_not_an_empty_set()
    {
        // Savunmacı ayıklama override'ı boşaltırsa "override yok" sayılır — zincir dahil-küme/ana'ya düşer.
        var mainId = Guid.NewGuid();

        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            new[] { Guid.Empty }, includedVariantIds: null, mainId);

        effective.ShouldBe(new List<Guid?> { mainId });
    }
}
