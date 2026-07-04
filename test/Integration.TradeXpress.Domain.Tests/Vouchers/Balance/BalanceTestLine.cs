using System;
using Integration.TradeXpress.Bullions;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Poster testleri için ortak <see cref="VoucherLine"/> fabrikası + sabit birim Id'leri.
/// Varsayılanlar nötrdür — her test yalnız ilgilendiği alanı kurar. Id'ler deterministik
/// (kalıcı-id değil, test sabiti).
/// </summary>
internal static class BalanceTestLine
{
    public static readonly Guid LineId    = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid VoucherId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static readonly Guid TryUnit   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UsdUnit   = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid HasUnit   = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid GumUnit   = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid PltUnit   = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid LaborUnit = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public static VoucherLine Create(
        ProcessType type,
        ProcessDirectionType direction = ProcessDirectionType.Inbound,
        ProcessPaymentType? paymentType = ProcessPaymentType.Normal,
        decimal amount = 0m,
        decimal factor = 0m,
        decimal total = 0m,
        Guid mainUnitId = default,
        decimal payFactor = 0m,
        decimal payTotal = 0m,
        Guid? payUnitId = null,
        decimal payUnitRate = 0m,
        bool? isReport = null,
        decimal? assayAmount = null,
        decimal? silverFactor = null,
        decimal? platinumFactor = null,
        MetalDisposition? silverMode = null,
        Guid? silverUnitId = null,
        Guid? platinumUnitId = null,
        decimal? goldLaborUnitRate = null)
    {
        return new VoucherLine(LineId, VoucherId, new VoucherLineInput(
            Type:              type,
            Direction:         direction,
            PaymentType:       paymentType,
            CommodityId:       null,
            CommodityCode:     "TEST",
            Quantity:          0m,
            Amount:            amount,
            Factor:            factor,
            Total:             total,
            MainUnitId:        mainUnitId,
            PayFactor:         payFactor,
            MarketPrice:       0m,
            PayTotal:          payTotal,
            Profit:            0m,
            PayCommodityId:    null,
            PayCommodityCode:  null,
            PayUnitId:         payUnitId,
            PayUnitRate:       payUnitRate,
            DueDate:           null,
            Description:       null,
            IsReport:          isReport,
            AssayAmount:       assayAmount,
            SilverFactor:      silverFactor,
            PlatinumFactor:    platinumFactor,
            SilverMode:        silverMode,
            SilverUnitId:      silverUnitId,
            PlatinumUnitId:    platinumUnitId,
            GoldLaborUnitRate: goldLaborUnitRate));
    }
}
