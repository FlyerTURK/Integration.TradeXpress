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

        // Tanımlar → Para Birimleri
        var definitions = new ApplicationMenuItem(
            TradeXpressMenus.Currencies,
            l["Definitions"],
            icon: TradeXpressIcons.Definitions,
            order: 2
        );
        // Finansal — Para Birimleri + Kur Panosu + Pariteler (alt menü).
        var financial = new ApplicationMenuItem(
            TradeXpressMenus.Financial,
            l["Menu:Financial"],
            icon: TradeXpressIcons.Financial
        );
        // Para Birimleri (CRUD; liste tab'ında, edit yeni yığın tab'ında). Split menü kalemi kaldırıldı.
        financial.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.CurrencyUnits,
            l["CurrencyUnits"],
            url: "/currencies/currency-units",
            icon: TradeXpressIcons.CurrencyUnit
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        // Kur panosu — viewer'ın efektif fiyatları + "Margin Ayarla" + marj geçmişi.
        financial.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.PriceBoard,
            l["Menu:PriceBoard"],
            url: "/currencies/prices",
            icon: TradeXpressIcons.PriceBoard
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        // Pariteler — base/quote çiftleri (CRUD). Host yönetir; tenant kendi paritesini ekler.
        financial.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Parities,
            l["Menu:Parities"],
            url: "/currencies/parities",
            icon: TradeXpressIcons.Parity
        ).RequirePermissions(TradeXpressPermissions.Parities.Default));
        definitions.AddItem(financial);
        // Emtialar — Voucher/VoucherLine'da seçilecek işaretçi emtia tipleri (alt menü). Nakitler buraya bağlı.
        var currentTenant = context.ServiceProvider.GetRequiredService<ICurrentTenant>();
        if (currentTenant.Id != null)
        {
            var commodities = new ApplicationMenuItem(
            TradeXpressMenus.Commodities,
            l["Commodities"],
            icon: TradeXpressIcons.Commodities
        );
        var cashesMenu = new ApplicationMenuItem(
            TradeXpressMenus.Cashes,
            l["Cashes"],
            url: "/cashes",
            icon: TradeXpressIcons.Cash
        ).RequirePermissions(TradeXpressPermissions.Cashes.Default);
        cashesMenu.CssClass = "underline-menu-item";

        cashesMenu.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Reports + ".Cash",
            l["CashReport"],
            url: "/reports/cash",
            icon: "fa fa-chart-bar"
        ));

        commodities.AddItem(cashesMenu);
        commodities.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Services,
            l["Services"],
            url: "/services",
            icon: TradeXpressIcons.Service
        ));
        commodities.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Futures,
            l["Futures"],
            url: "/futures",
            icon: TradeXpressIcons.Future
        ));
        var scrapsMenu = new ApplicationMenuItem(
            TradeXpressMenus.Scraps,
            l["Scraps"],
            url: "/scraps",
            icon: TradeXpressIcons.Scrap
        );
        scrapsMenu.CssClass = "underline-menu-item";

        scrapsMenu.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Reports + ".Scrap",
            l["ScrapReport"],
            url: "/reports/scrap",
            icon: "fa fa-chart-bar"
        ));

        commodities.AddItem(scrapsMenu);

        var metalsMenu = new ApplicationMenuItem(
            TradeXpressMenus.Metals,
            l["Metals"],
            url: "/metals",
            icon: TradeXpressIcons.Metal
        );
        metalsMenu.CssClass = "underline-menu-item";

        metalsMenu.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Reports + ".Metal",
            l["MetalReport"],
            url: "/reports/metal",
            icon: "fa fa-chart-bar"
        ));

        commodities.AddItem(metalsMenu);
        commodities.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Stones,
            l["Stones"],
            url: "/stones",
            icon: TradeXpressIcons.Stone
        ));
        commodities.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Jewelries,
            l["Jewelries"],
            url: "/jewelries",
            icon: TradeXpressIcons.Jewelry
        ));
        definitions.AddItem(commodities);
        }
        // Değerleme (re-base) ayrı kullanıcı sayfası DEĞİL — kullanıcı daima piyasa/alışık
        // fiyatı görür; gerçek (base) değer arka planda hesaplanır (işlem/muhasebe).
        // Marj ayrı menü/sayfa DEĞİL — CurrencyUnit ve pano grid'inde "Margin Ayarla"
        // action'ıyla düzenlenir; geçmiş pano "Geçmiş" aksiyonundan görünür.
        // Organizasyonlar (Tanımlar altında) — Şirketler + Cari Hesaplar. Company/Account tenant'a aittir
        // (host şirket tanımlayamaz) → yalnız tenant oturumunda gösterilir. Şube/Kasa ve Alt Hesap ayrı menü
        // DEĞİL: parent edit formundaki drill list'lerle yönetilir.
        if (currentTenant.Id != null)
        {
            var organizations = new ApplicationMenuItem(
                TradeXpressMenus.Organizations,
                l["Menu:Organizations"],
                icon: TradeXpressIcons.Organizations,
                order: 3
            );
            // Şirketler (OrgScope üstü + değerleme base'i)
            organizations.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Companies,
                l["Menu:Companies"],
                url: "/companies",
                icon: TradeXpressIcons.Company
            ).RequirePermissions(TradeXpressPermissions.Companies.Default));
            // Cari Hesaplar — company-scoped. Alt hesaplar drill (Hesap edit formunda).
            organizations.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.AccountList,
                l["Accounts"],
                url: "/accounts",
                icon: TradeXpressIcons.Account
            ).RequirePermissions(TradeXpressPermissions.Accounts.Default));
            definitions.AddItem(organizations);
        }

        context.Menu.AddItem(definitions);

        if (currentTenant.Id != null)
        {
            // Cari İşlemler — bağımsız işlem formu (adaptif 3 panelli yerleşim).
            context.Menu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.CurrentTransactions,
                l["Menu:CurrentTransactions"],
                url: "/cari-islemler",
                icon: TradeXpressIcons.CurrentTransactions,
                order: 1
            ).RequireAuthenticated());
        }



        // Ülkeler (merkezi referans — host yönetir, tenant seçer)
        context.Menu.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Countries,
            l["Menu:Countries"],
            url: "/countries",
            icon: TradeXpressIcons.Country,
            order: 6
        ).RequirePermissions(TradeXpressPermissions.Countries.Default));

        // Identity Management Menu
        var identityMenu = new ApplicationMenuItem(
            "IdentityManagement",
            l["IdentityManagement"],
            icon: TradeXpressIcons.Identity
        );
        identityMenu.AddItem(new ApplicationMenuItem(
            "IdentityManagement.Users",
            l["Users"],
            url: "/admin/users",
            icon: TradeXpressIcons.User
        ).RequirePermissions("AbpIdentity.Users"));
        identityMenu.AddItem(new ApplicationMenuItem(
            "IdentityManagement.Roles",
            l["Roles"],
            url: "/admin/roles",
            icon: TradeXpressIcons.Role
        ).RequirePermissions("AbpIdentity.Roles"));
        administration.AddItem(identityMenu);

        // Tenant Management Menu
        if (MultiTenancyConsts.IsEnabled)
        {
            var tenantMenu = new ApplicationMenuItem(
                "TenantManagement",
                l["TenantManagement"],
                url: "/tenant-management/tenants",
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
