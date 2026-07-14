namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik varyant sistemi (herhangi bir entity'ye EntityName+EntityId ile bağlı nitelik→değer→varyant) alan
/// sınırları + iş kuralı sabitleri. SpecialCode/EntityImage agnostik deseniyle hizalı; Product varyant sabitleri paritesi.
/// </summary>
public static class EntityVariantConsts
{
    /// <summary>Sahip entity tipi adı (ör. "Good", "Product", "Metal").</summary>
    public const int EntityNameMaxLength = 128;

    public const int AttributeNameMaxLength = 64;    // ör. "Renk", "Beden"
    public const int AttributeValueMaxLength = 128;  // ör. "Kırmızı", "42", "XL"

    /// <summary>Sahip entity başına en fazla nitelik sayısı.</summary>
    public const int MaxAttributesPerEntity = 5;

    // Varyant (nitelik×değer kombinasyonundan türetilen) — kod/ad OTOMATİK üretilir, ana entity kodundan geniş.
    public const int VariantCodeMaxLength = 64;
    public const int VariantNameMaxLength = 256;
    public const int BarcodeMaxLength = 64;
    public const int TradeIdentifierMaxLength = 64;   // GTIN/MPN/OEM (per-SKU ticari kimlikler)
    public const int DescriptionMaxLength = 4000;   // uzun/HTML (marketplace açıklaması)

    /// <summary>En-az-1 varyant kuralının seed'lediği ANA varyant kod/adı.</summary>
    public const string MainVariantCode = "ANAVARYANT";
    public const string MainVariantName = "Ana Varyant";
}
