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
    public const string CurrencyMargin = "fas fa-percent";

    // ── Panolar ──
    public const string PriceBoard = "fas fa-chart-line";
    public const string ParityBoard = "fas fa-arrow-right-arrow-left";

    // ── Yönetim ──
    public const string Tenant = "fas fa-users-cog";
    public const string Settings = "fas fa-cog";
    public const string User = "fas fa-user";
    public const string Role = "fas fa-user-tag";
    public const string Permission = "fas fa-key";

    // ── Genel ──
    public const string Home = "fas fa-home";
}
