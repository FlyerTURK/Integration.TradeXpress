using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Mücevher (<see cref="ProcessType.Jewelry"/>) satırlarının cari bakiye etkisi — Taş gibi <b>parasal tek bacak</b>:
/// değer (<see cref="VoucherLine.PayTotal"/>) ödeme biriminde (<see cref="VoucherLine.PayUnitId"/>).
/// Peşin (<see cref="ProcessPaymentType.WithCash"/>) → cariye yansımaz. Giriş(Inbound)→ALACAK(+), Çıkış→BORÇ(−).
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class JewelryBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Jewelry;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        if (line.PaymentType == ProcessPaymentType.WithCash)
            yield break;

        if (line.PayUnitId is { } payUnit && line.PayTotal != 0m)
        {
            var sign = line.Direction.IsInflow() ? 1m : -1m;
            yield return new BalanceEffect(payUnit, sign * line.PayTotal);
        }
    }
}
