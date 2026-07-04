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
/// Yön: <c>Direction.IsInflow()</c> → Buy (inflow). Daima 2 etki.
/// <para>
/// <b>Çevir'in TERSİ işaret — BİLİNÇLİ (2026-07-03 kullanıcı onayı).</b> Çevir bir birim-dönüşümüdür
/// (aynı sahibin bir birimindeki bakiyeyi diğer birime aktarır). Vadeli ise bir ALIŞVERİŞTİR: bir
/// birimde borç, karşı birimde alacak doğurur. İki işlemin ekonomik anlamı farklı olduğundan işaret
/// yönleri de terstir; bu, orijinal ERPPRO trigger'ından (Çevir=Vadeli özdeş) kasıtlı sapmadır.
/// </para>
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class FutureBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Future;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        var isBuy = line.Direction.IsInflow();   // Buy(4) → inflow

        // Ana bacak: Alış → ALACAK (+Total), Satış → BORÇ (−Total).
        if (line.MainUnitId != Guid.Empty && line.Total != 0m)
            yield return new BalanceEffect(line.MainUnitId, isBuy ? line.Total : -line.Total);

        // Pay bacağı: Alış → BORÇ (−PayTotal), Satış → ALACAK (+PayTotal).
        if (line.PayUnitId is { } payUnit && line.PayTotal != 0m)
            yield return new BalanceEffect(payUnit, isBuy ? -line.PayTotal : line.PayTotal);
    }
}
