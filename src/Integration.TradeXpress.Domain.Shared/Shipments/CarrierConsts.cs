namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Kargo firması (host-global çekirdek referans) alan uzunluk sabitleri. Kanal firma sabitleriyle
/// (<c>N11ShipmentConsts</c>) hizalı: <see cref="Carrier.Code"/> N11 ShortName'den (32), <see cref="Carrier.Name"/>
/// N11 Name'den (128) türer. Kanal-nötr çekirdek; kanal-özel alanlar AYRIDIR.
/// </summary>
public static class CarrierConsts
{
    /// <summary>Kaynak/kısa kod (N11 ShortName'den türer, ör. ARAS/YK) — kültür-bağımsız UPPER.</summary>
    public const int CodeMaxLength = 32;

    public const int NameMaxLength = 128;
}
