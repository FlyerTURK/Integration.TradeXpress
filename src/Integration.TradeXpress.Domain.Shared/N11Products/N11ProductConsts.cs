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

    /// <summary>N11 satıcı notu (sellerNote) — kanal-özel kısa düz metin (MemoEdit).</summary>
    public const int SellerNoteMaxLength = 500;

    /// <summary>N11 kanal-özel açıklama (description; HTML — DxHtmlEditor). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public const int DescriptionMaxLength = 20000;

    // Grup ürün (SaveProduct opsiyonel): aynı grup üyeleri groupItemCode'da eşleşir, groupAttribute grubu ayıran
    // özellik adı (ör. "Renk"), itemName grup içindeki öğe adı. N11 belgesi net üst sınır vermez → makul limit.
    public const int GroupItemCodeMaxLength = 64;
    public const int GroupAttributeMaxLength = 64;
    public const int ItemNameMaxLength = 128;

    /// <summary>Varyant SKU stok kodu ("{VaryantKodu}-{SequenceNo}") — satıcı-geneli benzersiz N11 kimliği.</summary>
    public const int StockCodeMaxLength = 128;

    // Attribute / özel bilgi (owned → JSON) alan sınırları.
    public const int CategoryAttributeNameMaxLength = 256;
    public const int CategoryAttributeValueMaxLength = 4000;
    public const int SpecialInfoKeyMaxLength = 64;
    public const int SpecialInfoValueMaxLength = 20000;   // HTML olabilir

    // Kanal-özel varyant ÖZELLİĞİ/DEĞERİ (SalesChannelTrN11ProductAttribute/Value) — ERP ProductAttributeConsts
    // (Products/ProductAttributeConsts.cs) ile HİZALI (klon-sonra-ayrış deseni; aynı alan sınırları).
    public const int AttributeNameMaxLength = 64;    // ör. "Renk", "Beden"
    public const int AttributeValueMaxLength = 128;  // ör. "Kırmızı", "Siyah"

    /// <summary>Kartezyen kombinasyon imzası ("{AttributeId}={ValueId}|...") üst sınırı — makul özellik sayısı × Guid uzunluğu.</summary>
    public const int CombinationSignatureMaxLength = 600;
}
