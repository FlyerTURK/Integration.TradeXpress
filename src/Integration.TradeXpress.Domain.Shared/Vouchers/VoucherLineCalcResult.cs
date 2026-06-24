namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="VoucherLineCalculator"/> çıktısı: hesaplanan iki-bacak değerleri +
/// UI durum ipuçları. Karar UI'da değildir — motor üretir, UI yalnız render eder
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
