using System.Text;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Aksan/büyük-küçük duyarsız arama anahtarı üretir: "Çiçek" → "cicek", "cicek" → "cicek".
///
/// <para><b>Neden var:</b> DevExpress grid'inin yerleşik araması harfi harfine eşleştiriyor — İngilizce
/// klavyeyle "cicek" yazan kullanıcı "Çiçek" kategorisini bulamıyordu (2026-07-28 Hakan).</para>
///
/// <para><b>Neden Framework'te:</b> aksan-duyarsız arama uygulamaya değil arayüze ait genel bir ihtiyaç;
/// uygulama katmanındaki eşdeğeri (N11 kategori adları) sunucu tarafında yaşıyor ve Framework ona bağlı
/// değil. İkisi aynı kuralı uygular: Türkçe harfler ASCII tabanına iner, sonuç küçük harftir.</para>
///
/// <para>Dönüşüm char-map ile TEK GEÇİŞTE yapılır — <c>ToLower</c> + kültür karşılaştırması "I/ı/İ/i"
/// tuzağına düşüyor ve kültüre göre farklı sonuç veriyor.</para>
/// </summary>
public static class SearchTextNormalizer
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
                'ı' or 'I' or 'İ' or 'i' or 'î' or 'Î' or 'ï' or 'Ï' => 'i',
                'ü' or 'Ü' or 'u' or 'U' or 'û' or 'Û' => 'u',
                'ö' or 'Ö' or 'o' or 'O' or 'ô' or 'Ô' => 'o',
                'ç' or 'Ç' or 'c' or 'C' => 'c',
                'ş' or 'Ş' or 's' or 'S' => 's',
                'ğ' or 'Ğ' or 'g' or 'G' => 'g',
                'â' or 'Â' or 'a' or 'A' or 'ä' or 'Ä' => 'a',
                'é' or 'É' or 'è' or 'È' or 'ê' or 'Ê' or 'e' or 'E' => 'e',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return sb.ToString();
    }

    /// <summary>Metin, arama terimini AKSANSIZ olarak içeriyor mu. Terim boşsa <c>true</c> (filtre yok).</summary>
    public static bool Matches(string? text, string? term)
    {
        var normalizedTerm = Normalize(term);
        return normalizedTerm.Length == 0 || Normalize(text).Contains(normalizedTerm, System.StringComparison.Ordinal);
    }
}
