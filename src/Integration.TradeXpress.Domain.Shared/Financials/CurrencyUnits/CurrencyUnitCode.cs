namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Seed edilen bilinen birim kodları. String literal yerine bunları kullan ki
/// bir yeniden-adlandırma sessizce Guid.Empty lookup üretmesin.
/// Yalnız HAREM'in güvenilir verdiği birimler + pivot TRY (Altınkaynak devre dışı;
/// Harem'de bayat olan JPY/KWD ve hiç vermediği RUB/AZN/CNY/RON/AED dahil DEĞİL).
/// </summary>
public static class CurrencyUnitCode
{
    public const string TRY = "TRY";   // pivot — feed yok, FinalPrice 1
    public const string USD = "USD";
    public const string EUR = "EUR";
    public const string GBP = "GBP";
    public const string CHF = "CHF";
    public const string SAR = "SAR";
    public const string AUD = "AUD";
    public const string CAD = "CAD";
    public const string HAS = "HAS";   // Has altın (gram)
    public const string GUM = "GUM";   // Has gümüş (gram)
    public const string PLT = "PLT";   // Platin (Harem türetilmiş)
    public const string PLD = "PLD";   // Paladyum (Harem türetilmiş)
}
