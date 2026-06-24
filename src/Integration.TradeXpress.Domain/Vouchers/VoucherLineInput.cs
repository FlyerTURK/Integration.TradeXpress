using System;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="VoucherLine"/> oluşturma/güncelleme taşıyıcısı (Domain içi). Hesaplanmış
/// iki-bacak değerlerini + sınıflandırmayı taşır; entity ctor/Set bunu tüketir.
/// Snapshot alanları (CommodityCode/PayCommodityCode) relational değil, gösterim içindir.
/// </summary>
public sealed record VoucherLineInput(
    ProcessType          Type,
    ProcessDirectionType Direction,
    ProcessPaymentType?  PaymentType,
    Guid?                CommodityId,
    string               CommodityCode,
    decimal              Quantity,
    decimal              Amount,
    decimal              Factor,
    decimal              Total,
    Guid                 MainUnitId,
    decimal              PayFactor,
    decimal              MarketPrice,
    decimal              PayTotal,
    decimal              Profit,
    Guid?                PayCommodityId,
    string?              PayCommodityCode,
    Guid?                PayUnitId,
    decimal              PayUnitRate,
    DateTime?            DueDate,
    string?              Description);
