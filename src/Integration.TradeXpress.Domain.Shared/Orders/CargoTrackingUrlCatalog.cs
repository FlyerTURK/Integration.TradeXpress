using System;
using System.Globalization;
using System.Text;

namespace Integration.TradeXpress.Orders;

/// <summary>Kargo firması adı → harici gönderi-sorgulama URL şablonu. Firma adı <b>contains</b> + büyük/küçük harf ve
/// Türkçe diakritik duyarsız eşlenir ("Yurtiçi" ≡ "Yurtici"). Bilinmeyen firma → <c>null</c> (link YOK, düz metin gösterilir).
/// Genişletme = tek satır (N11OrderStatusCatalog / kanal deseniyle hizalı). Nötr veri katmanı (Blazor/UI bağımlılığı yok;
/// resx değil — dile bağımsız URL) → hem Blazor UI hem sunucu tüketebilir; bu yüzden Domain.Shared'da, kargo/sipariş
/// katalogları N11OrderStatusCatalog ile aynı yerde.</summary>
public static class CargoTrackingUrlCatalog
{
    // Normalize edilmiş firma-adı parçası (küçük harf, diakritiksiz) → URL şablonu ({0} = takip no).
    // ŞİMDİLİK yalnız Yurtiçi Kargo; yeni firma = bu diziye tek satır.
    private static readonly (string Needle, string UrlTemplate)[] Carriers =
    {
        ("yurtici", "https://www.yurticikargo.com/tr/online-servisler/gonderi-sorgula?code={0}"),
    };

    /// <summary>Firma adı + takip no → harici takip URL'si; firma çözülemez ya da takip no boşsa <c>null</c>.</summary>
    public static string? ResolveTrackingUrl(string? carrierName, string? trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(carrierName) || string.IsNullOrWhiteSpace(trackingNumber))
        {
            return null;
        }

        var haystack = Normalize(carrierName);
        var code = StripSeparators(trackingNumber);
        if (code.Length == 0)
        {
            return null;
        }

        foreach (var (needle, template) in Carriers)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal))
            {
                return string.Format(CultureInfo.InvariantCulture, template, code);
            }
        }

        return null;
    }

    // Takip no'yu sorgulama için sadeleştir: boşluk ve tire çıkar (12 hane, ayraçsız beklenir).
    private static string StripSeparators(string trackingNumber)
    {
        var sb = new StringBuilder(trackingNumber.Length);
        foreach (var ch in trackingNumber)
        {
            if (!char.IsWhiteSpace(ch) && ch != '-')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    // Türkçe diakritikleri sadeleştir + küçük harfe indir ("Yurtiçi" ve "Yurtici" aynı needle'a eşleşsin).
    private static string Normalize(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            sb.Append(ch switch
            {
                'ç' => 'c',
                'ğ' => 'g',
                'ı' => 'i',
                'ö' => 'o',
                'ş' => 's',
                'ü' => 'u',
                _ => ch,
            });
        }

        return sb.ToString();
    }
}
