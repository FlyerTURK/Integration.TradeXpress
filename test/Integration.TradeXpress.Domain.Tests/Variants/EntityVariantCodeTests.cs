using System;
using System.Globalization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// <see cref="EntityVariantSynchronizer.BuildVariantCode"/> Türkçe-farkında büyütme golden testleri.
/// Varyant kodu Türkçe nitelik değerlerinden türer + kullanıcıya SKU olarak görünür → ı→I (noktasız), i→İ (noktalı).
/// Invariant büyütme "Kırmızı"yı "KıRMıZı" (ı büyümez), "Yeşil"i "YEŞIL" (i→I noktasız) yapardı — Türkçe için bozuk.
/// Dönüşüm DETERMİNİSTİK (thread kültüründen bağımsız): tr-TR ve en-US iş parçacığında AYNI çıktı → çatalsız benzersizlik
/// (konvansiyonun invariant'la sağladığı amaç korunur). Regresyon (invariant'a dönüş) KIRMIZI olmalı.
/// </summary>
public class EntityVariantCodeTests
{
    [Fact]
    public void BuildVariantCode_dotless_i_uppercases_to_ascii_I()
    {
        // "Kırmızı" (ı = dotless) → "KIRMIZI" (I = ASCII noktasız). Invariant "KıRMıZı" verirdi.
        EntityVariantSynchronizer.BuildVariantCode(new[] { "Kırmızı" }).ShouldBe("KIRMIZI");
    }

    [Fact]
    public void BuildVariantCode_dotted_i_uppercases_to_dotted_capital()
    {
        // "Yeşil" (i = dotted) → "YEŞİL" (İ = noktalı büyük). Invariant "YEŞIL" (I noktasız) verirdi.
        EntityVariantSynchronizer.BuildVariantCode(new[] { "Yeşil" }).ShouldBe("YEŞİL");
    }

    [Fact]
    public void BuildVariantCode_joins_values_with_dash()
    {
        EntityVariantSynchronizer.BuildVariantCode(new[] { "Kırmızı", "42" }).ShouldBe("KIRMIZI-42");
        EntityVariantSynchronizer.BuildVariantCode(new[] { "Mavi", "XL" }).ShouldBe("MAVİ-XL");
    }

    [Fact]
    public void BuildVariantCode_is_culture_independent()
    {
        // Aynı girdi tr-TR ve en-US iş parçacığında AYNI kodu vermeli (deterministik → benzersizlik çatallanmaz).
        var values = new[] { "Yeşil", "Kırmızı" };
        var underTr = RunUnderCulture("tr-TR", () => EntityVariantSynchronizer.BuildVariantCode(values));
        var underEn = RunUnderCulture("en-US", () => EntityVariantSynchronizer.BuildVariantCode(values));
        underTr.ShouldBe(underEn);
    }

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
