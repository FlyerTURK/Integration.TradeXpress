namespace Integration.TradeXpress.Blazor.Client.Navigation;

public class TradeXpressMenus
{
    private const string Prefix = "TradeXpress";

    public const string Home = Prefix + ".Home";

    public const string Companies           = Prefix + ".Companies";
    public const string Branches            = Prefix + ".Branches";
    public const string Vaults              = Prefix + ".Vaults";
    public const string Countries           = Prefix + ".Countries";

    public const string Currencies          = Prefix + ".Currencies";
    public const string CurrencyUnits       = Currencies + ".CurrencyUnits";
    public const string PriceBoard          = Currencies + ".PriceBoard";
    public const string Parities            = Currencies + ".Parities";
}
