using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Maden (<see cref="ProcessType.Metal"/>) satırlarının bakiye etkisi (ERPPROV3 paritesi):
/// <list type="bullet">
///   <item><b>Peşin</b> (<see cref="ProcessPaymentType.WithCash"/>): bakiyeye yansımaz → etki yok.</item>
///   <item><b>Rezervasyon</b> (<see cref="ProcessPaymentType.Reservation"/>): taahhüt sayacı —
///         bakiyeye yansımaz → etki yok (yalnız stok kullanılabilirliğini etkiler, o da raporda).</item>
///   <item><b>Bedelli</b> (<see cref="ProcessPaymentType.WithCurrency"/>): tek bacak — bedel
///         (<see cref="VoucherLine.PayUnitId"/>/<see cref="VoucherLine.PayTotal"/>). Maden bacağı yok
///         (işçilik Factor'a yedirildiği için Total bedele zaten yansır).</item>
///   <item><b>Normal/İade/Emanet</b>: <b>İKİ bacak</b> — ana Has
///         (<see cref="VoucherLine.MainUnitId"/>/<see cref="VoucherLine.Total"/>) + işçilik
///         (<see cref="VoucherLine.PayUnitId"/>/<see cref="VoucherLine.PayTotal"/>).</item>
/// </list>
/// İşaret: Giriş(Inbound)→ALACAK(+), Çıkış(Outbound)→BORÇ(−). isInflow = <c>Direction.IsInflow()</c>.
/// (Hurda'dan fark: Normal'de işçilik ikinci bacak olarak cari bakiyeye yansır.)
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class MetalBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Metal;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        if (line.PaymentType is ProcessPaymentType.WithCash or ProcessPaymentType.Reservation)
            yield break;   // Peşin → yansımaz; Rezervasyon → taahhüt sayacı, bakiye-dışı

        var sign = line.Direction.IsInflow() ? 1m : -1m;   // Giriş +, Çıkış −

        if (line.PaymentType == ProcessPaymentType.WithCurrency)
        {
            // Bedelli → yalnız bedel bacağı.
            if (line.PayUnitId is { } bedelUnit && line.PayTotal != 0m)
                yield return new BalanceEffect(bedelUnit, sign * line.PayTotal);
            yield break;
        }

        // Normal/İade/Emanet → ana Has + işçilik.
        if (line.MainUnitId != Guid.Empty && line.Total != 0m)
            yield return new BalanceEffect(line.MainUnitId, sign * line.Total);
        if (line.PayUnitId is { } laborUnit && line.PayTotal != 0m)
            yield return new BalanceEffect(laborUnit, sign * line.PayTotal);
    }
}
