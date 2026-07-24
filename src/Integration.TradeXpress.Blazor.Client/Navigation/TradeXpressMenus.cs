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
    public const string Transfers           = Prefix + ".Transfers";
    public const string Confirmations       = Prefix + ".Confirmations";
    public const string MediaLibrary        = Prefix + ".MediaLibrary";
    public const string Scheduler           = Prefix + ".Scheduler";

    // Tanımlar üst grubu
    public const string Definitions         = Prefix + ".Definitions";

    // Raporlar üst grubu + alt rapor sayfaları (tümü tek grupta toplanır)
    public const string Reports             = Prefix + ".Reports";
    public const string ReportsPosition     = Reports + ".Position";
    public const string ReportsBalanceSheet = Reports + ".BalanceSheet";
    public const string ReportsTransactions = Reports + ".Transactions";
    public const string ReportsCash         = Reports + ".Cash";
    public const string ReportsMetal        = Reports + ".Metal";
    public const string ReportsScrap        = Reports + ".Scrap";
    public const string ReportsGoodStock    = Reports + ".GoodStock";
    public const string ReportsGoodMovement = Reports + ".GoodMovement";

    // Satış (Tanımlar altı alt grup) — kanal + kanalla ilişkili kataloglar
    public const string Sales               = Prefix + ".Sales";
    public const string SalesChannels       = Prefix + ".SalesChannels";
    public const string ShipmentTemplates   = Sales + ".ShipmentTemplates";
    public const string Orders              = Prefix + ".Orders";

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
    public const string Goods               = Commodities + ".Goods";
    public const string AddOns              = Commodities + ".AddOns";
    public const string VariantTemplates    = Commodities + ".VariantTemplates";

    public const string Substitutions           = Commodities + ".Substitutions";
    public const string SubstitutionCalculation = Substitutions + ".Calculation";

    public const string Organizations       = Prefix + ".Organizations";
    public const string Accounts            = Prefix + ".Accounts";
    public const string AccountList         = Accounts + ".List";
    public const string SubAccounts         = Accounts + ".SubAccounts";
}
