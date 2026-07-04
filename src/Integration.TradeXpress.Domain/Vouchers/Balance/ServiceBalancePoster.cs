using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Hizmet (<see cref="ProcessType.Service"/>) satırlarının bakiye etkisi (pay-leg, Nakit ile aynı alanlar):
/// Hizmet = Commodity; karşılık birim = <see cref="VoucherLine.PayUnitId"/>, tutar = <see cref="VoucherLine.PayTotal"/>.
/// <list type="bullet">
///   <item>Peşin (<see cref="ProcessPaymentType.WithCash"/>) → bakiyeye YANSIMAZ.</item>
///   <item><see cref="VoucherLine.PayUnitId"/> yok → etki yok.</item>
///   <item>Giriş → ALACAK (+PayTotal); Çıkış → BORÇ (−PayTotal). (Nakit ile aynı işaret.)</item>
/// </list>
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class ServiceBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Service;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        if (line.PaymentType == ProcessPaymentType.WithCash)
            yield break;

        if (line.PayUnitId is not { } unitId)
            yield break;

        // Giriş → ALACAK (+), Çıkış → BORÇ (−). (Nakit ile aynı.)
        // Bilinçli ve ground-truth ONAYLI (ERPGOLDV2 matrisi, 2026-07-03): Hizmet tipi yalnız
        // Giriş/Çıkış tanır; Credit/Buy asla üretilmez → == Inbound ⟺ IsInflow() özdeştir.
        var amount = line.Direction == ProcessDirectionType.Inbound ? line.PayTotal : -line.PayTotal;
        yield return new BalanceEffect(unitId, amount);
    }
}
