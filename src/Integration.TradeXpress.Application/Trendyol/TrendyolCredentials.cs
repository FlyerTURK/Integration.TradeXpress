namespace Integration.TradeXpress.Trendyol;

/// <summary>
/// Trendyol API kimliği (per-kanal) — bir <see cref="SalesChannels.SalesChannelTrTrendyol"/> kaydından çözülür.
/// <see cref="SellerId"/> path'e ve zorunlu <c>User-Agent</c>'a girer; <see cref="ApiKey"/>/<see cref="ApiSecret"/>
/// Basic auth üretir. MERKEZİ tek kimlik YOK — her çağrı ilgili company'nin kendi kanal kaydından kimliği çözer
/// (eleştiri F-kimlik). Sir ASLA loglanmaz.
/// </summary>
public sealed record TrendyolCredentials(string SellerId, string ApiKey, string ApiSecret);
