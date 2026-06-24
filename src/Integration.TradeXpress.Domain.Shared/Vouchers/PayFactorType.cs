namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiyat birimi tipi (ERPPROV3 paritesi) — salt UI; kanonik PayFactor her zaman tek bir bazda saklanır,
/// panel seçilen tipe göre fiyatı çevirip gösterir. <b>Tek enum</b>; her panel ilgili alt kümeyi gösterir:
/// <list type="bullet">
///   <item>Vadeli: <see cref="Gram"/> (kanonik) / <see cref="Ounce"/> (×31.1035).</item>
///   <item>Hurda: <see cref="Has"/> (kanonik) / <see cref="Quantity"/> (brüt gram, ×Factor).</item>
/// </list>
/// </summary>
public enum PayFactorType : byte
{
    /// <summary>Gram başına (Vadeli kanonik).</summary>
    Gram     = 0,

    /// <summary>Ons başına — gösterim = kanonik × 31.1035.</summary>
    Ounce    = 1,

    /// <summary>Has başına (Hurda kanonik).</summary>
    Has      = 2,

    /// <summary>Miktar (brüt gram) başına — gösterim = kanonik × Factor.</summary>
    Quantity = 3,
}
