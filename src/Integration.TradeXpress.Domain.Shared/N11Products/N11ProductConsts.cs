namespace Integration.TradeXpress.N11Products;

/// <summary>N11 ürün listeleme (SalesChannelTrN11Product) alan sınırları.</summary>
public static class N11ProductConsts
{
    /// <summary>N11 kategori/ürün id'si (numerik ama matematik yapılmaz → string).</summary>
    public const int ExternalIdMaxLength = 32;

    /// <summary>Kayıt-bazlı N11 upsert kimliği ("{ÜrünKodu}-{SequenceNo}"). Ürün kodu (32) + ek payı.</summary>
    public const int SellerCodeMaxLength = 64;

    public const int CategoryNameMaxLength = 512;
    public const int ShipmentTemplateNameMaxLength = 128;
    public const int StatusMaxLength = 32;
    public const int LastErrorMaxLength = 2000;

    /// <summary>Varyant SKU stok kodu ("{VaryantKodu}-{SequenceNo}") — satıcı-geneli benzersiz N11 kimliği.</summary>
    public const int StockCodeMaxLength = 128;

    // Attribute / özel bilgi (owned → JSON) alan sınırları.
    public const int AttributeNameMaxLength = 256;
    public const int AttributeValueMaxLength = 4000;
    public const int SpecialInfoKeyMaxLength = 64;
    public const int SpecialInfoValueMaxLength = 20000;   // HTML olabilir
}
