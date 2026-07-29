using System;
using Integration.TradeXpress.N11Categories;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// "Mega mi?" yüklemi — sentetik üst katmanı N11'den gelen gerçek kategorilerden ayıran TEK kaynak.
///
/// <para>Neden mekanik ağ: kategori sayımı (mega hariç) ve senkron damgası bu yükleme dayanıyor. Sessizce
/// bozulursa sayım yanlış çıkar ve damga yanlış satırlara yazılır — ikisi de gözle fark edilmez.</para>
/// </summary>
public class N11MegaCategoryTests
{
    [Theory]
    [InlineData("MEGA-MODA")]
    [InlineData("MEGA-ELEKTRONIK")]
    [InlineData("MEGA-EV")]
    [InlineData("MEGA-BEBEK")]
    [InlineData("MEGA-KOZMETIK")]
    [InlineData("MEGA-MUCEVHER")]
    [InlineData("MEGA-SPOR")]
    [InlineData("MEGA-KITAP")]
    [InlineData("MEGA-OTOMOTIV")]
    public void Recognizes_every_synthetic_mega(string externalId)
    {
        N11MegaCategories.IsMega(externalId).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1001770")]   // Ayakkabı & Çanta — GERÇEK top-level
    [InlineData("1002680")]   // Yatırımlık Altın & Gümüş
    [InlineData("1000145")]
    public void Does_not_treat_real_n11_categories_as_mega(string externalId)
    {
        N11MegaCategories.IsMega(externalId).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Handles_missing_identifiers(string? externalId)
    {
        N11MegaCategories.IsMega(externalId).ShouldBeFalse();
    }

    [Fact]
    public void Does_not_match_by_prefix_alone()
    {
        // Ayırt etme ÜYELİK'tir, önek değil: "MEGA-" ile başlayan uydurma bir id mega SAYILMAZ.
        // (Önek sabiti yalnız belgeleme içindir.)
        N11MegaCategories.IsMega(N11MegaCategories.SyntheticIdPrefix + "UYDURMA").ShouldBeFalse();
    }

    [Fact]
    public void Every_declared_mega_is_recognized()
    {
        // Listeye yeni mega eklenirse yüklem onu da tanımalı (liste ile yüklem ayrışmasın).
        foreach (var (externalId, _) in N11MegaCategories.Megas)
        {
            N11MegaCategories.IsMega(externalId).ShouldBeTrue();
        }
    }

    [Fact]
    public void Marking_sync_moves_the_stamp_forward()
    {
        var category = new N11Category("MEGA-MODA", null, "Moda", isLeaf: false, lastModifiedExternal: null);
        var first = new DateTime(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc);
        var second = first.AddHours(25);

        category.MarkSynced(first);
        category.LastSyncedAt.ShouldBe(first);

        category.MarkSynced(second);
        category.LastSyncedAt.ShouldBe(second);
    }

    [Fact]
    public void Sync_stamp_is_independent_of_the_n11_supplied_timestamp()
    {
        // LastSyncedAt = BİZİM mutabakat anımız; LastModifiedExternal = N11'in kendi tarihi. Karıştırılırsa
        // bayatlık kapısı yanlış alandan okur ve (REST bu alanı hep null döndürdüğü için) hiç kapanmaz.
        var external = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var category = new N11Category("MEGA-EV", null, "Ev & Yaşam", isLeaf: false, lastModifiedExternal: external);

        category.MarkSynced(new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc));

        category.LastModifiedExternal.ShouldBe(external);
        category.LastSyncedAt.ShouldNotBe(category.LastModifiedExternal);
    }
}
