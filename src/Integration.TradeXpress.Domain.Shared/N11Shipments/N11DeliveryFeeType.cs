namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo şablonu kargo ödeme tipi (<c>deliveryFeeType</c>) — kaynak: resmî SOAP referans dokümanı v4.6
/// ("1 alıcı öder, 2 mağaza öder, 3 şartlı kargo, 4 N11 öder"). CreateOrUpdate isteği 1/2/3 kabul eder; Get
/// yanıtı 4'ü de dönebilir. (REST sipariş-tarafındaki 1/3/5 kodlaması AYRIDIR — şablonla ilgisi yoktur.)
/// </summary>
public enum N11DeliveryFeeType : byte
{
    /// <summary>Alıcı öder.</summary>
    BuyerPays = 1,

    /// <summary>Mağaza (satıcı) öder.</summary>
    SellerPays = 2,

    /// <summary>Şartlı kargo (ör. X TL üstü ücretsiz) — mağaza öder.</summary>
    Conditional = 3,

    /// <summary>N11 öder (yalnız Get yanıtında; şartlı kargonun N11-öder varyantı).</summary>
    N11Pays = 4,
}
