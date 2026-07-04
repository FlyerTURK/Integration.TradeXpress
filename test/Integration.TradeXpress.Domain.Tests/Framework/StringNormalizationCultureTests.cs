using System;
using System.Globalization;
using Shouldly;
using Xunit;

namespace Integration.Framework;

/// <summary>
/// <see cref="StringNormalizationExtensions.NormalizeAsCode"/>'un KÜLTÜR-BAĞIMSIZ olduğunu mekanik
/// zorlayan golden testler. Türkçe İ/i tuzağı: kültür-duyarlı ToUpper "istanbul"ı tr-TR iş parçacığında
/// "İSTANBUL", en-US'ta "ISTANBUL" yapıp aynı mantıksal kodu iki kez kaydedilebilir kılardı (benzersizlik
/// yalnız DB unique index'e kalırdı). Bu testler GEÇmeli; regresyon (CurrentUICulture'a dönüş) KIRMIZI olmalı.
/// (Name tarafı = görünen ad, DB'de unique index yok → kültüre-duyarlı TitleCase kasıtlı korunur, burada test edilmez.)
/// </summary>
public class StringNormalizationCultureTests
{
    // Aşağıdaki tüm girdiler her iki iş parçacığı kültüründe AYNI çıktı vermeli. tr-TR'de kültür-duyarlı
    // upper 'i'→'İ' / 'ı'→'I' çatallardı; invariant her ikisini de kültürden bağımsız çözer.
    [Theory]
    [InlineData("istanbul")]
    [InlineData("izmir")]
    [InlineData("i")]
    [InlineData("I")]
    [InlineData("ı")]
    [InlineData("İ")]
    [InlineData("ışık akı")]
    [InlineData("iğdır bağ")]
    public void NormalizeAsCode_produces_same_result_in_tr_and_en(string raw)
    {
        var underTr = RunUnderCulture("tr-TR", () => raw.NormalizeAsCode());
        var underEn = RunUnderCulture("en-US", () => raw.NormalizeAsCode());
        underTr.ShouldBe(underEn);
    }

    [Fact]
    public void NormalizeAsCode_istanbul_uppercases_to_ascii_in_every_culture()
    {
        // Tuzağın kalbi: küçük noktalı 'i' → ASCII 'I' (kültür-duyarlı 'İ' DEĞİL), her iki kültürde.
        RunUnderCulture("tr-TR", () => "istanbul".NormalizeAsCode()).ShouldBe("ISTANBUL");
        RunUnderCulture("en-US", () => "istanbul".NormalizeAsCode()).ShouldBe("ISTANBUL");
    }

    [Fact]
    public void NormalizeAsCode_lowercase_i_maps_to_ascii_I_not_dotted_capital()
    {
        // tr-TR CurrentUICulture ToUpper 'i'yi 'İ' (U+0130) yapardı; invariant ASCII 'I' (U+0049) verir.
        var result = RunUnderCulture("tr-TR", () => "i".NormalizeAsCode());
        result.ShouldBe("I");
        result[0].ShouldBe('I');       // düz ASCII 'I'
        result.ShouldNotContain('İ');  // noktalı 'İ' (tr-upper çıktısı) ASLA sızmamalı
    }

    // Belirli bir kültürü geçici olarak iş parçacığına uygulayıp action'ı çalıştırır, sonra eski kültürü geri yükler.
    private static string RunUnderCulture(string cultureName, Func<string> action)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
