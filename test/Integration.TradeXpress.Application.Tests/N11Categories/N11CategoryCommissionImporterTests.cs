using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// <see cref="N11CategoryCommissionImporter"/> saf parse + eşleme testleri (DB'siz). TSV'de ExternalId YOK →
/// eşleme AD YOLUYLA (sonek): TSV yolu DB yaprak yolunun soneki olmalı; bitişik tekrarlar düşürülür (sığ dallarda
/// yaprak adı üst kolonda da tekrarlanır). Eşleşmeyen/muğlak/geçersiz satırlar RAPORLANIR (görev kuralı: sessiz
/// geçilmez). Gömülü kaynak da ayrıca okunabilir olmalı (deploy'da .claude yoluna bağımlılık YOK).
/// </summary>
public class N11CategoryCommissionImporterTests
{
    private const string Header = "Agac4\tAgac3\tAgac2\tAgac1_leaf\tKomisyonKdvDahil\tPazarlamaBedeli\tPazaryeriBedeli\tHakedisGunu";

    /// <summary>Mini DB ağacı: kök "Ayakkabı &amp; Çanta" → "Ayakkabı Bakım Ürünleri" → yapraklar.</summary>
    private static List<N11Category> BuildCategories()
    {
        return new List<N11Category>
        {
            new("1", null, "Ayakkabı & Çanta", isLeaf: false, lastModifiedExternal: null),
            new("2", "1", "Ayakkabı Bakım Ürünleri", isLeaf: false, lastModifiedExternal: null),
            new("3", "2", "Ayakkabı Boyası & Spreyi", isLeaf: true, lastModifiedExternal: null),
            new("4", "2", "Ayakkabı Bağcığı", isLeaf: true, lastModifiedExternal: null),
            // Aynı ada sahip İKİ yaprak (farklı dallarda) → yol uyuşmazsa MUĞLAK raporu sınanır.
            new("5", "1", "Cüzdan", isLeaf: false, lastModifiedExternal: null),
            new("6", "5", "Kartlık", isLeaf: true, lastModifiedExternal: null),
            new("7", "2", "Kartlık", isLeaf: true, lastModifiedExternal: null),
        };
    }

    [Fact]
    public void ParseTsv_reads_rows_and_cleans_rate_expressions()
    {
        var tsv = Header + "\n" +
            "Ayakkabı & Çanta\tAyakkabı Bakım Ürünleri\tAyakkabı Boyası & Spreyi\tAyakkabı Boyası & Spreyi\t19\t%1 + KDV\t%0.67 + KDV\t24\n";

        var parse = N11CategoryCommissionImporter.ParseTsv(tsv);

        parse.InvalidRows.ShouldBeEmpty();
        var row = parse.Rows.ShouldHaveSingleItem();
        // Bitişik tekrar düşürüldü: "... > Ayakkabı Boyası & Spreyi" tek kez.
        row.Path.Count.ShouldBe(3);
        row.CommissionRate.ShouldBe(19m);
        row.MarketingFeeRate.ShouldBe(1m);
        row.MarketplaceFeeRate.ShouldBe(0.67m);
        row.PayoutDays.ShouldBe(24);
    }

    [Fact]
    public void ParseTsv_reports_invalid_rows_instead_of_silently_skipping()
    {
        var tsv = Header + "\n" +
            "A\tB\tC\tC\tOranYok\t\t\t\n" +      // komisyon çözülemez → geçersiz raporu
            "kolonsuz satır\n";                    // kolon sayısı yetersiz → geçersiz raporu

        var parse = N11CategoryCommissionImporter.ParseTsv(tsv);

        parse.Rows.ShouldBeEmpty();
        parse.InvalidRows.Count.ShouldBe(2);
    }

