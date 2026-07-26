namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo şablonu kargo ödeme tipi (<c>deliveryFeeType</c>) — <b>CANLI API ile doğrulanmış</b> (2026-07-26):
/// yalnız <b>2 ve 3</b> kabul edilir. Tip 1 push'ta reddediliyor:
/// <i>"Delivery fee type alanı 2 veya 3 ile tanımlanabilir."</i>
///
/// <para><b>İş sonucu:</b> N11'de kargo bedelini ALICIYA yıkma seçeneği YOKTUR — bedel satıcıya aittir (şartlı
/// kargoda eşiğin altındaki siparişte alıcı öder, ama şablon yine satıcı-öder ailesindedir). Dolayısıyla N11
/// kanalında kargo maliyeti DAİMA reçeteye girer; "alıcı öderse fiyata ekleme" kuralı yalnız diğer kanallar
/// (Trendyol/Etsy/kendi site) için anlamlıdır.</para>
///
/// <para><b>Kaldırılanlar</b> (v4.6 dokümanında geçiyordu, canlıda yok): <c>BuyerPays=1</c> — push reddediyor;
/// <c>N11Pays=4</c> — hata mesajı yalnız 2/3'e izin verdiğinden yazma tarafında karşılığı yok.</para>
/// </summary>
public enum N11DeliveryFeeType : byte
{
    /// <summary>Mağaza (satıcı) öder.</summary>
    SellerPays = 2,

    /// <summary>Şartlı kargo (ör. X TL üstü ücretsiz) — mağaza öder.</summary>
    Conditional = 3,
}
