namespace Integration.TradeXpress.Shipments;

/// <summary>Kargo şablonu (ERP-level, birleşik) alan uzunluk sabitleri. Min uzunluklar merkezî
/// <see cref="EntityFieldConsts"/>'ta (Code/Name/Description). Kanal-özel N11 alanları AYRIDIR
/// (bkz. <c>N11ShipmentConsts</c>); bu şablon kanal-nötr çekirdektir.</summary>
public static class ShipmentTemplateConsts
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    /// <summary>Kargo firması serbest adı (opsiyonel; ortak/paylaşılan kavram).</summary>
    public const int CarrierNameMaxLength = 128;

    /// <summary>İade koşulları/açıklaması serbest metni (opsiyonel).</summary>
    public const int ReturnInfoMaxLength = 1024;

    /// <summary>Şartlı kargo eşiği (ConditionalThreshold) decimal precision — emtia/parasal precision'la hizalı.</summary>
    public const int ThresholdPrecision = 18;
    public const int ThresholdScale = 2;
}
