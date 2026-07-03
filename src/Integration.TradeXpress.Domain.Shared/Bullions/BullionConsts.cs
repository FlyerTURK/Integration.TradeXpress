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

    /// <summary>Raporsuz takoz HAS değerleme katsayısı — SABİT. Legacy parite: <c>FN.TakozKur = HAS kuru × 0.6</c>
    /// ve <c>Report.BakiyeListesi</c>/<c>Stok.TakozOzet</c>/<c>Stok.GetCesniStoklari</c>'nda hardcoded <c>× 0.600</c>
    /// (ERPGOLDV2'de doğrulandı). 1 TAKOZ gram = <c>DefaultCarpan</c> HAS gram.</summary>
    public const decimal DefaultCarpan = 0.600m;
}
