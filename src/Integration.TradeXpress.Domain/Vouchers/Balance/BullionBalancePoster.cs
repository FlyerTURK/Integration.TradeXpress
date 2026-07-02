using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Takoz (<see cref="ProcessType.Bullion"/>) satırının bakiye etkisi. Bacaklar <see cref="BullionLegCalculator"/>
/// tarafından YÖN İŞARETLİ üretilir → poster ek işaret UYGULAMAZ, yalnız birimlere dağıtır:
/// raporsuz→tek pseudo bacak (MainUnit), raporlu→altın(MainUnit)+gümüş/platin/paladyum(yan birimler)+işçilik(PayUnit).
/// Kur snapshot'ları satırda saklı (kayıt anı). 4 metal de işçilikli (PT/PD ERPPROV3'te yok — eklendi).
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class BullionBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Bullion;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        var legs = BullionLegCalculator.ComputeBullion(new BullionLegInput(
            Direction:              line.Direction,
            IsReport:               line.IsReport ?? false,
            Amount:                 line.Amount,
            AssayAmount:            line.AssayAmount ?? 0m,
            GoldFactor:             line.Factor,                  // altın milyemi
            SilverFactor:           line.SilverFactor ?? 0m,
            PlatinumFactor:         line.PlatinumFactor ?? 0m,
            PalladiumFactor:        line.PalladiumFactor ?? 0m,
            SilverMode:             line.SilverMode,
            PlatinumMode:           line.PlatinumMode,
            PalladiumMode:          line.PalladiumMode,
            GoldLaborRate:          line.PayFactor,               // altın işçilik fiyatı (mevcut alan)
            SilverLaborRate:        line.SilverLaborRate ?? 0m,
            PlatinumLaborRate:      line.PlatinumLaborRate ?? 0m,
            PalladiumLaborRate:     line.PalladiumLaborRate ?? 0m,
            GoldRate:               line.GoldRate ?? 0m,
            SilverRate:             line.SilverRate ?? 0m,
            PlatinumRate:           line.PlatinumRate ?? 0m,
            PalladiumRate:          line.PalladiumRate ?? 0m,
            PayUnitRate:            line.PayUnitRate,             // işçilik tahsil birimi kuru (mevcut alan)
            GoldLaborUnitRate:      line.GoldLaborUnitRate ?? 0m,
            SilverLaborUnitRate:    line.SilverLaborUnitRate ?? 0m,
            PlatinumLaborUnitRate:  line.PlatinumLaborUnitRate ?? 0m,
            PalladiumLaborUnitRate: line.PalladiumLaborUnitRate ?? 0m));

        if (legs.UnreportedTotal != 0m && line.MainUnitId != Guid.Empty)
            yield return new BalanceEffect(line.MainUnitId, legs.UnreportedTotal);
        if (legs.GoldTotal != 0m && line.MainUnitId != Guid.Empty)
            yield return new BalanceEffect(line.MainUnitId, legs.GoldTotal);
        if (legs.SilverTotal != 0m && line.SilverUnitId is { } gum)
            yield return new BalanceEffect(gum, legs.SilverTotal);
        if (legs.PlatinumTotal != 0m && line.PlatinumUnitId is { } plt)
            yield return new BalanceEffect(plt, legs.PlatinumTotal);
        if (legs.PalladiumTotal != 0m && line.PalladiumUnitId is { } pld)
            yield return new BalanceEffect(pld, legs.PalladiumTotal);
        if (legs.LaborTotal != 0m && line.PayUnitId is { } labor)
            yield return new BalanceEffect(labor, legs.LaborTotal);
    }
}
