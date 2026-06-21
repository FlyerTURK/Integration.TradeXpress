using System.Globalization;
using System.Text.RegularExpressions;

namespace Integration.Framework;

/// <summary>
/// Kimlik alanları (Code/Name) için merkezî string normalizasyonu — her zaman elimizin altında
/// (extension). Kültür duyarlı (<see cref="CultureInfo.CurrentUICulture"/>) ki kullanıcı kendi
/// dilinde rahat etsin. Doğrulama (min/max/empty) BURADA DEĞİL → <see cref="StringFieldGuard"/>.
/// </summary>
public static class StringNormalizationExtensions
{
    /// <summary>Code normalizasyonu: Trim · çoklu boşluk→tek · boşluk→<c>_</c> · BÜYÜK harf (CurrentUICulture).</summary>
    public static string NormalizeAsCode(this string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(raw.Trim());
        return collapsed.Replace(' ', '_').ToUpper(CultureInfo.CurrentUICulture);
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
