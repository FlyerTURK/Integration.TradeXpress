using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Dekont (<see cref="ProcessType.DebitNote"/>) satırlarının bakiye etkisi — Nakit ile aynı işaret mantığı
/// AMA <b>peşin muafiyeti YOK</b>: dekont DAİMA bakiyeye yazar (legacy BORC=999 tek bacak paritesi).
/// <list type="bullet">
///   <item>Karşılık birimi (<see cref="VoucherLine.PayUnitId"/>) yoksa etki yok.</item>
///   <item>Giriş (<see cref="ProcessDirectionType.Inbound"/>, UI etiketi ALACAK) → +,
///         aksi (BORÇ) → −; tutar <see cref="VoucherLine.PayTotal"/>.</item>
/// </list>
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class DebitNoteBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.DebitNote;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        // Karşılık birimi yoksa hareket yok. (Peşin muafiyeti BİLİNÇLİ yok — dekont daima bakiyeye yazar.)
        if (line.PayUnitId is not { } unitId)
            yield break;

        // Giriş → ALACAK (+), aksi → BORÇ (−). (Nakit ile aynı işaret.)
        // Bilinçli ve ground-truth ONAYLI (ERPGOLDV2 matrisi, 2026-07-03): Dekont/Devir tipi yalnız
        // Giriş/Çıkış tanır; Credit/Buy asla üretilmez → == Inbound ⟺ IsInflow() özdeştir.
        var amount = line.Direction == ProcessDirectionType.Inbound ? line.PayTotal : -line.PayTotal;
        yield return new BalanceEffect(unitId, amount);
    }
}