    [Fact]
    public void Match_maps_rows_to_leaves_by_path_suffix()
    {
        var tsv = Header + "\n" +
            "Ayakkabı & Çanta\tAyakkabı Bakım Ürünleri\tAyakkabı Boyası & Spreyi\tAyakkabı Boyası & Spreyi\t19\t%1 + KDV\t%0.67 + KDV\t24\n" +
            "Ayakkabı & Çanta\tAyakkabı Bakım Ürünleri\tAyakkabı Bağcığı\tAyakkabı Bağcığı\t21\t%1 + KDV\t%0.67 + KDV\t24\n";
        var parse = N11CategoryCommissionImporter.ParseTsv(tsv);

        var match = N11CategoryCommissionImporter.Match(parse.Rows, BuildCategories());

        match.Unmatched.ShouldBeEmpty();
        match.Conflicts.ShouldBeEmpty();
        match.Matches.Count.ShouldBe(2);
        match.Matches.Single(m => m.Category.ExternalId == "3").Row.CommissionRate.ShouldBe(19m);
        match.Matches.Single(m => m.Category.ExternalId == "4").Row.CommissionRate.ShouldBe(21m);
        match.LeafCount.ShouldBe(4);
    }

    [Fact]
    public void Match_is_accent_and_case_insensitive()
    {
        // TSV panelden farklı büyük/küçük ve aksanla gelebilir — N11NameNormalizer iki tarafı da eşitler.
        var tsv = Header + "\n" +
            "AYAKKABI & ÇANTA\tAyakkabi Bakim Urunleri\tAyakkabi Bagcigi\tAyakkabi Bagcigi\t21\t\t\t\n";
        var parse = N11CategoryCommissionImporter.ParseTsv(tsv);

        var match = N11CategoryCommissionImporter.Match(parse.Rows, BuildCategories());

        match.Matches.ShouldHaveSingleItem().Category.ExternalId.ShouldBe("4");
    }

    [Fact]
    public void Match_reports_unmatched_and_ambiguous_rows()
    {
        var tsv = Header + "\n" +
            "X\tY\tZ\tHiç Olmayan Yaprak\t19\t\t\t\n" +   // yaprak adı DB'de yok → eşleşmedi raporu
            "Yanlış Üst\tKartlık\tKartlık\tKartlık\t19\t\t\t\n";   // ad iki yaprakta var, yol uyuşmuyor → MUĞLAK/uyuşmaz raporu
        var parse = N11CategoryCommissionImporter.ParseTsv(tsv);

        var match = N11CategoryCommissionImporter.Match(parse.Rows, BuildCategories());

        match.Matches.ShouldBeEmpty();
        match.Unmatched.Count.ShouldBe(2);
    }

    [Fact]
    public void Match_reports_conflicting_rates_for_same_leaf()
    {
        var tsv = Header + "\n" +
            "Ayakkabı & Çanta\tAyakkabı Bakım Ürünleri\tAyakkabı Bağcığı\tAyakkabı Bağcığı\t21\t\t\t\n" +
            "Ayakkabı & Çanta\tAyakkabı Bakım Ürünleri\tAyakkabı Bağcığı\tAyakkabı Bağcığı\t25\t\t\t\n";
        var parse = N11CategoryCommissionImporter.ParseTsv(tsv);

        var match = N11CategoryCommissionImporter.Match(parse.Rows, BuildCategories());

        match.Matches.ShouldHaveSingleItem().Row.CommissionRate.ShouldBe(21m);   // ilk eşleşme kalır
        match.Conflicts.ShouldHaveSingleItem();
    }

    [Fact]
    public void Embedded_tsv_resource_is_present_and_parses_with_low_error_rate()
    {
        // Gömülü kaynak (deploy'da .claude klasörü yok) okunabilir olmalı; panel tablosu ~3700 satır.
        var content = N11CategoryCommissionImporter.ReadEmbeddedTsv();
        var parse = N11CategoryCommissionImporter.ParseTsv(content);

        parse.Rows.Count.ShouldBeGreaterThan(3000);
        parse.InvalidRows.Count.ShouldBeLessThan(parse.Rows.Count / 100);   // %1'den az biçim hatası
    }
}
