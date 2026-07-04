using System.Globalization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Financials;

/// <summary>
/// <see cref="FinancialRounding"/> birim testleri — kalıcılaşan finansal değerlerin tek yuvarlama
/// noktası. Politika (ERPPRO ground-truth, SQL implicit ROUND paritesi): tutar N2, milyem/kur N5,
/// MidpointRounding.AwayFromZero (0.005 → 0.01, −0.005 → −0.01; banker's/ToEven DEĞİL).
/// </summary>
public class FinancialRoundingTests
{
    [Theory]
    // Midpoint: AwayFromZero → sıfırdan uzağa (ToEven olsaydı 0.005 → 0.00 olurdu).
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("1.125", "1.13")]      // ToEven 1.12 verirdi — AwayFromZero ayrıştırıcı senaryo
    [InlineData("-1.125", "-1.13")]
    [InlineData("2.675", "2.68")]
    // Midpoint olmayan normal yuvarlama.
    [InlineData("9.165", "9.17")]
    [InlineData("-9.165", "-9.17")]
    [InlineData("10.994", "10.99")]
    [InlineData("10.996", "11.00")]
    // Zaten N2 → değişmez.
    [InlineData("100.25", "100.25")]
    [InlineData("0", "0")]
    [InlineData("-33.05", "-33.05")]
    public void RoundAmount_rounds_to_two_decimals_away_from_zero(string raw, string expected)
    {
        FinancialRounding.RoundAmount(decimal.Parse(raw, CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    // Midpoint: 6. hane tam 5 → sıfırdan uzağa.
    [InlineData("0.000005", "0.00001")]
    [InlineData("-0.000005", "-0.00001")]
    [InlineData("0.916125", "0.91613")]    // ToEven 0.91612 verirdi
    [InlineData("-0.916125", "-0.91613")]
    // Midpoint olmayan.
    [InlineData("0.9161234", "0.91612")]
    [InlineData("47.303456", "47.30346")]
    // Zaten N5 → değişmez.
    [InlineData("0.91600", "0.916")]
    [InlineData("6132", "6132")]
    public void RoundRate_rounds_to_five_decimals_away_from_zero(string raw, string expected)
    {
        FinancialRounding.RoundRate(decimal.Parse(raw, CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }
}
