using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Querying;

/// <summary>
/// Aksan/harf katlama (fold) davranışını doğrular: ÜMRANİYE = ÜMRANıYE = Umraniye →
/// "umraniye"; "U" yazınca u/ü/û eşleşir. Hem saf <see cref="SearchNormalizer"/> hem
/// <see cref="ListQueryableExtensions"/>'ın in-memory (= aynı Expression) yolu test edilir.
/// </summary>
public class SearchFoldingTests
{
    // ── Saf normalizer ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("ÜMRANİYE", "umraniye")]
    [InlineData("ÜMRANıYE", "umraniye")]   // kasıtlı küçük ı
    [InlineData("Umraniye", "umraniye")]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("Çağrı", "cagri")]
    [InlineData("Gümüş", "gumus")]
    [InlineData("Şişli", "sisli")]
    // Türkçe ç ş ğ + büyük Ç Ş Ğ — hepsi tek testte
    [InlineData("Çç Şş Ğğ", "cc ss gg")]
    [InlineData("ÇAĞLAYAN", "caglayan")]
    [InlineData("Beşiktaş", "besiktas")]
    // İskandinav / Avrupa — harita genişletilince iki taraf da senkron katlar.
    [InlineData("Smørrebrød", "smorrebrod")]
    [InlineData("Blåbær", "blabaer")]
    [InlineData("Ångström", "angstrom")]
    [InlineData("Æøå", "aeoa")]
    [InlineData("Mañana", "manana")]
    [InlineData("Straße", "strasse")]
    public void Fold_maps_turkish_and_accents_to_ascii_lowercase(string input, string expected)
        => SearchNormalizer.Fold(input).ShouldBe(expected);

    [Fact]
    public void Fold_treats_u_family_alike()
    {
        // "U" / "u" / "ü" / "û" hepsi 'u'ya katlanır.
        SearchNormalizer.Fold("U").ShouldBe("u");
        SearchNormalizer.Fold("ü").ShouldBe("u");
        SearchNormalizer.Fold("Û").ShouldBe("u");
        SearchNormalizer.Fold("û").ShouldBe("u");
    }

    // ── Engine entegrasyonu (ApplyListRequest aynı fold Expression'ını çalıştırır) ──

    private sealed class City
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static IQueryable<City> Data() => new List<City>
    {
        new() { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "Ümraniye" },
        new() { Id = new Guid("00000000-0000-0000-0000-000000000002"), Name = "Umraniye" },
        new() { Id = new Guid("00000000-0000-0000-0000-000000000003"), Name = "İstanbul" },
        new() { Id = new Guid("00000000-0000-0000-0000-000000000004"), Name = "Şişli" },
    }.AsQueryable();

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "Id" };

    [Theory]
    [InlineData("umraniye")]
    [InlineData("UMRANIYE")]
    [InlineData("ÜMRANıYE")]   // kasıtlı küçük ı
    [InlineData("ümraniye")]
    public void Global_search_matches_both_spellings_regardless_of_input_form(string term)
    {
        var result = Data().ApplyListRequest(new ListRequestDto { Filter = term }, Allowed).ToList();

        // Hem "Ümraniye" hem "Umraniye" eşleşir.
        result.Select(c => c.Name).ShouldBe(new[] { "Ümraniye", "Umraniye" }, ignoreOrder: true);
    }

    [Fact]
    public void Column_filter_contains_is_also_folded()
    {
        var req = new ListRequestDto();
        req.Filters.Add(new FilterField
        {
            Field = "Name",
            Operator = ListFilterOperator.Contains,
            Value = "ŞİŞ"     // büyük + Türkçe → "sis"
        });

        var result = Data().ApplyListRequest(req, Allowed).ToList();
        result.ShouldHaveSingleItem().Name.ShouldBe("Şişli");
    }
}
