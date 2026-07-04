using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Taş (<see cref="ProcessType.Stone"/>) satırlarının cari bakiye etkisi — <b>parasal tek bacak</b>:
/// değer (<see cref="VoucherLine.PayTotal"/>) ödeme biriminde (<see cref="VoucherLine.PayUnitId"/>).
/// Peşin (<see cref="ProcessPaymentType.WithCash"/>) → cariye yansımaz (kasa/anlık). İşaret:
/// Giriş(Inbound)→ALACAK(+), Çıkış(Outbound)→BORÇ(−). Taş envanteri (firma stoğu) ayrı boyut — burada YOK.
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class StoneBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Stone;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        if (line.PaymentType == ProcessPaymentType.WithCash)
            yield break;   // Peşin → cariye yansımaz

        if (line.PayUnitId is { } payUnit && line.PayTotal != 0m)
        {
            var sign = line.Direction.IsInflow() ? 1m : -1m;   // Giriş +, Çıkış −
            yield return new BalanceEffect(payUnit, sign * line.PayTotal);
        }
    }
}
