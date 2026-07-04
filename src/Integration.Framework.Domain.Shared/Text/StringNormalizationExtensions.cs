using System.Globalization;
using System.Text.RegularExpressions;

namespace Integration.Framework;

/// <summary>
/// Kimlik alanları (Code/Name) için merkezî string normalizasyonu — her zaman elimizin altında
/// (extension). Code büyütme KÜLTÜR-BAĞIMSIZ (<see cref="string.ToUpperInvariant"/>) — Türkçe İ/i
/// tuzağını önler (tr "istanbul"→İSTANBUL, en →ISTANBUL çatallanmasını kapatır; benzersizlik yalnız
/// DB unique index'e kalmaz). Name yalnızca görünen ad (DB'de unique index yok) → kültüre-duyarlı
/// TitleCase kalır. Doğrulama (min/max/empty) BURADA DEĞİL → <see cref="StringFieldGuard"/>.
/// </summary>
public static class StringNormalizationExtensions
{
    /// <summary>Code normalizasyonu: Trim · çoklu boşluk→tek · BÜYÜK harf (invariant, kültür-bağımsız).
    /// Boşluk KORUNUR (ürün kararı 2026-07-04: '_' dönüşümü kaldırıldı — kod "ANA KASA" gibi boşluklu yazılabilir).</summary>
    public static string NormalizeAsCode(this string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(raw.Trim());
        return collapsed.ToUpperInvariant();
    }

    /// <summary>Name normalizasyonu: Trim · çoklu boşluk→tek · her kelimenin ilk harfi büyük (TitleCase, CurrentUICulture).</summary>
    public static string NormalizeAsName(this string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(raw.Trim());
        return CultureInfo.CurrentUICulture.TextInfo.ToTitleCase(collapsed);
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value, @"\s+", " ");
    }
}
