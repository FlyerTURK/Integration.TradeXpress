namespace Integration.TradeXpress.Products;

/// <summary>Ürün özelleştirme alanı — owned (Product.SpecialInfo → JSON kolonu). Satıcı yalnız <see cref="Key"/>'i
/// (müşteri giriş alanı etiketi, ör. "Üst Yazı") tanımlar; müşteri değeri sipariş anında doldurur → <see cref="Value"/>
/// satıcıda OPSİYONEL (varsayılan/örnek). Pazaryeri-genel varsayılan; kanal-ürünü boşsa bunu devralır (N11
/// specialProductInfoList deseninin ürün-genel karşılığı).</summary>
public class ProductSpecialInfo
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public ProductSpecialInfo()
    {
    }

    public ProductSpecialInfo(string key, string value)
    {
        Key = key;
        Value = value;
    }
}
