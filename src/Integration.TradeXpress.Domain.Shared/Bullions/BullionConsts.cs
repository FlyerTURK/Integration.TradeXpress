using System;
using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Bullions;

/// <summary>
/// Takoz pseudo-birim sabitleri — ERPPRO <c>BakiyeKodlari</c> TAKOZ satırı (BirimId=-1) paritesi.
/// TAKOZ <b>gerçek bir CurrencyUnit DEĞİL</b> (feed/parite/marj yok); raporsuz takozun ham gramını izleyen
/// sahte birim. Konsolidasyonda <c>TAKOZ × <see cref="DefaultCarpan"/> → HAS</c> (legacy <c>Carpan</c>).
/// </summary>
public static class BullionConsts
{
    /// <summary>TAKOZ pseudo-biriminin ayrılmış sabit Id'si (legacy -1 karşılığı). DB'de CurrencyUnit satırı YOK.</summary>
    public static readonly Guid PseudoUnitId = new("ba11ba11-ba11-ba11-ba11-ba11ba11ba11");

    /// <summary>TAKOZ pseudo-birim kodu (gösterimde özel-durum).</summary>
    public const string PseudoUnitCode = CurrencyUnitCode.Bullion;

    /// <summary>TAKOZ → HAS konsolidasyon katsayısı (= takoz varsayılan milyemi; legacy BakiyeKodlari.Carpan).
    /// 1 TAKOZ gram = <c>DefaultCarpan</c> HAS gram. Varsayılan 0.300 (operatör sonra ayarlayabilir).</summary>
    public const decimal DefaultCarpan = 0.300m;
}
