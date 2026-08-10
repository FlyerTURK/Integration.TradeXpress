namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Bir gönderimin TÜRÜ — KANAL-AGNOSTİK. Delil değerini belirler: tam push başlık/görsel de gönderir,
/// fiyat/stok senkronu yalnız adet/fiyat.
///
/// <para><b>Neden nötr bir enum:</b> defterin okuma yüzeyi birleşiktir (tek grid, üç kanal). Kanalın kendi
/// enum'unu (<c>N11ProductPushKind</c> / <c>TrendyolProductPushKind</c>) ekrana taşımak, UI'ı kanal başına
/// dallandırır ve aynı anlamı iki farklı isimle gösterirdi (N11 <c>FullPush</c> ↔ Trendyol <c>Create</c>).
/// Kanal-özel enum'lar YAZMA tarafında yerinde kalır; bu enum yalnız OKUMA modelinin dilidir.</para>
/// </summary>
public enum ChannelPushKind : byte
{
    /// <summary>Tam ürün gönderimi — başlık/görsel/nitelik dâhil (N11 <c>FullPush</c>, Trendyol <c>Create</c>).</summary>
    FullPush = 0,

    /// <summary>Yalnız fiyat/stok senkronu — içerik değişmez. Otonom repricing turlarının ürettiği tür budur.</summary>
    PriceStockSync = 1,

    /// <summary>Yalnız içerik güncelleme; fiyat ve stok değişmez. Bugün yalnız Trendyol üretir.</summary>
    ContentUpdate = 2,
}
