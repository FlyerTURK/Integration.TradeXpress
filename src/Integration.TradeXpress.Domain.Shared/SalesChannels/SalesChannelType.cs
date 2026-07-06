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
}
