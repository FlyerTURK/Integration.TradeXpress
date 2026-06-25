namespace Integration.TradeXpress.Blazor.Client;

/// <summary>
/// Entity/navigasyon ikonlarının TEK kaynağı. Her entity'nin ikonu burada bir kez tanımlanır;
/// list page (EntityIcon), drill (EntityIcon), MDI sekme açma (TabManager), menü ve toolbar
/// child-açma butonları hep buradan okur. Yeni entity eklerken ikonu yalnız buraya yaz.
/// FontAwesome (free, solid) class'ları.
/// </summary>
public static class TradeXpressIcons
{
    // ── Org hiyerarşisi ──
    public const string Company = "custom-icon-company";
    public const string Branch = "custom-icon-branch";
    public const string Vault = "custom-icon-vault";

    // ── Tanımlar ──
    public const string Country = "custom-icon-country";
    public const string CurrencyUnit = "custom-icon-currency-unit";
    public const string Cash = "custom-icon-cash";
    public const string Service = "custom-icon-service";
    public const string Future = "custom-icon-future";
    public const string Scrap = "custom-icon-scrap";
    public const string Metal = "custom-icon-metal";
    public const string Stone = "custom-icon-stone";
    public const string Jewelry = "custom-icon-jewelry";

    // ── Hesaplar ──
    public const string Account = "custom-icon-account";
    public const string SubAccount = "custom-icon-list";
    public const string Accounts = "custom-icon-accounts";
    public const string CurrencyMargin = "custom-icon-currency-margin";
    public const string Parity = "custom-icon-parity";

    // ── Panolar ──
    public const string PriceBoard = "custom-icon-price-board";

    // ── İşlemler ──
    public const string CurrentTransactions = "custom-icon-current-transactions";

    // ── Yönetim ──
    public const string Tenant = "custom-icon-tenant";
    public const string Settings = "custom-icon-settings";
    public const string User = "custom-icon-user";
    public const string Role = "custom-icon-role";
    public const string Permission = "custom-icon-permission";

    // ── Menü grupları (parent düğümler) ──
    public const string Definitions = "custom-icon-definitions";
    public const string Commodities = "custom-icon-commodities";
    public const string Organizations = "custom-icon-organizations";
    public const string Financial = "custom-icon-financial";
    public const string Identity = "custom-icon-identity";

    // ── Genel ──
    public const string Home = "custom-icon-home";
}
