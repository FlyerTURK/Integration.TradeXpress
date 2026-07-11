using System.Text;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori adı normalize yardımcısı (SSOT) — Türkçe aksanları ASCII tabanına indirger + küçük harfe çevirir
/// (tek geçiş; İ/ı/i tuzağını char-map ile atlar). "Kül"→"kul", "kul"→"kul" → aksan/case-duyarsız eşleşme.
/// Hem picker aramasında (<c>N11CategoryAppService.SearchLeafCategoriesAsync</c>) hem komisyon TSV eşlemesinde
/// (<c>N11CategoryCommissionImporter</c>) kullanılır.
/// </summary>
public static class N11NameNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            sb.Append(ch switch
            {
                'ı' or 'I' or 'İ' or 'i' or 'î' or 'Î' => 'i',
                'ü' or 'Ü' or 'u' or 'U' or 'û' or 'Û' => 'u',
                'ö' or 'Ö' or 'o' or 'O' => 'o',
                'ç' or 'Ç' or 'c' or 'C' => 'c',
                'ş' or 'Ş' or 's' or 'S' => 's',
                'ğ' or 'Ğ' or 'g' or 'G' => 'g',
                'â' or 'Â' or 'a' or 'A' => 'a',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return sb.ToString();
    }
}
