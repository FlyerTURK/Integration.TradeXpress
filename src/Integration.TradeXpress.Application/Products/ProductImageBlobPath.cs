using System.Linq;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün görseli blob adının PATH ön-ekini üretir — blob adı artık anlamlı bir DB anahtarı
/// ("Products/{ÜrünKodu}[/{VaryantKodu}]/GORSEL0001.ext"); diskte klasör değil, blob provider'da (Database)
/// düz bir key. Import ve upload akışları ORTAK bu helper'ı kullanır (DRY, tek kural).
/// </summary>
public static class ProductImageBlobPath
{
    private const string Root = "Products";

    /// <summary>Blob klasör ön-eki (trailing slash YOK). Varyant kodu boşsa ürün-geneli
    /// ("Products/{ÜrünKodu}"); doluysa varyant-seviyesi ("Products/{ÜrünKodu}/{VaryantKodu}").</summary>
    public static string Folder(string productCode, string? variantCode)
    {
        var folder = Root + "/" + Seg(productCode);
        var variant = Seg(variantCode);
        if (variant.Length > 0)
        {
            folder = folder + "/" + variant;
        }

        return folder;
    }

    /// <summary>Path segmenti temizler — '/' , '\' ve kontrol karakterleri atılır (path'i bozmasın);
    /// boşluk KORUNUR, baş/son boşluk trim'lenir. Kod normalize kuralıyla (boşluk korunur) hizalı.</summary>
    private static string Seg(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var cleaned = new string(raw
            .Where(c => c != '/' && c != '\\' && !char.IsControl(c))
            .ToArray());
        return cleaned.Trim();
    }
}
