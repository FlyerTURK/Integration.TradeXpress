using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Virman (<see cref="ProcessType.Transfer"/>) satırlarının bakiye etkisi — Nakit ile aynı işaret mantığı
/// AMA peşin muafiyeti YOK (virmanda peşin kavramı zaten yoktur; ödeme tipi daima Normal → "VGN"/"VCN").
/// <para>Çift bacak İKİ ayrı satırdan doğar: her satır KENDİ hesabının voucher'ında durur ve poster
/// tek satıra bakar — kaynak satır (Çıkış) −, ikiz karşı satır (Giriş) + postlar; toplam etki sıfırdır.</para>
/// <list type="bullet">
///   <item>Karşılık birimi (<see cref="VoucherLine.PayUnitId"/>) yoksa etki yok.</item>
///   <item>Giriş (<see cref="ProcessDirectionType.Inbound"/>, UI etiketi ALACAK) → +,
///         aksi (BORÇ) → −; tutar <see cref="VoucherLine.PayTotal"/>.</item>
/// </list>
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class TransferBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Transfer;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        // Karşılık birimi yoksa hareket yok. (Peşin muafiyeti BİLİNÇLİ yok — virman daima bakiyeye yazar.)
        if (line.PayUnitId is not { } unitId)
            yield break;

        // Giriş → ALACAK (+), aksi → BORÇ (−). (Nakit ile aynı işaret; çift etki iki satırdan doğar.)
        var amount = line.Direction == ProcessDirectionType.Inbound ? line.PayTotal : -line.PayTotal;
        yield return new BalanceEffect(unitId, amount);
    }
}
