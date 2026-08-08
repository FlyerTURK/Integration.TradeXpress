using Volo.Abp.BackgroundWorkers;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;
using Integration.TradeXpress.Blazor.Components;
using Integration.TradeXpress.Blazor.Client;
using Integration.Framework.Blazor.Client;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiTenancy;
using Volo.Abp;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Components.Web;
using Volo.Abp.AspNetCore.Components.WebAssembly.WebApp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.Swashbuckle;
using Volo.Abp.UI.Navigation;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;


namespace Integration.TradeXpress.Blazor;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(Volo.Abp.AspNetCore.Mvc.UI.Bootstrap.AbpAspNetCoreMvcUiBootstrapModule),
    typeof(Volo.Abp.AspNetCore.Mvc.UI.Bundling.AbpAspNetCoreMvcUiBundlingModule),
    typeof(TradeXpressApplicationModule),
    typeof(TradeXpressEntityFrameworkCoreModule),
    typeof(TradeXpressHttpApiModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(Volo.Abp.AspNetCore.Components.Server.AbpAspNetCoreComponentsServerModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(IntegrationFrameworkBlazorClientModule)
)]
public class TradeXpressBlazorModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
        {
            options.AddAssemblyResource(
                typeof(TradeXpressResource),
                typeof(TradeXpressDomainSharedModule).Assembly,
                typeof(TradeXpressApplicationModule).Assembly,
                typeof(TradeXpressApplicationContractsModule).Assembly,
                typeof(TradeXpressBlazorModule).Assembly
            );
        });

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("TradeXpress");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", configuration["AuthServer:CertificatePassPhrase"]!);
                serverBuilder.SetIssuer(new Uri(configuration["AuthServer:Authority"]!));
            });
        }

        PreConfigure<AbpAspNetCoreComponentsWebOptions>(options =>
        {
            options.IsBlazorWebApp = true;
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        // Sıra korunur (bazı kayıtlar "son kayıt kazanır" / options sırasına duyarlı olabilir).
        ConfigureRazorComponentsAndUi(context);
        ConfigureAuthentication(context);
        ConfigureAppUrls(configuration);
        ConfigureAutoApiControllers();
        ConfigureNavigation(configuration);
        ConfigureLocalization();
        ConfigureLoginPage(context);
        ConfigureClientServices(context);
        RegisterClientMapperlyMappers(context);

        // N11 sahte sunucusu ayarları. Bölüm yoksa Enabled=false → hiçbir şey değişmez.
        // (Uç adresleri N11:Endpoints bölümünden Application modülünde bağlanıyor.)
        context.Services.Configure<Integration.TradeXpress.Mocks.N11.N11MockOptions>(
            configuration.GetSection(Integration.TradeXpress.Mocks.N11.N11MockOptions.SectionName));
    }

    /// <summary>Razor Components (Server+WASM hibrit) + DevExpress + framework CRUD taban ayarları.</summary>
    private void ConfigureRazorComponentsAndUi(ServiceConfigurationContext context)
    {
        // Framework CRUD bileşenleri (CrudComponentBase) constructor'da
        // LocalizationResource atıyor — Server modunda da resource'u set et.
        Integration.Framework.Blazor.Client.Components.Crud.CrudComponentBase.DefaultLocalizationResource
            = typeof(TradeXpressResource);

        //https://github.com/dotnet/aspnetcore/issues/52530
        Configure<RouteOptions>(options =>
        {
            options.SuppressCheckForUnhandledSecurityMetadata = true;
        });

        // Blazor Server circuit'i JS→.NET büyük dönüşlerde (ör. istemci-yakalanan video poster JPEG'i) varsayılan 32KB
        // SignalR mesaj sınırını aşınca TaskCanceledException veriyordu → sınırı 16 MB'a çıkar.
        Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
        {
            options.MaximumReceiveMessageSize = 16 * 1024 * 1024;
        });

        // Add services to the container.
        context.Services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                // Blazor Server oturum (circuit) dayanıklılığı: kullanıcı sayfadan ayrılınca (sekme arka plana geçince
                // / inaktivite) SignalR bağlantısı kopar. Varsayılan saklama süresi ~3 dk olduğundan, biraz sonra
                // dönen kullanıcı oturumunu bulamaz → tüm açık tablar/state SIFIRLANIR. Süreyi 30 dk'ya çıkarıp
                // saklanan circuit sayısını artırıyoruz → makul terk-dönüşlerde reconnect aynı oturuma bağlanır,
                // açık ekran/tablar korunur.
                options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
                options.DisconnectedCircuitMaxRetained = 200;
            })
            .AddInteractiveWebAssemblyComponents();

        Configure<AbpMvcLibsOptions>(options =>
        {
            options.CheckLibs = false;
        });

        context.Services.AddDevExpressBlazor(options => {
            options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
        });
    }

    /// <summary>Bearer yönlendirmesi (OpenIddict) + dinamik claim üretimi.</summary>
    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    /// <summary>Uygulama kök URL'i + izinli redirect listesi.</summary>
    private void ConfigureAppUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });
    }

    /// <summary>Application katmanından otomatik API controller üretimi.</summary>
    private void ConfigureAutoApiControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(TradeXpressApplicationModule).Assembly);
        });
    }

    /// <summary>Sol menü katkıcısı.</summary>
    private void ConfigureNavigation(IConfiguration configuration)
    {
        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new Integration.TradeXpress.Blazor.Client.Navigation.TradeXpressMenuContributor(configuration));
        });
    }

    /// <summary>Varsayılan lokalizasyon kaynağı.</summary>
    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.DefaultResourceType = typeof(TradeXpressResource);
        });
    }

    /// <summary>ABP'nin yerleşik login Razor Page'i kapatılır; challenge'lar Blazor login sayfasına yönlenir.</summary>
    private void ConfigureLoginPage(ServiceConfigurationContext context)
    {
        // Suppress ABP's built-in /Account/Login Razor Page so our Blazor component owns the route.
        context.Services.Configure<Microsoft.AspNetCore.Mvc.RazorPages.RazorPagesOptions>(options =>
        {
            options.Conventions.Add(new DisableAbpLoginPageConvention());
        });

        // Redirect unauthenticated cookie-auth challenges to our Blazor login page.
        context.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, options =>
        {
            options.LoginPath = "/account/login";
        });
    }

    /// <summary>Client (WASM) modülündeki servislerin server DI'ına ELLE kaydı — client modülü server'ın
    /// DependsOn zincirinde olmadığından auto-scan/conventional registration burada çalışmaz.</summary>
    private void ConfigureClientServices(ServiceConfigurationContext context)
    {
        // Server render mode için servisler (WASM modülünde browser DI'da kayıtlı,
        // Server modunda host DI'da kayıtlı olması gerekiyor).
        // RouteResolver: durumsuzdur → Singleton OK.
        // Diğerleri: kullanıcı/devre başına durum taşır → Scoped (her SignalR circuit ayrı instance).
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Theming.IThemeService,
                                    Integration.TradeXpress.Blazor.Client.Theming.ThemeService>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Theming.ISizeModeService,
                                    Integration.TradeXpress.Blazor.Client.Theming.SizeModeService>();
        // LookupComboBox ekle/düzelt düğmelerinin VARSAYILAN hedefi (2026-08-07 Hakan kuralı: düğmeler
        // varsayılan görünür — "yoksa standart combo zaten işimizi görüyor"). Eşleme uygulamaya aittir;
        // Framework hiçbir uygulama tipini tanımaz.
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Components.Crud.ILookupEditComponentRegistry>(
            Integration.TradeXpress.Blazor.Client.Services.TradeXpressLookupEditComponents.Build());

        context.Services.AddSingleton<Integration.TradeXpress.Blazor.Client.Services.Mdi.RouteResolver>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager,
                                    Integration.TradeXpress.Blazor.Client.Services.Mdi.TabManager>();
        context.Services.AddScoped<Integration.Framework.Blazor.Client.Services.Mdi.IMdiTabOpener>(
            sp => (Integration.TradeXpress.Blazor.Client.Services.Mdi.TabManager)sp.GetRequiredService<Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager>());
        context.Services.AddScoped<Integration.Framework.Blazor.Client.Resilience.DevErrorSink>();

        // EntityProfile'lar (kimlik tek-kaynak; Faz 1 pilot Vault + parent Branch). Registry framework modülünde
        // (DependsOn'da) kayıtlı; profiller client modülde → server DependsOn zincirinde DEĞİL → ELLE kaydet,
        // yoksa server'da registry boş kalır (Get(VaultListDto) fırlatır). WorkingContextService ile AYNI desen.
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile,
                                      Integration.TradeXpress.Blazor.Client.Profiles.VaultProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile,
                                      Integration.TradeXpress.Blazor.Client.Profiles.BranchProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile,
                                      Integration.TradeXpress.Blazor.Client.Profiles.CompanyProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile,
                                      Integration.TradeXpress.Blazor.Client.Profiles.AccountProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile,
                                      Integration.TradeXpress.Blazor.Client.Profiles.SubAccountProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile,
                                      Integration.TradeXpress.Blazor.Client.Profiles.UserProfile>();

        // Çalışma bağlamı (working context) — sol menü footer'ındaki şube seçici sürer (server-side elle kayıt;
        // client modülü DependsOn zincirinde değil → client modüldeki kayıt server'da çalışmaz).
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService,
                                   Integration.TradeXpress.Blazor.Client.Services.Working.WorkingContextService>();

        // ICurrentCompany köprüsü — working şubenin şirketini sunucu-ambient ICurrentCompany'ye taşır. Client modülü
        // DependsOn'da olmadığından [Dependency(ReplaceServices)] sunucuda çalışmaz → elle kaydet (son kayıt kazanır).
        // Yoksa ICurrentCompany.Id sunucuda DAİMA null → yerel kur re-base / pozisyon / emtia scope çözülmez.
        context.Services.AddScoped<Integration.TradeXpress.MultiCompany.ICompanyContextProvider,
                                   Integration.TradeXpress.Blazor.Client.Services.Working.WorkingCompanyContextProvider>();

        // ICurrentBranch / ICurrentVault köprüsü — çalışma bağlamı artık KASA hassasiyetinde (şirket+şube+kasa).
        // Aynı gerekçe: client modülü DependsOn'da değil → [Dependency(ReplaceServices)] sunucuda çalışmaz, elle kaydet.
        // Kaynak singleton WorkingSelectionStore'dur (UoW child scope'unda boş kopya tuzağı).
        // NOT: kasa ambient'i hiçbir query-filter'a BAĞLANMAZ — ortam varsayılanı, kısıtlama değil.
        context.Services.AddScoped<Integration.TradeXpress.Branches.IBranchContextProvider,
                                   Integration.TradeXpress.Blazor.Client.Services.Working.WorkingBranchContextProvider>();
        context.Services.AddScoped<Integration.TradeXpress.Vaults.IVaultContextProvider,
                                   Integration.TradeXpress.Blazor.Client.Services.Working.WorkingVaultContextProvider>();

        // Working seçiminin scope-bağımsız SSOT'u (per-user, SINGLETON) — ABP UoW child scope'larındaki DbContext
        // filtresi seçimi buradan okur; scoped WorkingContextService'in boş kopyasına düşmez (owned kayıtların
        // "yazıldı ama görünmüyor / parent bulunamadı" kök-neden fix'i). Client modülü DependsOn'da değil → elle kayıt.
        context.Services.AddSingleton<Integration.TradeXpress.Blazor.Client.Services.Working.WorkingSelectionStore>();

        // Fiş satırı kaydının TEK karar noktası (dış cari → normal fiş yolu · iç kasa → Teyit ayna onayı).
        // Client modülü DependsOn zincirinde değil → server'da da ELLE kaydedilir, yoksa paneller DI'da patlar.
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions.VoucherLinePersister>();

        // Identity Management Services
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.IIdentityUserService,
                                   Integration.TradeXpress.Blazor.Client.Services.IdentityUserService>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.IIdentityRoleService,
                                   Integration.TradeXpress.Blazor.Client.Services.IdentityRoleService>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.Identity.UserCrudAdapter>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.Identity.RoleCrudAdapter>();

        // Faz 4 — Referans lookup cache (read-koordinatör): CurrencyUnit. Edit form'lar API yerine bundan besler
        // (5dk TTL); CrudEditHost commit/delete'te typeof(ListDto).FullName ile notify → cache auto-invalidate.
        context.Services.AddScoped<Integration.Framework.Blazor.Client.Services.Base.ILookupCache<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListDto>>(sp =>
        {
            var svc = sp.GetRequiredService<Integration.TradeXpress.Financials.CurrencyUnits.ICurrencyUnitAppService>();
            return new Integration.Framework.Blazor.Client.Services.Base.LookupCache<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListDto>(
                async ct =>
                {
                    var page = await svc.GetListAsync(new Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListRequestDto { MaxResultCount = 1000 });
                    return new System.Collections.Generic.List<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListDto>(page.Items);
                },
                sp.GetRequiredService<Integration.Framework.Blazor.Client.Services.Mdi.IEntityChangeNotifier>(),
                typeof(Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListDto).FullName!);
        });

        // Client modülü Server'ın DependsOn zincirinde olmadığından ITransientDependency
        // auto-scan çalışmıyor; circuit-level servisler burada manuel kayıtlanır.
        context.Services.AddTransient<Integration.Framework.Blazor.Client.Services.Base.IUiStateService,
                                      Integration.TradeXpress.Blazor.Client.Services.TradeXpressUiStateService>();
        // Yakalanan teknik hataları Developer Error Panel'e taşıyan köprü (Blazor Server'da ILogger tarayıcıya gitmez).
        context.Services.AddTransient<Integration.Framework.Blazor.Client.Services.Base.IClientErrorReporter,
                                      Integration.Framework.Blazor.Client.Resilience.DevErrorReporter>();
        // Grid export assembly lazy-loader (CrudLayout + DrillList ortak; Server'da no-op, WASM'da lazy-load).
        context.Services.AddScoped<Integration.Framework.Blazor.Client.Components.Crud.IGridExportAssemblyLoader,
                                   Integration.Framework.Blazor.Client.Components.Crud.GridExportAssemblyLoader>();
    }

    /// <summary>
    /// WASM'dan ABP web-app'e (Server+WASM hibrit) geçişte uçan kayıt: TradeXpress.Blazor.Client
    /// modülü WASM-only bağımlılıkları (AbpAutofacWebAssemblyModule vb.) yüzünden server'ın
    /// [DependsOn] zincirine alınamaz, dolayısıyla o assembly'deki Mapperly mapper'ları sunucu
    /// DI'ında otomatik kayıtlanmaz. CrudPageBase sunucuda render edilen edit formlarında
    /// GetDto↔ViewModel / ViewModel→Create/Update map'lerini çağırır; kayıt olmadan
    /// "No object mapping was found" atar. Bu metot o mapper'ları (yalnızca onları) tarayıp
    /// IObjectMapper&lt;TSource,TDestination&gt; olarak server DI'ına ekler.
    /// </summary>
    private static void RegisterClientMapperlyMappers(ServiceConfigurationContext context)
    {
        var assembly = typeof(Integration.TradeXpress.Blazor.Client.TradeXpressBlazorClientModule).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract) continue;

            // ABP Mapperly mapper'ları IAbpMapperlyMapper<TSource,TDestination> (ve iki-yönlüyse
            // IAbpReverseMapperlyMapper<,>) uygular; MapperlyAutoObjectMappingProvider bunları
            // DI'dan bu arayüzlerle çözer. Conventional registration'ın yaptığını birebir taklit et.
            var mapperInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType &&
                    (i.GetGenericTypeDefinition() == typeof(Volo.Abp.Mapperly.IAbpMapperlyMapper<,>) ||
                     i.GetGenericTypeDefinition() == typeof(Volo.Abp.Mapperly.IAbpReverseMapperlyMapper<,>)))
                .ToArray();

            if (mapperInterfaces.Length == 0) continue;

            context.Services.AddTransient(type);
            foreach (var mapperInterface in mapperInterfaces)
                context.Services.AddTransient(mapperInterface, type);
        }
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var env = context.GetEnvironment();
        var app = context.GetApplicationBuilder();

        // Middleware + endpoint SIRASI kritik — alt metotlar çağrı sırasını birebir korur.
        ConfigureRequestPipeline(app, env);

        app.UseConfiguredEndpoints(builder =>
        {
            MapSignInCookieEndpoint(builder);
            MapFindTenantEndpoint(builder);
            MapEtsyOAuthCallbackEndpoint(builder);
            MapN11MockEndpointsIfEnabled(builder, context);
            MapBlazorComponents(builder);
        });

        StartHaremFeedWorkerIfEnabled(context);

        // N11 host-global referans (il/ilçe + kargo firması) nightly re-sync. YALNIZ Blazor host'ta kayıtlı →
        // çift-çalışma önleme. İlk tur 24s sonra; host kimliği (N11:CategorySync) yoksa sessizce atlar.
        //
        // ⚠ SAHTE SUNUCU KİPİNDE KAYDEDİLMEZ. Bu senkron TAM re-sync'tir: N11'den gelmeyen il/ilçe ve kargo
        // firması BAYAT sayılıp SİLİNİR (N11CityAppService:123 · N11ShipmentCompanyAppService:85). Sahte sunucu
        // bu uçları servis etmediği için bugün fetch 404 alıp senkronu silmeye VARMADAN düşürüyor — ama güvenlik
        // "mock'ta tesadüfen o rota yok" olamaz: mock'a bir catch-all ya da boş yanıt eklendiği gün 81 il, tüm
        // ilçeler ve 68 kargo firması silinir. Hesap kapalı olduğu için o veri GERİ GETİRİLEMEZ.
        // (Kategori ağacı bu riskin DIŞINDA: N11Categories tarafında hiçbir silme yok, upsert-only.)
        if (!IsN11MockActive(context))
        {
            Volo.Abp.Threading.AsyncHelper.RunSync(() =>
                context.AddBackgroundWorkerAsync<Integration.TradeXpress.N11.N11ReferenceSyncWorker>());
        }

        // Sipariş SEED worker — boş kanalları streaming (order başına commit) doldurur. YALNIZ Blazor host'ta.
        Volo.Abp.Threading.AsyncHelper.RunSync(() =>
            context.AddBackgroundWorkerAsync<Integration.TradeXpress.Orders.OrderSyncBackgroundWorker>());

        // 15-dk repricing döngüsü (ADR-PRODUCT-ORCHESTRATION Dilim 2) — kanal fiyat/stok tazeleme sinyali.
        // YALNIZ Blazor host'ta (çift-çalışma yok — diğer worker'larla aynı tekilleştirme).
        Volo.Abp.Threading.AsyncHelper.RunSync(() =>
            context.AddBackgroundWorkerAsync<Integration.TradeXpress.Orchestration.RepricingCycleWorker>());

        // Trendyol batch DURUM işçisi (5 dk) — asenkron gönderimleri çözer: COMPLETED'da LastSent* terfi eder ve
        // push geçmişi yazılır. Bu worker olmadan finalizasyon yalnız elle "durum yenile"ye bağlı kalır ve
        // çözülmeyen kayıt çifte-batch guard'ı yüzünden kalıcı kilitlenir. YALNIZ Blazor host'ta.
        Volo.Abp.Threading.AsyncHelper.RunSync(() =>
            context.AddBackgroundWorkerAsync<Integration.TradeXpress.TrendyolProducts.TrendyolBatchStatusWorker>());

        // Etsy taxonomy TAM-RECONCILE worker (günlük; RunOnStart=true → açılışta İLK bayatlık kontrolü). Bayat/boşsa
        // reconcile (ekle/güncelle/HARD-sil); değilse atlar. YALNIZ Blazor host'ta → çift-çalışma yok.
        Volo.Abp.Threading.AsyncHelper.RunSync(() =>
            context.AddBackgroundWorkerAsync<Integration.TradeXpress.EtsyTaxonomies.EtsyTaxonomySyncWorker>());

        // N11 kategori ağacı + komisyon mutabakatı (günlük; RunOnStart=true → açılışta İLK bayatlık kontrolü).
        // Damga 1 günden yeniyse N11'e istek bile gitmez. Komisyon bu turun parçası — kullanıcı düğmeye basmaz.
        // YALNIZ Blazor host'ta → çift-çalışma yok (dağıtık kilit sağlayıcısı kayıtlı değil, iki hostta kayıt
        // aynı anda iki tam ağaç çekimi + ExternalId unique index çakışması demek olurdu).
        //
        // ⚠ SAHTE SUNUCU KİPİNDE KAYDEDİLMEZ. Gerekçe referans worker'ınkinden FARKLI: kategori senkronu SİLMEZ
        // (upsert-only), ama mock'a CategoryService ucu eklendikten sonra (kimlik probu için) senkron mock'tan
        // BAŞARILI yanıt alabilir hâle geldi: REST /cdn/categories bulunamayıp SOAP'a düşer, mock oradan tek bir
        // sahte kategori döndürür ve bu HOST-GLOBAL tabloya yazılır. 4400 gerçek kategorinin arasına sahte satır
        // karışması, silinmesi kadar olmasa da kirlenmedir ve tüm tenant'ları etkiler.
        if (!IsN11MockActive(context))
        {
            Volo.Abp.Threading.AsyncHelper.RunSync(() =>
                context.AddBackgroundWorkerAsync<Integration.TradeXpress.N11Categories.N11CategorySyncWorker>());
        }

        // Müşteri sorusu senkronu — pazaryerine giden TEK MERKEZ (UI asla doğrudan çağırmaz). Periyot 1 DAKİKA
        // = N11 kota penceresi; her tur TEK iş adımı harcar. "5 dakikada bir tazeleme" kararı worker periyoduyla
        // değil kanal-başı eşikle sağlanır (bkz. ChannelQuestionSyncWorker özeti). YALNIZ Blazor host'ta →
        // iki süreçte kayıt, aynı dakikada iki çağrı = garanti accessLimit demek olurdu.
        Volo.Abp.Threading.AsyncHelper.RunSync(() =>
            context.AddBackgroundWorkerAsync<Integration.TradeXpress.ChannelQuestions.ChannelQuestionSyncWorker>());
    }

    /// <summary>HTTP request pipeline'ı (middleware zinciri) — sıralama duyarlı, dokunma.</summary>
    private static void ConfigureRequestPipeline(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Configure the HTTP request pipeline.
        if (env.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseAbpRequestLocalization();
        app.UseStaticFiles();
        app.UseRouting();
        app.MapAbpStaticAssets();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }

        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAntiforgery();
        app.UseAuthorization();
        app.UseAbpSerilogEnrichers();
    }

    // ── Custom login endpoints ────────────────────────────────────────────
    // /account/sign-in-cookie: validates credentials + sets ASP.NET Identity
    // auth cookie so the browser picks it up (browser-side fetch, not server
    // HttpClient, so Set-Cookie actually reaches the user's cookie jar).
    private static void MapSignInCookieEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/account/sign-in-cookie", async (
                [Microsoft.AspNetCore.Mvc.FromBody] SignInCookieRequest                req,
                [Microsoft.AspNetCore.Mvc.FromServices] Volo.Abp.MultiTenancy.ITenantStore       tenantStore,
                [Microsoft.AspNetCore.Mvc.FromServices] Volo.Abp.MultiTenancy.ICurrentTenant     currentTenant,
                [Microsoft.AspNetCore.Mvc.FromServices] Volo.Abp.SettingManagement.ISettingManager settingManager,
                [Microsoft.AspNetCore.Mvc.FromServices] Microsoft.AspNetCore.Identity.SignInManager<Volo.Abp.Identity.IdentityUser> signInManager) =>
            {
                if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
                    return Microsoft.AspNetCore.Http.Results.Json(new { success = false, error = "Credentials required." });

                Guid? tenantId = null;
                if (!string.IsNullOrWhiteSpace(req.TenantName))
                {
                    var tenant = await tenantStore.FindAsync(req.TenantName.Trim());
                    if (tenant == null)
                        return Microsoft.AspNetCore.Http.Results.Json(new { success = false, error = "Tenant not found." });
                    tenantId = tenant.Id;
                }

                using (currentTenant.Change(tenantId))
                {
                    var result = await signInManager.PasswordSignInAsync(
                        req.UserName, req.Password,
                        isPersistent: true, lockoutOnFailure: false);

                    if (!result.Succeeded)
                        return Microsoft.AspNetCore.Http.Results.Json(new { success = false, error = "Invalid credentials." });

                    // Kullanıcının UI tercihleri (tema/boyut/dil) yanıta eklenir → Login bunları ayna
                    // cookie'lerine yansıtır; forceLoad sonrası App.razor SSR'ı DOĞRUDAN bu kullanıcının
                    // görünümüyle boyar (ara flaş/ikinci reload yok). Hata girişi ENGELLEMEZ (best-effort).
                    string? theme = null, size = null, culture = null;
                    try
                    {
                        var user = await signInManager.UserManager.FindByNameAsync(req.UserName);
                        if (user != null)
                        {
                            theme   = await settingManager.GetOrNullForUserAsync(Integration.TradeXpress.Settings.TradeXpressUiSettingNames.Theme,    user.Id, fallback: false);
                            size    = await settingManager.GetOrNullForUserAsync(Integration.TradeXpress.Settings.TradeXpressUiSettingNames.SizeMode, user.Id, fallback: false);
                            culture = await settingManager.GetOrNullForUserAsync(Integration.TradeXpress.Settings.TradeXpressUiSettingNames.Culture,  user.Id, fallback: false);

                            // K1 iş kuralı: login ekranında dile AÇIKÇA dokunulduysa o dil kazanır ve
                            // kullanıcının yeni sunucu tercihi olarak yazılır; dokunulmadıysa saklı tercih döner.
                            if (!string.IsNullOrWhiteSpace(req.ChosenCulture)
                                && (req.ChosenCulture is "tr" or "en"))
                            {
                                culture = req.ChosenCulture;
                                await settingManager.SetForUserAsync(user.Id, Integration.TradeXpress.Settings.TradeXpressUiSettingNames.Culture, req.ChosenCulture);
                            }
                        }
                    }
                    catch { /* tercih okunamazsa null döner — giriş etkilenmez */ }

                    return Microsoft.AspNetCore.Http.Results.Json(new
                    {
                        success = true,
                        prefs = new
                        {
                            theme   = string.IsNullOrWhiteSpace(theme)   ? null : theme,
                            size    = string.IsNullOrWhiteSpace(size)    ? null : size,
                            culture = string.IsNullOrWhiteSpace(culture) ? null : culture,
                        },
                    });
                }
            }).AllowAnonymous().DisableAntiforgery();

    }

    // /account/find-tenant: validates a tenant name before the user submits credentials.
    private static void MapFindTenantEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/account/find-tenant", async (
                string? name,
                [Microsoft.AspNetCore.Mvc.FromServices] Volo.Abp.MultiTenancy.ITenantStore tenantStore) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return Microsoft.AspNetCore.Http.Results.Ok(new { found = false, name = (string?)null });
                try
                {
                    var tenant = await tenantStore.FindAsync(name.Trim());
                    if (tenant == null)
                        return Microsoft.AspNetCore.Http.Results.Ok(new { found = false, name = (string?)null });
                    return Microsoft.AspNetCore.Http.Results.Ok(new { found = true, name = tenant.Name });
                }
                catch
                {
                    return Microsoft.AspNetCore.Http.Results.Ok(new { found = false, name = (string?)null });
                }
            }).AllowAnonymous();

    }

    // /etsy/oauth-callback: Etsy OAuth 2.0 (PKCE) geri dönüşü — satıcı Etsy'de onay verince Etsy tarayıcıyı buraya
    // yönlendirir (redirect URI Etsy uygulama kaydında birebir tanımlı: App:SelfUrl + /etsy/oauth-callback).
    // Minimal-API deseni (sign-in-cookie/find-tenant emsali). Endpoint'te [Authorize] YOK (fallback policy de yok →
    // ASP.NET varsayılanıyla erişilebilir): OAuth callback'inin kimliği sunucuda saklanan TEK-KULLANIMLIK STATE'tir
    // (CSRF nonce → kanal/tenant/verifier cache'te, 10 dk TTL; bilinmeyen state reddedilir) — cross-site redirect'te
    // auth cookie'ye güvenmek SameSite'a göre kırılgan olurdu. İş mantığı (state doğrula → token değişimi → kanala
    // yaz) IEtsyOAuthService'te; endpoint yalnız sonucu kullanıcı yönlendirmesine çevirir.
    private static void MapEtsyOAuthCallbackEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/etsy/oauth-callback", async (
                string? state,
                string? code,
                string? error,
                [Microsoft.AspNetCore.Mvc.FromServices] Integration.TradeXpress.SalesChannels.Etsy.IEtsyOAuthService oauthService) =>
            {
                var result = await oauthService.HandleCallbackAsync(state, code, error);

                // Kanal biliniyorsa Etsy edit sayfasına (başarı/hata bayrağıyla — UI toast gösterir), bilinmiyorsa
                // (state çözülemedi) genel kanal listesine dön.
                var target = result.ChannelId is { } channelId
                    ? $"/sales-channels/etsy/{channelId}?oauth={(result.Success ? "ok" : "err")}"
                    : "/sales-channels?oauth=err";
                return Microsoft.AspNetCore.Http.Results.Redirect(target);
            });
    }

    /// <summary>Razor Components kök haritası (Server + WASM render modları + client assembly'leri).</summary>
    private static void MapBlazorComponents(IEndpointRouteBuilder builder)
    {
        builder.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(WebAppAdditionalAssembliesHelper.GetAssemblies<TradeXpressBlazorClientModule>());
    }

    // Harem fiyat feed'i — TEK sahip burada (Kur Panosu bu Blazor host'ta render edilir ve
    // ExchangeRateCacheService process-başına singleton'dır). In-process Microsoft.Playwright ile
    // (bundled Chromium, headless) Harem socket.io WS'i dinlenir — harici HaremBridge (Node/Python/8765)
    // GEREKMEZ. HaremEnabled=false ise hiç başlatılmaz.
    /// <summary>
    /// N11 sahte sunucusu ETKİN mi — ÜÇ kapı birden: geliştirme ortamı + <c>N11:Mock:Enabled</c> +
    /// taban adresin gerçek N11'den BAŞKA bir yeri göstermesi.
    ///
    /// <para>Üçüncü kapı belirleyici: bayrak açık olsa bile taban adres hâlâ <c>api.n11.com</c> ise hiçbir
    /// istek mock'a gitmez, dolayısıyla "mock kipi" de değiliz. Worker kararı bu yüzden bayrağa değil
    /// <b>trafiğin nereye gittiğine</b> bakar.</para>
    /// </summary>
    private static bool IsN11MockActive(ApplicationInitializationContext context)
    {
        var env = context.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        if (!env.IsDevelopment())
        {
            return false;
        }

        var mock = context.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Integration.TradeXpress.Mocks.N11.N11MockOptions>>().Value;
        if (!mock.Enabled)
        {
            return false;
        }

        var endpoints = context.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Integration.TradeXpress.N11Products.N11EndpointOptions>>().Value;
        return endpoints.IsRedirected;
    }

    /// <summary>Sahte N11 uçlarını haritalar — yalnız üç kapı da açıkken. Kapalıysa hiçbir rota kaydedilmez
    /// (üretimde bu kod zaten derlenmiyor: proje referansı Debug-only).</summary>
    private static void MapN11MockEndpointsIfEnabled(IEndpointRouteBuilder builder, ApplicationInitializationContext context)
    {
        if (!IsN11MockActive(context))
        {
            return;
        }

        var options = context.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Integration.TradeXpress.Mocks.N11.N11MockOptions>>().Value;
        var env = context.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var storePath = string.IsNullOrWhiteSpace(options.StorePath)
            ? System.IO.Path.Combine(env.ContentRootPath, "App_Data", "n11-mock-store.json")
            : options.StorePath;

        var store = new Integration.TradeXpress.Mocks.N11.N11MockStore(storePath, options.QueuedPollsBeforeProcessed);
        Integration.TradeXpress.Mocks.N11.N11MockEndpoints.MapN11MockEndpoints(builder, store, options);
        Integration.TradeXpress.Mocks.N11.N11MockOrderEndpoint.MapN11MockOrderEndpoint(builder, store, options);
        Integration.TradeXpress.Mocks.N11.N11MockProductServiceEndpoint.MapN11MockProductServiceEndpoint(builder, store, options);

        var logger = context.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .CreateLogger("N11Mock");
        logger.LogWarning(
            "N11 SAHTE SUNUCUSU ETKİN — tüm N11 istekleri {BaseUrl} adresine gidiyor, GERÇEK N11'e DEĞİL. Depo: {StorePath}",
            context.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Integration.TradeXpress.N11Products.N11EndpointOptions>>()
                .Value.BaseUrl,
            storePath);
    }

    private static void StartHaremFeedWorkerIfEnabled(ApplicationInitializationContext context)
    {
        var feedOptions = context.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Integration.TradeXpress.Financials.ExchangeRates.ExchangeRateOptions>>().Value;
        if (feedOptions.HaremEnabled)
        {
            Volo.Abp.Threading.AsyncHelper.RunSync(() =>
                context.AddBackgroundWorkerAsync<Integration.TradeXpress.Financials.ExchangeRates.HaremPlaywrightFeedWorker>());
        }
    }
}

file sealed record SignInCookieRequest(string UserName, string Password, string? TenantName, string? ChosenCulture = null);

file sealed class DisableAbpLoginPageConvention
    : Microsoft.AspNetCore.Mvc.ApplicationModels.IPageRouteModelConvention
{
    public void Apply(Microsoft.AspNetCore.Mvc.ApplicationModels.PageRouteModel model)
    {
        if (model.AreaName == "Account" &&
            model.RelativePath.EndsWith("/Login.cshtml", StringComparison.OrdinalIgnoreCase))
        {
            model.Selectors.Clear();
        }
    }
}
