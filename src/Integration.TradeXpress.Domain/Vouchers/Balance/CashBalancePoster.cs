using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Nakit (<see cref="ProcessType.Cash"/>) satırlarının bakiye etkisi:
/// <list type="bullet">
///   <item>Peşin (<see cref="ProcessPaymentType.WithCash"/>) → bakiyeye YANSIMAZ
///         (anlık ödeme, cari bakiyede birikmez).</item>
///   <item>Karşılık birimi (<see cref="VoucherLine.PayUnitId"/>) yoksa etki yok.</item>
///   <item>Giriş (<see cref="ProcessDirectionType.Inbound"/>) → ALACAK (+),
///         aksi → BORÇ (−); tutar <see cref="VoucherLine.PayTotal"/>.</item>
/// </list>
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class CashBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Cash;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        // Peşin → bakiyeye yansımaz.
        if (line.PaymentType == ProcessPaymentType.WithCash)
            yield break;

        // Karşılık birimi yoksa hareket yok.
        if (line.PayUnitId is not { } unitId)
            yield break;

        // Nakitte yön Giriş/Çıkış: In → ALACAK (+), aksi → BORÇ (−).
        var amount = line.Direction == ProcessDirectionType.Inbound ? line.PayTotal : -line.PayTotal;
        yield return new BalanceEffect(unitId, amount);
    }
}
