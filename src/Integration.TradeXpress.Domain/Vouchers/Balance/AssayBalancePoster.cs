using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Çeşni (<see cref="ProcessType.Assay"/>) satırının bakiye etkisi — biriken çeşni stoğundan (takoz
/// girişlerindeki numune/AssayAmount havuzu) cariye SAF METAL verilmesi. Yön DAİMA ÇIKIŞ, para bacağı YOK
/// (Fiyat=Tutar=0; legacy Kodu="CESNI"):
/// <list type="bullet">
///   <item>HAS bacağı (<see cref="VoucherLine.MainUnitId"/>) → −(Miktar × altın milyemi <see cref="VoucherLine.Factor"/>).</item>
///   <item>GUM bacağı (<see cref="VoucherLine.SilverUnitId"/>) → −(Miktar × gümüş milyemi <see cref="VoucherLine.SilverFactor"/>).</item>
/// </list>
/// Birim Id'leri satırda taşınır (panel doldurur — BullionBalancePoster ile aynı desen).
/// </summary>
[ExposeServices(typeof(IVoucherLineBalancePoster))]
public sealed class AssayBalancePoster : IVoucherLineBalancePoster, ITransientDependency
{
    public ProcessType ProcessType => ProcessType.Assay;

    public IEnumerable<BalanceEffect> Post(VoucherLine line)
    {
        var gold = line.Amount * line.Factor;
        if (gold != 0m && line.MainUnitId != Guid.Empty)
            yield return new BalanceEffect(line.MainUnitId, -gold);

        var silver = line.Amount * (line.SilverFactor ?? 0m);
        if (silver != 0m && line.SilverUnitId is { } gum)
            yield return new BalanceEffect(gum, -silver);
    }
}
