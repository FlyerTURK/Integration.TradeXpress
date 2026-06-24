namespace Integration.TradeXpress.Financials.ExchangeRates;

public class LiveRateDto
{
    public string Code { get; set; } = string.Empty;
    public decimal Buy  { get; set; }
    public decimal Sell { get; set; }
}
