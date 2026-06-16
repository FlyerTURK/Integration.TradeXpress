namespace Integration.TradeXpress.Blazor.Client.Pages.Admin;

/// <summary>
/// Identity liste ve edit sekmeleri arasında değişim bildirimi (IEntityChangeNotifier) için
/// paylaşılan anahtarlar. Liste sayfası bu anahtarı dinler; edit sayfası kayıttan sonra yayınlar.
/// </summary>
public static class IdentityViewKeys
{
    public const string Users = "admin/users";
    public const string Roles = "admin/roles";
}
