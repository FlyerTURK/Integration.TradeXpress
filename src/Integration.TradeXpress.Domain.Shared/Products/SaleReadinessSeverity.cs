namespace Integration.TradeXpress.Products;

/// <summary>
/// Satışa-hazırlık issue'unun ağırlığı (2026-08-19 ürün satışa hazırlık paneli). Üç kademe — ikisi değil:
/// <see cref="Error"/> doğrulamayı DURDURUR (o varyant/ürün <c>Ready</c> yapılmaz), <see cref="Warning"/> doğrulamayı
/// durdurmaz ama sonuçta ve satışa hazırlık panelinde görünür, <see cref="Info"/> yalnız bilgidir ("push'ta gerekecek").
/// <para><b>KDV bu ölçekte asla Error değildir</b> (2026-08-19 Hakan: "KDV'nin sistemimizde çok da önemi yoktur") —
/// push zinciri KDV'siz gönderimi reddetse bile doğrulama KDV yüzünden engellenmez; en fazla Warning.</para>
/// </summary>
public enum SaleReadinessSeverity : byte
{
    Info = 0,
    Warning = 1,
    Error = 2,
}
