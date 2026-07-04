using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Çevir (<see cref="ProcessType.Convert"/>) satırlarının bakiye etkisi — <b>iki bacaklı</b> birim dönüşümü:
/// <list type="bullet">
///   <item>Ana bacak (kaynak, <see cref="VoucherLine.MainUnitId"/>/<see cref="VoucherLine.Total"/>):
///         Alacak(Credit)→BORÇ(−), Borç(Debit)→ALACAK(+).</item>
///   <item>Karşı bacak (hedef, <see cref="VoucherLine.PayUnitId"/>/<see cref="VoucherLine.PayTotal"/>):
///         Alacak→ALACAK(+), Borç→BORÇ(−).</item>
/// </list>
/// Yön: <c>Direction.IsInflow()</c> → Credit (inflow). Daima 2 etki üretir.
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class ConvertBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Convert;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        var isCredit = line.Direction.IsInflow();   // Credit(2) → inflow

        // Ana bacak: Alacak → BORÇ (−Total), Borç → ALACAK (+Total).
        if (line.MainUnitId != Guid.Empty && line.Total != 0m)
            yield return new BalanceEffect(line.MainUnitId, isCredit ? -line.Total : line.Total);

        // Karşı bacak: Alacak → ALACAK (+PayTotal), Borç → BORÇ (−PayTotal).
        if (line.PayUnitId is { } payUnit && line.PayTotal != 0m)
            yield return new BalanceEffect(payUnit, isCredit ? line.PayTotal : -line.PayTotal);
    }
}
