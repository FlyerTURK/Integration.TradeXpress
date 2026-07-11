namespace Integration.TradeXpress.Products;

/// <summary>
/// Kanal <b>türetilmiş fiyat</b> formülünün TEK kaynağı (SSOT — N11/Trendyol AppService'leri + Blazor client
/// aynı formülü paylaşır; kopya formül yasak): <c>fiyat = NetCost × (1 + Margin/100)</c> [MARKUP].
/// Margin null = marjsız (×1). Yuvarlama YAPMAZ — çağıranlar mevcut davranışla (ham değer) birebir hizalı.
/// </summary>
public static class DerivedPriceCalculator
{
    public static decimal Calculate(decimal netCost, decimal? margin)
    {
        return netCost * (1m + (margin ?? 0m) / 100m);
    }
}
