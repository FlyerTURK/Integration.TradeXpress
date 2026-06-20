using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.MultiTenancy;
using Volo.Abp.Account.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Blazor.Client.Navigation;

public class TradeXpressMenuContributor : IMenuContributor
{
    private readonly IConfiguration _configuration;

    public TradeXpressMenuContributor(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
        else if (context.Menu.Name == StandardMenus.User)
        {
            await ConfigureUserMenuAsync(context);
        }
    }

    private static async Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<TradeXpressResource>();
        
        //Administration
        var administration = context.Menu.GetAdministration();
        administration.Order = 6;

        context.Menu.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Home,
            l["Menu:Home"],
            "/",
            icon: TradeXpressIcons.Home,
            order: 1
        ));

        // Tanımlar → Para Birimleri
        var definitions = new ApplicationMenuItem(
            TradeXpressMenus.Currencies,
            l["Definitions"],
            icon: "fas fa-coins",
            order: 2
        );
        // Faz 4 test dönemi: Para Birimleri HEM normal (split olmayan toolbar regresyonu)
        // HEM split olarak menüde — karşılaştırma + normal CrudToolbar testi için.
        definitions.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.CurrencyUnits,
            l["CurrencyUnits"],
            url: "/currencies/currency-units",
            icon: TradeXpressIcons.CurrencyUnit
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        definitions.AddItem(new ApplicationMenuItem(
            "TradeXpress.CurrencyUnitsSplit",
            l["CurrencyUnits"] + " (Split)",
            url: "/currencies/currency-units-split",
            icon: TradeXpressIcons.CurrencyUnit
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        // Kur panosu — viewer'ın efektif fiyatları + "Margin Ayarla" + marj geçmişi.
        definitions.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.PriceBoard,
            l["Menu:PriceBoard"],
            url: "/currencies/prices",
            icon: TradeXpressIcons.PriceBoard
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        // Parite panosu — aktif paritelerin canlı çapraz kurları.
        definitions.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.ParityBoard,
            l["Menu:ParityBoard"],
            url: "/currencies/parities",
            icon: TradeXpressIcons.ParityBoard
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        // Değerleme (re-base) ayrı kullanıcı sayfası DEĞİL — kullanıcı daima piyasa/alışık
        // fiyatı görür; gerçek (base) değer arka planda hesaplanır (işlem/muhasebe).
        // Marj ayrı menü/sayfa DEĞİL — CurrencyUnit ve pano grid'inde "Margin Ayarla"
        // action'ıyla düzenlenir; geçmiş pano "Geçmiş" aksiyonundan görünür.
        context.Menu.AddItem(definitions);

        // Org ağacı yalnız TENANT'a aittir; host (merkezi operasyon) şirket tanımlayamaz → host
        // oturumunda menüde gösterilmez. Şube/Kasa ayrı menü DEĞİL: Şirket edit formunda gömülü
        // drill list'lerle ve Şirketler listesindeki "Şubeler" → "Kasalar" toolbar action'larıyla yönetilir.
        var currentTenant = context.ServiceProvider.GetRequiredService<ICurrentTenant>();
        if (currentTenant.Id != null)
        {
            // Şirketler (OrgScope üstü + değerleme base'i)
            context.Menu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Companies,
                l["Menu:Companies"],
                url: "/companies-split",
                icon: TradeXpressIcons.Company,
                order: 3
            ).RequirePermissions(TradeXpressPermissions.Companies.Default));
        }

        // Ülkeler (merkezi referans — host yönetir, tenant seçer)
        context.Menu.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Countries,
            l["Menu:Countries"],
            url: "/countries-split",
            icon: TradeXpressIcons.Country,
            order: 6
        ).RequirePermissions(TradeXpressPermissions.Countries.Default));

        // Identity Management Menu
        var identityMenu = new ApplicationMenuItem(
            "IdentityManagement",
            l["IdentityManagement"],
            icon: "fas fa-id-card-alt"
        );
        identityMenu.AddItem(new ApplicationMenuItem(
            "IdentityManagement.Users",
            l["Users"],
            url: "/users-split",
            icon: TradeXpressIcons.User
        ).RequirePermissions("AbpIdentity.Users"));
        identityMenu.AddItem(new ApplicationMenuItem(
            "IdentityManagement.Roles",
            l["Roles"],
            url: "/roles-split",
            icon: TradeXpressIcons.Role
        ).RequirePermissions("AbpIdentity.Roles"));
        administration.AddItem(identityMenu);

        // Tenant Management Menu
        if (MultiTenancyConsts.IsEnabled)
        {
            var tenantMenu = new ApplicationMenuItem(
                "TenantManagement",
                l["TenantManagement"],
                url: "/tenant-management/tenants-split",
                icon: TradeXpressIcons.Tenant
            ).RequirePermissions("AbpTenantManagement.Tenants");
            administration.AddItem(tenantMenu);
        }

        // Settings Menu
        var settingsMenu = new ApplicationMenuItem(
            "SettingManagement",
            l["SettingManagement"],
            url: "/setting-management",
            icon: TradeXpressIcons.Settings
        ).RequireAuthenticated();
        administration.AddItem(settingsMenu);
    }

    private async Task ConfigureUserMenuAsync(MenuConfigurationContext context)
    {
        var accountStringLocalizer = context.GetLocalizer<AccountResource>();
        var authServerUrl = _configuration["AuthServer:Authority"] ?? "";

        context.Menu.AddItem(new ApplicationMenuItem(
            "Account.Manage",
            accountStringLocalizer["MyAccount"],
            $"{authServerUrl.EnsureEndsWith('/')}Account/Manage",
            icon: "fa fa-cog",
            order: 1000,
            target: "_blank").RequireAuthenticated());

        await Task.CompletedTask;
    }
}
