using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// <see cref="SubstitutionEffectiveVariantResolver"/> — 2026-07-27'de DEĞİŞEN kapsam semantiğini sabitler.
///
/// <para>ESKİ kural: ürünün override listesi boşsa gruptan devralınırdı; yani kullanıcı bir madenin son
/// işaretini kaldırdığında sistem sessizce gruba dönüyor, kaldırma eylemi ETKİSİZ kalıyordu.
/// YENİ kural: liste VERİLDİYSE (null değil) ürünün kendi kapsamıdır — boş olması da bir cevaptır
/// ("bu madeni istemiyorum"). Grup yalnız ürün bağlamı YOKKEN (null) belirleyicidir.</para>
/// </summary>
public class SubstitutionScopeSemanticsTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid VariantA = Guid.NewGuid();
    private static readonly Guid VariantB = Guid.NewGuid();

    /// <summary>KURALIN ÖZÜ: ürün listesi boş VERİLDİYSE gruba DÖNÜLMEZ — o maden kullanılmaz.</summary>
    [Fact]
    public void Empty_product_scope_means_excluded_not_inherited()
    {
        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: Array.Empty<Guid>(),
            includedVariantIds: new[] { VariantA, VariantB },
            mainVariantId: Main);

        effective.ShouldBeEmpty();
    }

    [Fact]
    public void Product_scope_wins_over_group_scope()
    {
        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: new[] { VariantB },
            includedVariantIds: new[] { VariantA },
            mainVariantId: Main);

        effective.ShouldBe(new Guid?[] { VariantB });
    }

    /// <summary>Ürün bağlamı YOKKEN (grup hesaplama sayfası) grup kalemi belirleyicidir.</summary>
    [Fact]
    public void Group_scope_applies_when_there_is_no_product_context()
    {
        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: null,
            includedVariantIds: new[] { VariantA },
            mainVariantId: Main);

        effective.ShouldBe(new Guid?[] { VariantA });
    }

    /// <summary>Ürün bağlamı yok + grup kalemi de boş → statüko değişmezi: yalnız ana varyant.</summary>
    [Fact]
    public void Falls_back_to_main_variant_without_product_or_group_scope()
    {
        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: null,
            includedVariantIds: Array.Empty<Guid>(),
            mainVariantId: Main);

        effective.ShouldBe(new Guid?[] { Main });
    }

    [Fact]
    public void Duplicates_and_empty_guids_are_defensively_removed()
    {
        var effective = SubstitutionEffectiveVariantResolver.Resolve(
            overrideVariantIds: new[] { VariantA, Guid.Empty, VariantA, VariantB },
            includedVariantIds: null,
            mainVariantId: Main);

        effective.ShouldBe(new Guid?[] { VariantA, VariantB });
    }
}
