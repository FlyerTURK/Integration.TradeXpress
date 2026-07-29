using Integration.Framework.Blazor.Client.Components.Crud;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// Lookup popup aramasının aksan/case duyarsızlığı.
///
/// <para><b>Neden var:</b> İngilizce klavyeyle "cicek" yazan kullanıcı "Çiçek" kategorisini bulamıyordu
/// (2026-07-28 Hakan). Kural sessizce bozulabilecek türden — bir <c>ToLower()</c> refactor'ü Türkçe "I/ı/İ"
/// tuzağına düşürüp aramayı kültüre bağımlı hale getirir.</para>
/// </summary>
public class SearchTextNormalizerTests
{
    [Theory]
    [InlineData("Çiçek", "cicek")]
    [InlineData("ÇİÇEK", "cicek")]
    [InlineData("Gümüş", "gumus")]
    [InlineData("Yüzük", "yuzuk")]
    [InlineData("Işıl", "isil")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Ağırlık", "agirlik")]
    public void Normalizes_turkish_letters_to_ascii_lowercase(string input, string expected)
    {
        SearchTextNormalizer.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Çiçek Sepeti", "cicek")]
    [InlineData("Çiçek Sepeti", "çiçek")]
    [InlineData("Takı › Yüzük › Alyans", "yuzuk")]
    [InlineData("Takı › Yüzük › Alyans", "YÜZÜK")]
    public void Matches_ignores_accents_and_case_in_both_directions(string text, string term)
    {
        SearchTextNormalizer.Matches(text, term).ShouldBeTrue();
    }

    [Fact]
    public void Empty_term_matches_everything()
    {
        // Arama kutusu boşken filtre uygulanmaz — tüm liste görünür.
        SearchTextNormalizer.Matches("Çiçek", string.Empty).ShouldBeTrue();
        SearchTextNormalizer.Matches("Çiçek", null).ShouldBeTrue();
    }

    [Fact]
    public void Non_matching_term_is_rejected()
    {
        SearchTextNormalizer.Matches("Çiçek", "yuzuk").ShouldBeFalse();
    }
}
