using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// <see cref="SubstitutionGroupItem.SetIncludedVariants"/> normalizasyon testleri (Dilim 1 — opt-in varyant kapsamı).
/// Değişmez: BOŞ liste = yalnız ANA varyant dahil (statüko) → null/boş girişler boş listeye iner; duplike ve
/// boş-Guid ayıklanır, kullanıcı sırası korunur. "{yalnız ana} → boş" normalizasyonu yazma sınırında (AppService,
/// ana-varyant bilgisi orada) — entity testi yalnız entity'nin kendi sözleşmesini kilitler.
/// </summary>
public class SubstitutionGroupItemIncludedVariantsTests
{
    [Fact]
    public void New_item_defaults_to_empty_included_variants_status_quo()
    {
        // Yeni satır hiçbir varyant seçmeden doğar → boş liste = yalnız ana varyant (mevcut gruplar aynen çalışır).
        var item = CreateItem();

        item.IncludedVariantIds.ShouldBeEmpty();
    }

    [Fact]
    public void SetIncludedVariants_null_normalizes_to_empty_list()
    {
        var item = CreateItem();
        item.SetIncludedVariants(new[] { Guid.NewGuid() });

        item.SetIncludedVariants(null);

        item.IncludedVariantIds.ShouldBeEmpty();
    }

    [Fact]
    public void SetIncludedVariants_removes_duplicates_and_empty_guids_preserving_order()
    {
        var item = CreateItem();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        item.SetIncludedVariants(new[] { first, Guid.Empty, second, first });

        item.IncludedVariantIds.ShouldBe(new List<Guid> { first, second });
    }

    [Fact]
    public void SetIncludedVariants_replaces_previous_set()
    {
        var item = CreateItem();
        var initial = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        item.SetIncludedVariants(new[] { initial });

        item.SetIncludedVariants(new[] { replacement });

        item.IncludedVariantIds.ShouldBe(new List<Guid> { replacement });
    }

    private static SubstitutionGroupItem CreateItem()
    {
        return new SubstitutionGroupItem(
            companyId: Guid.NewGuid(),
            substitutionGroupId: Guid.NewGuid(),
            metalId: Guid.NewGuid());
    }
}
