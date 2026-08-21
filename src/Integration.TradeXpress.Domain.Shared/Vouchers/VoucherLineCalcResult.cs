namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="VoucherLineCalculator"/> çıktısı: hesaplanan ana ve pay tarafı değerleri
/// (<c>Total</c> / <c>PayTotal</c>) + UI durum ipuçları. Karar UI'da değildir — motor üretir, UI yalnız render eder
/// (readonly kilit, pay-combo kaynağı, giriş/çıkış işareti).
/// </summary>
public sealed record VoucherLineCalcResult(
    decimal            Amount,
    decimal            Factor,
    decimal            Total,
    decimal            PayFactor,
    decimal            MarketPrice,
    decimal            PayTotal,
    decimal            Profit,
    bool               PayFactorReadOnly,
    bool               PayTotalReadOnly,
    PayCommoditySource PayCommoditySource,
    bool               IsInflow);
