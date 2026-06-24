using System;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="VoucherLineCalculator"/> girdisi (UI-agnostik, saf). Kurlar burada
/// DEĞİL — çağıran <c>buyRateOf</c> delegesiyle sağlar (client: lookup cache;
/// sunucu: ExchangeRate). Böylece motor infra'sız kalır.
/// </summary>
public sealed record VoucherLineCalcInput(
    ProcessType          ProcessType,
    ProcessDirectionType Direction,
    ProcessPaymentType?  PaymentType,
    Guid?                MainUnitId,
    Guid?                PayUnitId,
    decimal              Amount,
    decimal              Factor,
    decimal              Total,
    decimal              PayFactor,
    decimal              PayTotal,
    decimal              MarketPrice,
    EditedField          EditedField);
