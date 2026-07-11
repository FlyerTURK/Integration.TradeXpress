namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Yan-maliyet kaleminin <b>hesaplama modu</b> (2026-07-10 kullanıcı kararı — gider satırları grid'i):
/// kalem reçeteye nasıl yansır. <c>SideCostRecipeComposer</c> düz projeksiyon yapar:
/// FixedAmount → <c>Add</c>, PercentOfCost → <c>Percent(AllAbove)</c>, GrossUpPercent → <c>GrossUp(AllAbove)</c>
/// (GrossUp satırları HEP EN SONDA — sabit giderler de komisyona tabi; kâr korunumu matematiği).
/// </summary>
public enum SideCostCalcMode : byte
{
    /// <summary>Sabit tutar (kalemin para birimiyle; birim boşsa kanal yereli) — reçetede <c>Add</c> satırı.</summary>
    FixedAmount = 1,

    /// <summary>Devreden maliyet toplamının yüzdesi (0-100) — reçetede <c>Percent(AllAbove)</c> satırı
    /// (Loomis sigorta primi deseni: operand maliyet katmanında s iken fiyat kuruşuna doğrudur).</summary>
    PercentOfCost = 2,

    /// <summary>Brütleştirme yüzdesi [0,100) — reçetede <c>GrossUp(AllAbove)</c> satırı, HEP EN SONDA
    /// (taban ÷ (1−oran/100); komisyon/Offsite Ads deseni — kâr korunumu).</summary>
    GrossUpPercent = 3,
}
