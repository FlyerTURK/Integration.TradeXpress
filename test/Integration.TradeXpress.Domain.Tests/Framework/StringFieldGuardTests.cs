using Shouldly;
using Xunit;

namespace Integration.Framework;

/// <summary>
/// StringFieldGuard'ın YENİ eklenen iki kapısının testleri (framework reusable kod = test zorunlu):
/// NormalizeInvariantCode (ISO kod — kültür-bağımsız UPPER) ve EnsureRequiredText (serbest zorunlu metin).
/// </summary>
public class StringFieldGuardTests
{
    // ── NormalizeInvariantCode ────────────────────────────────────────────────

    [Theory]
    [InlineData("tr", "TR")]
    [InlineData(" us ", "US")]
    [InlineData("id", "ID")] // Kültür tuzağı: tr-TR ToUpper 'i'→'İ' yapardı; invariant 'I' bekliyoruz.
    public void NormalizeInvariantCode_trims_and_uppercases_invariant(string raw, string expected)
    {
        StringFieldGuard.NormalizeInvariantCode(raw, "Code", 2, 2).ShouldBe(expected);
    }

    [Fact]
    public void NormalizeInvariantCode_rejects_empty()
    {
        Should.Throw<RequiredPropertyException>(
            () => StringFieldGuard.NormalizeInvariantCode("   ", "Code", 2, 2));
    }

    [Fact]
    public void NormalizeInvariantCode_rejects_wrong_length()
    {
        // Sabit uzunluk (min = max = 2): tek harf kısa, üç harf uzun.
        Should.Throw<TooShortPropertyException>(
            () => StringFieldGuard.NormalizeInvariantCode("T", "Code", 2, 2));
        Should.Throw<TooLongPropertyException>(
            () => StringFieldGuard.NormalizeInvariantCode("TRY", "Code", 2, 2));
    }

    // ── EnsureRequiredText ────────────────────────────────────────────────────

    [Fact]
    public void EnsureRequiredText_trims_but_preserves_case()
    {
        // Serbest metin: case/boşluk normalizasyonu YOK (yalnız uç Trim).
        StringFieldGuard.EnsureRequiredText("  Haremaltin feed  ", "Source", 1, 64)
            .ShouldBe("Haremaltin feed");
    }

    [Fact]
    public void EnsureRequiredText_rejects_whitespace_only()
    {
        Should.Throw<RequiredPropertyException>(
            () => StringFieldGuard.EnsureRequiredText(" \t ", "Source", 1, 64));
    }

    [Fact]
    public void EnsureRequiredText_enforces_max_length()
    {
        Should.Throw<TooLongPropertyException>(
            () => StringFieldGuard.EnsureRequiredText("abcdef", "Source", 1, 5));
    }
}
