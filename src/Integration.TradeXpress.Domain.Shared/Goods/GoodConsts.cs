namespace Integration.TradeXpress.Goods;

/// <summary>Good (Mamül — genel amaçlı ticari mal / perakende stok kartı) alan sınırları.</summary>
public static class GoodConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 4000;  // marketplace açıklaması — uzun/HTML (Product ile hizalı)
    public const int AttributeMaxLength   = 64;   // Marka/Model/Cins/Tür/Renk/Beden/Kategori/Grup
    public const int BarcodeMaxLength     = 64;
    public const int StockUnitMaxLength   = 32;   // Stok birimi (SpecialCode kodu: adet/kilo/cm…)

    public const int PricePrecision = 18;
    public const int PriceScale     = 5;

    // Adet/miktar sınırları (Min/Max stok).
    public const int QuantityPrecision = 18;
    public const int QuantityScale     = 3;

    // Vergi oranları (% — KDV alış/satış, ÖTV, tevkifat).
    public const int RatePrecision = 9;
    public const int RateScale     = 4;

    // Görseller (owned JSON — Product.Images deseni; çoklu).
    public const int MaxImageCount        = 10;
    public const int ImageUrlMaxLength    = 2048;
    public const int ImageBlobNameMaxLength = 256;
    public const int ImageFileNameMaxLength = 256;

    // Varyant sistemi artık AGNOSTİK (EntityVariantConsts) — Good tipli varyant sabitleri kaldırıldı.
}
