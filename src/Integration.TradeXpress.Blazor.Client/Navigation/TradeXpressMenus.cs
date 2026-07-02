namespace Integration.TradeXpress.Blazor.Client.Navigation;

public class TradeXpressMenus
{
    private const string Prefix = "TradeXpress";

    public const string Home = Prefix + ".Home";

    public const string Companies           = Prefix + ".Companies";
    public const string Branches            = Prefix + ".Branches";
    public const string Vaults              = Prefix + ".Vaults";
    public const string Countries           = Prefix + ".Countries";
    public const string CurrentTransactions = Prefix + ".CurrentTransactions";
    public const string Reports             = Prefix + ".Reports";
    public const string Scheduler           = Prefix + ".Scheduler";

    public const string Currencies          = Prefix + ".Currencies";
    public const string Financial           = Prefix + ".Financial";
    public const string CurrencyUnits       = Currencies + ".CurrencyUnits";
    public const string Parities            = Currencies + ".Parities";

    public const string Commodities         = Prefix + ".Commodities";
    public const string Cashes              = Commodities + ".Cashes";
    public const string Services            = Commodities + ".Services";
    public const string Futures             = Commodities + ".Futures";
    public const string Scraps              = Commodities + ".Scraps";
    public const string Metals              = Commodities + ".Metals";
    public const string Stones              = Commodities + ".Stones";
    public const string Jewelries           = Commodities + ".Jewelries";

    public const string Organizations       = Prefix + ".Organizations";
    public const string Accounts            = Prefix + ".Accounts";
    public const string AccountList         = Accounts + ".List";
    public const string SubAccounts         = Accounts + ".SubAccounts";
}
