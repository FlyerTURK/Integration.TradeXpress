using System;
using Integration.TradeXpress.Bullions;

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
    string?              Description,

    // ── Takoz (Bullion) — diğer tiplerde null/default ──
    BullionType?         BullionType            = null,
    Guid?                AssayOfficeId          = null,
    string?              ReportNo               = null,
    bool?                IsReport               = null,
    bool?                IsExtra                = null,
    decimal?             AssayAmount            = null,
    decimal?             SilverFactor           = null,
    decimal?             PlatinumFactor         = null,
    decimal?             PalladiumFactor        = null,
    MetalDisposition?    SilverMode             = null,
    MetalDisposition?    PlatinumMode           = null,
    MetalDisposition?    PalladiumMode          = null,
    BullionLaborMode?    LaborMode              = null,
    decimal?             SilverLaborRate        = null,
    decimal?             PlatinumLaborRate      = null,
    decimal?             PalladiumLaborRate     = null,
    Guid?                GoldLaborUnitId        = null,
    Guid?                SilverLaborUnitId      = null,
    Guid?                PlatinumLaborUnitId    = null,
    Guid?                PalladiumLaborUnitId   = null,
    Guid?                SilverUnitId           = null,
    Guid?                PlatinumUnitId         = null,
    Guid?                PalladiumUnitId        = null,
    decimal?             GoldRate               = null,
    decimal?             SilverRate             = null,
    decimal?             PlatinumRate           = null,
    decimal?             PalladiumRate          = null,
    decimal?             GoldLaborUnitRate      = null,
    decimal?             SilverLaborUnitRate    = null,
    decimal?             PlatinumLaborUnitRate  = null,
    decimal?             PalladiumLaborUnitRate = null);
