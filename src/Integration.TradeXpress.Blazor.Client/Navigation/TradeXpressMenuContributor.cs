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
        var currentTenant = context.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // Kök sıra (günlük kullanım üstte): Cari İşlemler(1) · Transferler(2) · Teyitler(3)
        // · Siparişler(4) · Takvim(5) · Tanımlar(6) · Raporlar(7) · Yönetim(8).

        //Administration
        var administration = context.Menu.GetAdministration();
        administration.Order = 8;

        // ── Kök: günlük operasyon kalemleri (tenant-only) ──
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

            // Transferler — AYRI bir ekran DEĞİL: Cari İşlemler ile AYNI voucher formunun organizasyon-içi
            // kipi (Cari Hesap→Alt Hesap yerine Şube→Kasa). Kipi MENÜ/ROTA belirler; form içinde karşı-taraf
            // combo'su yoktur. İzin yaptığı işe göre: iç kipte kayıt postlanmaz, karşı kasaya Teyit TEKLİFİ
            // düşer → Confirmations.Propose. (Eski bağımsız Transfer aggregate'i + Transfers.* izinleri SÖKÜLDÜ.)
            context.Menu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Transfers,
                l["Menu:Transfers"],
                url: "/transfers",
                icon: TradeXpressIcons.Transfer,
                order: 2
            ).RequirePermissions(TradeXpressPermissions.Confirmations.Propose));

            // Teyitler — organizasyon-içi karşılıklı ayna onayı gelen/giden kutusu (kasa↔kasa; postlama
            // yalnız iki taraf da kendi kaydını yazıp teyitleyince). Tenant-only (kasa operasyonu).
            context.Menu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Confirmations,
                l["Menu:Confirmations"],
                url: "/confirmations",
                icon: TradeXpressIcons.Confirmation,
                order: 3
            ).RequirePermissions(TradeXpressPermissions.Confirmations.View));

            // Siparişler — ORTAK sipariş paneli (tüm satış kanallarının siparişleri tek grid). Salt-okuma çekim (O0);
            // kanal menüsü gibi tenant-only (company-owned operasyonel kayıt). İzin: SalesChannels (ayrı Order izni O0'da yok).
            context.Menu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Orders,
                l["Orders"],
                url: "/orders",
                icon: TradeXpressIcons.SalesChannel,
                order: 4
            ).RequirePermissions(TradeXpressPermissions.SalesChannels.Default));

            // Takvim — DevExpress DxScheduler (company-scoped randevular); günlük kullanım aracı → kökte kalır.
            // İkon: placeholder (history); özel takvim ikonu onay sonrası.
            context.Menu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Scheduler,
                l["Menu:Scheduler"],
                url: "/scheduler",
                icon: TradeXpressIcons.History,
                order: 5
            ).RequirePermissions(TradeXpressPermissions.Appointments.Default));
        }

        // ── Tanımlar ──
        var definitions = new ApplicationMenuItem(
            TradeXpressMenus.Definitions,
            l["Definitions"],
            icon: TradeXpressIcons.Definitions,
            order: 6
        );

        // Finansal — Para Birimleri + Pariteler (alt menü).
        var financial = new ApplicationMenuItem(
            TradeXpressMenus.Financial,
            l["Menu:Financial"],
            icon: TradeXpressIcons.Financial,
            order: 1
        );
        // Para Birimleri (CRUD; liste tab'ında, edit yeni yığın tab'ında). Split menü kalemi kaldırıldı.
        financial.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.CurrencyUnits,
            l["CurrencyUnits"],
            url: "/currencies/currency-units",
            icon: TradeXpressIcons.CurrencyUnit
        ).RequirePermissions(TradeXpressPermissions.CurrencyUnits.Default));
        // Pariteler — base/quote çiftleri (CRUD). Host yönetir; tenant kendi paritesini ekler.
        financial.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Parities,
            l["Menu:Parities"],
            url: "/currencies/parities",
            icon: TradeXpressIcons.Parity
        ).RequirePermissions(TradeXpressPermissions.Parities.Default));
        definitions.AddItem(financial);

        // Satış — Satış Kanalları + Kargo Şablonları (alt grup). YALNIZ tenant (2026-07-10 kullanıcı
        // kararı: kanal company-owned operasyonel kayıttır, host'ta tanımlanamaz → menüsü de host'ta
        // görünmez; host-global kataloglar [N11/Trendyol kategorileri] tenant'tan Change(null) ile beslenir).
        if (currentTenant.Id != null)
        {
            var sales = new ApplicationMenuItem(
                TradeXpressMenus.Sales,
                l["Menu:Sales"],
                icon: TradeXpressIcons.SalesChannel,
                order: 2
            );
            sales.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.SalesChannels,
                l["SalesChannels"],
                url: "/sales-channels",
                icon: TradeXpressIcons.SalesChannel
            ).RequirePermissions(TradeXpressPermissions.SalesChannels.Default));
            // Kargo Şablonları — birleşik ERP kargo şablonu katalogu (company-owned, kanal-nötr); ürün formunda seçilir.
            sales.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ShipmentTemplates,
                l["ShipmentTemplates"],
                url: "/shipment-templates",
                icon: TradeXpressIcons.ShipmentTemplate
            ).RequirePermissions(TradeXpressPermissions.ShipmentTemplates.Default));
            definitions.AddItem(sales);
        }

        // Emtialar — Voucher/VoucherLine'da seçilecek işaretçi emtia tipleri (alt menü). Nakitler buraya bağlı.
        if (currentTenant.Id != null)
        {
            var commodities = new ApplicationMenuItem(
                TradeXpressMenus.Commodities,
                l["Commodities"],
                icon: TradeXpressIcons.Commodities,
                order: 3
            );
            commodities.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Cashes,
                l["Cashes"],
                url: "/cashes",
                icon: TradeXpressIcons.Cash
            ).RequirePermissions(TradeXpressPermissions.Cashes.Default));
            commodities.AddItem(new ApplicationMenuItem(
                "TradeXpress.AssayOffices",
                l["AssayOffices"],
                url: "/assay-offices",
                icon: "custom-icon-bullion"
            ).RequirePermissions(TradeXpressPermissions.AssayOffices.Default));
            // Eklentiler — sipariş anı fiyatlı seçenek katalogu (kurdele/kutu/ambalaj); ürünlere atanır.
            commodities.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.AddOns,
                l["AddOns"],
                url: "/add-ons",
                icon: "custom-icon-price"
            ).RequirePermissions(TradeXpressPermissions.AddOns.Default));
            // Varyant Tanımları — yeniden kullanılabilir özellik grubu (demet) katalogu; ürünlere "Katalogtan Uygula" ile aktarılır.
            commodities.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.VariantTemplates,
                l["VariantTemplates"],
                url: "/variant-templates",
                icon: "custom-icon-sliders"
            ).RequirePermissions(TradeXpressPermissions.VariantTemplates.Default));
            // Ürünler — polimorfik emtia katalogu (company-owned) + varyant drill.
            commodities.AddItem(new ApplicationMenuItem(
                "TradeXpress.Products",
                l["Menu:Products"],
                url: "/products",
                icon: TradeXpressIcons.Product
            ).RequirePermissions(TradeXpressPermissions.Products.Default));
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
            commodities.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Scraps,
                l["Scraps"],
                url: "/scraps",
                icon: TradeXpressIcons.Scrap
            ));
            commodities.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Metals,
                l["Metals"],
                url: "/metals",
                icon: TradeXpressIcons.Metal
            ));

            // Muadil Grupları — madenlerin ikame tanımı (adet-hesaplı + standart gramaj); Metals'in hemen altında.
            // Alt kalem: Muadil Hesaplama (grup + talep miktarı → Top-N kombinasyon tablosu).
            var substitutionsMenu = new ApplicationMenuItem(
                TradeXpressMenus.Substitutions,
                l["SubstitutionGroups"],
                url: "/substitutions",
                icon: TradeXpressIcons.Substitution
            ).RequirePermissions(TradeXpressPermissions.Substitutions.Default);
            substitutionsMenu.CssClass = "underline-menu-item";

            substitutionsMenu.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.SubstitutionCalculation,
                l["SubstitutionCalculation"],
                url: "/substitutions/calculation",
                icon: TradeXpressIcons.Report
            ));

            commodities.AddItem(substitutionsMenu);
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
            commodities.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.Goods,
                l["Goods"],
                url: "/goods",
                icon: TradeXpressIcons.Good
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
                order: 4
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

        // Ülkeler (merkezi coğrafya referansı — host yönetir, tenant seçer) — Tanımlar altında.
        definitions.AddItem(new ApplicationMenuItem(
            TradeXpressMenus.Countries,
            l["Menu:Countries"],
            url: "/countries",
            icon: TradeXpressIcons.Country,
            order: 5
        ).RequirePermissions(TradeXpressPermissions.Countries.Default));

        // Medya Kütüphanesi — şirket-kapsamlı DAM yönetimi (görsel/video blob storage); içerik/referans aracı → Tanımlar altında.
        if (currentTenant.Id != null)
        {
            definitions.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.MediaLibrary,
                l["MediaLibrary"],
                url: "/media",
                icon: TradeXpressIcons.Report,
                order: 6
            ).RequireAuthenticated());
        }

        context.Menu.AddItem(definitions);

        // ── Raporlar — TÜM rapor sayfaları tek üst grupta (tenant-only; raporlar kasa/işlem verisi üstünde çalışır) ──
        if (currentTenant.Id != null)
        {
            var reports = new ApplicationMenuItem(
                TradeXpressMenus.Reports,
                l["Menu:Reports"],
                icon: TradeXpressIcons.Report,
                order: 7
            );

            // Pozisyon Raporu — bilanço birimine göre canlı açık pozisyon (ledger toplamı, 5sn yenilenir).
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsPosition,
                l["PositionReport"],
                url: "/reports/position",
                icon: TradeXpressIcons.Report,
                order: 1
            ).RequirePermissions(TradeXpressPermissions.Reports.Position));

            // Bilanço Raporu — FULL net-varlık (snapshot; kapsam Şube/Şirket switch + tarih → Bilanço Al/Kaydet).
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsBalanceSheet,
                l["BalanceSheetReport"],
                url: "/reports/balance-sheet",
                icon: TradeXpressIcons.Report,
                order: 2
            ).RequirePermissions(TradeXpressPermissions.Reports.BalanceSheet));

            // İşlem Raporu — cari-hesap-BAĞIMSIZ, Company/Branch/Vault scoped, tarih aralıklı işlem listesi.
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsTransactions,
                l["TransactionReport"],
                url: "/reports/transactions",
                icon: TradeXpressIcons.Report,
                order: 3
            ).RequirePermissions(TradeXpressPermissions.Reports.Transactions));

            // Nakit Raporu — eski yeri Kasalar'ın altıydı; parent'ın Cashes izni görünürlük eşdeğeri için taşındı.
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsCash,
                l["CashReport"],
                url: "/reports/cash",
                icon: TradeXpressIcons.Report,
                order: 4
            ).RequirePermissions(TradeXpressPermissions.Cashes.Default));

            // Maden Raporu (eski yeri Madenler'in altıydı)
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsMetal,
                l["MetalReport"],
                url: "/reports/metal",
                icon: TradeXpressIcons.Report,
                order: 5
            ));

            // Hurda Raporu (eski yeri Hurdalar'ın altıydı)
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsScrap,
                l["ScrapReport"],
                url: "/reports/scrap",
                icon: TradeXpressIcons.Report,
                order: 6
            ));

            // Mamül Stok Raporu (eski yeri Mamüller'in altıydı)
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsGoodStock,
                l["GoodStockReport"],
                url: "/reports/good-stock",
                icon: TradeXpressIcons.Report,
                order: 7
            ));

            // Mamül Hareket Raporu (eski yeri Mamüller'in altıydı)
            reports.AddItem(new ApplicationMenuItem(
                TradeXpressMenus.ReportsGoodMovement,
                l["GoodMovementReport"],
                url: "/reports/good-movement",
                icon: TradeXpressIcons.Report,
                order: 8
            ));

            context.Menu.AddItem(reports);
        }

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
            icon: "custom-icon-settings",
            order: 1000,
            target: "_blank").RequireAuthenticated());

        await Task.CompletedTask;
    }
}
