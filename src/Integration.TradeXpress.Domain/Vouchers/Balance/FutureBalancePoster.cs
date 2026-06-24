using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Vadeli (<see cref="ProcessType.Future"/>) satırlarının bakiye etkisi — <b>iki bacaklı</b> alış/satış:
/// <list type="bullet">
///   <item>Ana bacak (<see cref="VoucherLine.MainUnitId"/>/<see cref="VoucherLine.Total"/>):
///         Alış(Buy)→ALACAK(+), Satış(Sell)→BORÇ(−).</item>
///   <item>Pay bacağı (<see cref="VoucherLine.PayUnitId"/>/<see cref="VoucherLine.PayTotal"/>):
///         Alış→BORÇ(−), Satış→ALACAK(+).</item>
/// </list>
/// Yön: <c>(int)Direction % 2 == 0</c> → Buy (inflow). Çevir'in TERSİ işaret. Daima 2 etki.
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class FutureBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Future;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        var isBuy = ((int)line.Direction % 2) == 0;   // Buy(4) → inflow

        // Ana bacak: Alış → ALACAK (+Total), Satış → BORÇ (−Total).
        if (line.MainUnitId != Guid.Empty && line.Total != 0m)
            yield return new BalanceEffect(line.MainUnitId, isBuy ? line.Total : -line.Total);

        // Pay bacağı: Alış → BORÇ (−PayTotal), Satış → ALACAK (+PayTotal).
        if (line.PayUnitId is { } payUnit && line.PayTotal != 0m)
            yield return new BalanceEffect(payUnit, isBuy ? -line.PayTotal : line.PayTotal);
    }
}
