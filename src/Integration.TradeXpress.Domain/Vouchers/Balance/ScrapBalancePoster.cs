using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Hurda (<see cref="ProcessType.Scrap"/>) satırlarının bakiye etkisi — ödeme tipine göre 0/1 bacak (ERPPROV3 paritesi):
/// <list type="bullet">
///   <item><b>Peşin</b> (<see cref="ProcessPaymentType.WithCash"/>): cari bakiyeye yansımaz (anlık/kasa) → etki yok.</item>
///   <item><b>Bedelli</b> (<see cref="ProcessPaymentType.WithCurrency"/>): pay bacağı
///         (<see cref="VoucherLine.PayUnitId"/>/<see cref="VoucherLine.PayTotal"/>).</item>
///   <item><b>Normal/İade/Emanet</b> (vd.): ana bacak
///         (<see cref="VoucherLine.MainUnitId"/>/<see cref="VoucherLine.Total"/> = Miktar × Factor).</item>
/// </list>
/// İşaret: Giriş(Inbound) → ALACAK(+), Çıkış(Outbound) → BORÇ(−). isInflow = <c>Direction.IsInflow()</c>.
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class ScrapBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Scrap;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        // Peşin → bakiyeye yansımaz.
        if (line.PaymentType == ProcessPaymentType.WithCash)
            yield break;

        var sign = line.Direction.IsInflow() ? 1m : -1m;   // Giriş +, Çıkış −

        if (line.PaymentType == ProcessPaymentType.WithCurrency)
        {
            // Bedelli → pay bacağı (ödeme birimi).
            if (line.PayUnitId is { } payUnit && line.PayTotal != 0m)
                yield return new BalanceEffect(payUnit, sign * line.PayTotal);
            yield break;
        }

        // Normal/İade/Emanet → ana bacak (Has).
        if (line.MainUnitId != Guid.Empty && line.Total != 0m)
            yield return new BalanceEffect(line.MainUnitId, sign * line.Total);
    }
}
