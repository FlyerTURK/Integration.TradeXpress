namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Satış kanalı TÜRÜ — TPT alt-tipini (somut pazaryeri) ayırt eder. Liste (polymorphic) ekranında "Tür" kolonu +
/// "Yeni ▾" tip seçimi + düzenlemede doğru tipe yönlendirme için kullanılır (kalıcı ayrım TPT tablosudur, bu enum
/// UI/DTO taşıyıcısıdır). Yeni pazaryeri = yeni <c>SalesChannel{Ülke}{Pazaryeri}</c> + buraya değer.
/// </summary>
public enum SalesChannelType : byte
{
    /// <summary>N11 (Türkiye) — <c>SalesChannelTrN11</c> (AppKey/AppSecret).</summary>
    TrN11 = 1,

    /// <summary>Trendyol (Türkiye) — <c>SalesChannelTrTrendyol</c> (SellerId/ApiKey/ApiSecret).</summary>
    TrTrendyol = 2,

    /// <summary>Etsy (global platform — ülke öneki YOK; ülke yalnız shop location) — <c>SalesChannelEtsy</c>
    /// (Keystring/SharedSecret + OAuth 2.0 PKCE token'ları).</summary>
    Etsy = 3,
}
