namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parity'ye özel sınırlar. DisplayOrder üst sınırı evrensel 0–99 yerine <b>0–999</b> —
/// host C(n,2) çiftleri yüzlerce parite seed'liyor (hâlihazırda ~165 parite).
/// </summary>
public static class ParityConsts
{
    public const int DisplayOrderMax = 999;
}
