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
    public const string Company = "fas fa-building";
    public const string Branch = "fas fa-code-branch";
    public const string Vault = "fas fa-vault";

    // ── Tanımlar ──
    public const string Country = "fas fa-earth-europe";
    public const string CurrencyUnit = "fas fa-coins";
    public const string Cash = "fas fa-money-bill-wave";
    public const string Service = "fas fa-screwdriver-wrench";
    public const string Future = "fas fa-hourglass-half";
    public const string Scrap = "fas fa-recycle";
    public const string Metal = "fas fa-ring";
    public const string Stone = "fas fa-gem";

    // ── Hesaplar ──
    public const string Account = "fas fa-book";
    public const string SubAccount = "fas fa-list-ul";
    public const string Accounts = "fas fa-book-open";
    public const string CurrencyMargin = "fas fa-percent";
    public const string Parity = "fas fa-arrow-right-arrow-left";

    // ── Panolar ──
    public const string PriceBoard = "fas fa-chart-line";

    // ── İşlemler ──
    public const string CurrentTransactions = "fas fa-right-left";

    // ── Yönetim ──
    public const string Tenant = "fas fa-users-cog";
    public const string Settings = "fas fa-cog";
    public const string User = "fas fa-user";
    public const string Role = "fas fa-user-tag";
    public const string Permission = "fas fa-key";

    // ── Menü grupları (parent düğümler) ──
    public const string Definitions = "fas fa-folder-tree";
    public const string Commodities = "fas fa-cubes";
    public const string Organizations = "fas fa-sitemap";
    public const string Financial = "fas fa-money-bill-trend-up";
    public const string Identity = "fas fa-id-card-alt";

    // ── Genel ──
    public const string Home = "fas fa-home";
}
