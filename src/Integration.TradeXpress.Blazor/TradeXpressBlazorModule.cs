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

        // Framework CRUD bileşenleri (CrudComponentBase) constructor'da
        // LocalizationResource atıyor — Server modunda da resource'u set et.
        Integration.Framework.Blazor.Client.Components.Crud.CrudComponentBase.DefaultLocalizationResource
            = typeof(TradeXpressResource);

        //https://github.com/dotnet/aspnetcore/issues/52530
        Configure<RouteOptions>(options =>
        {
            options.SuppressCheckForUnhandledSecurityMetadata = true;
        });

        // Add services to the container.
        context.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();

        Configure<AbpMvcLibsOptions>(options =>
        {
            options.CheckLibs = false;
        });

        context.Services.AddDevExpressBlazor(options => {
            options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
        });

        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });

        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });

        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(TradeXpressApplicationModule).Assembly);
        });

        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new Integration.TradeXpress.Blazor.Client.Navigation.TradeXpressMenuContributor(configuration));
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.DefaultResourceType = typeof(TradeXpressResource);
        });

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

        // Server render mode için servisler (WASM modülünde browser DI'da kayıtlı,
        // Server modunda host DI'da kayıtlı olması gerekiyor).
        // RouteResolver: durumsuzdur → Singleton OK.
        // Diğerleri: kullanıcı/devre başına durum taşır → Scoped (her SignalR circuit ayrı instance).
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Theming.IThemeService,
                                    Integration.TradeXpress.Blazor.Client.Theming.ThemeService>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Theming.ISizeModeService,
                                    Integration.TradeXpress.Blazor.Client.Theming.SizeModeService>();
        context.Services.AddSingleton<Integration.TradeXpress.Blazor.Client.Services.Mdi.RouteResolver>();
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager,
                                    Integration.TradeXpress.Blazor.Client.Services.Mdi.TabManager>();
        context.Services.AddScoped<Integration.Framework.Blazor.Client.Services.Mdi.IMdiTabOpener>(
            sp => (Integration.TradeXpress.Blazor.Client.Services.Mdi.TabManager)sp.GetRequiredService<Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager>());
        context.Services.AddScoped<Integration.TradeXpress.Blazor.Client.Dev.DevErrorSink>();

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
                                      Integration.TradeXpress.Blazor.Client.Dev.DevErrorReporter>();

        RegisterClientMapperlyMappers(context);
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

        app.UseConfiguredEndpoints(builder =>
        {
            // ── Custom login endpoints ────────────────────────────────────────────
            // /account/sign-in-cookie: validates credentials + sets ASP.NET Identity
            // auth cookie so the browser picks it up (browser-side fetch, not server
            // HttpClient, so Set-Cookie actually reaches the user's cookie jar).
            builder.MapPost("/account/sign-in-cookie", async (
                [Microsoft.AspNetCore.Mvc.FromBody] SignInCookieRequest                req,
                [Microsoft.AspNetCore.Mvc.FromServices] Volo.Abp.MultiTenancy.ITenantStore       tenantStore,
                [Microsoft.AspNetCore.Mvc.FromServices] Volo.Abp.MultiTenancy.ICurrentTenant     currentTenant,
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

                    return Microsoft.AspNetCore.Http.Results.Json(new { success = true });
                }
            }).AllowAnonymous().DisableAntiforgery();

            // /account/find-tenant: validates a tenant name before the user submits credentials.
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

            builder.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(WebAppAdditionalAssembliesHelper.GetAssemblies<TradeXpressBlazorClientModule>());
        });

        // Harem fiyat feed worker'ı. WASM→web-app geçişinde HttpApi.Host ayrı süreç olarak
        // çalışmadığından (Kur Panosu bu Blazor host'ta render edilir ve ExchangeRateCacheService
        // process-başına singleton'dır) worker'ı burada başlatıyoruz; aksi halde canlı cache boş
        // kalır ve pano fiyat gösteremez. HaremEnabled=false ise hiç başlatılmaz.
        var feedOptions = context.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Integration.TradeXpress.Financials.ExchangeRates.ExchangeRateOptions>>().Value;
        if (feedOptions.HaremEnabled)
        {
            Volo.Abp.Threading.AsyncHelper.RunSync(() =>
                context.AddBackgroundWorkerAsync<Integration.TradeXpress.Financials.ExchangeRates.ExchangeRateFeedWorker>());
        }
    }
}

file sealed record SignInCookieRequest(string UserName, string Password, string? TenantName);

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
