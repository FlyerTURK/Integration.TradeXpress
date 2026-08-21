using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Integration.TradeXpress.Blazor.Client.Navigation;
using Localization.Resources.AbpUi;
using Volo.Abp.Localization;
using Integration.TradeXpress.Localization;
using OpenIddict.Abstractions;
using Volo.Abp.Autofac.WebAssembly;
using Volo.Abp.Modularity;
using Volo.Abp.UI.Navigation;
using Volo.Abp.Mapperly;
using Integration.Framework.Blazor.Client;

namespace Integration.TradeXpress.Blazor.Client;

[DependsOn(
    typeof(AbpAutofacWebAssemblyModule),
    typeof(TradeXpressHttpApiClientModule),
    typeof(Volo.Abp.AspNetCore.Components.WebAssembly.AbpAspNetCoreComponentsWebAssemblyModule),
    typeof(Volo.Abp.Http.Client.IdentityModel.WebAssembly.AbpHttpClientIdentityModelWebAssemblyModule),
    typeof(Volo.Abp.UI.Navigation.AbpUiNavigationModule),
    typeof(AbpMapperlyModule),
    typeof(IntegrationFrameworkBlazorClientModule)
)]
public class TradeXpressBlazorClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        ConfigureLocalization();

        if (OperatingSystem.IsBrowser())
        {
            var environment = context.Services.GetSingletonInstance<IWebAssemblyHostEnvironment>();
            var builder = context.Services.GetSingletonInstance<WebAssemblyHostBuilder>();
            ConfigureAuthentication(builder);
            ConfigureHttpClient(context, environment);
        }

        context.Services.AddDevExpressBlazor(options => {
            options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
        });

        // Bu servisler WASM'da Singleton — Server modunda host DI'da kayıtlı,
        // WASM modunda browser DI'da kayıtlı. Her iki tarafta da kayıt harmless.
        context.Services.AddSingleton<Theming.IThemeService, Theming.ThemeService>();
        context.Services.AddSingleton<Theming.ISizeModeService, Theming.SizeModeService>();

        // MDI sekme altyapısı
        context.Services.AddSingleton<Services.Mdi.RouteResolver>();
        context.Services.AddSingleton<Services.Mdi.ITabManager, Services.Mdi.TabManager>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Services.Mdi.IMdiTabOpener>(
            sp => (Services.Mdi.TabManager)sp.GetRequiredService<Services.Mdi.ITabManager>());

        // Geliştirici Hata Paneli (sink/reporter Framework'e taşındı; panel bileşeni bu projede)
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Resilience.DevErrorSink>();
        // Yakalanan teknik hataları panele taşıyan IClientErrorReporter (Blazor Server'da ILogger tarayıcıya gitmez).
        context.Services.AddTransient<Integration.Framework.Blazor.Client.Services.Base.IClientErrorReporter,
                                      Integration.Framework.Blazor.Client.Resilience.DevErrorReporter>();
        // Grid export assembly lazy-loader (CrudLayout + DrillList ortak; WASM'da lazy-load, Server'da no-op).
        context.Services.AddScoped<Integration.Framework.Blazor.Client.Components.Crud.IGridExportAssemblyLoader,
                                   Integration.Framework.Blazor.Client.Components.Crud.GridExportAssemblyLoader>();

        // EntityProfile'lar (kimlik tek-kaynak). Registry bunları toplar; drill-tab ikon/başlıkları buradan türer.
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile, Profiles.VaultProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile, Profiles.BranchProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile, Profiles.CompanyProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile, Profiles.AccountProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile, Profiles.SubAccountProfile>();
        context.Services.AddSingleton<Integration.Framework.Blazor.Client.Profiles.EntityProfile, Profiles.UserProfile>();

        // Çalışma bağlamı (working context) — seçili çalışma şubesi (company+branch); sol menü footer combo'su sürer.
        context.Services.AddScoped<Services.Working.IWorkingContextService, Services.Working.WorkingContextService>();

        // Fiş satırı kaydının TEK karar noktası: dış cari → normal fiş yolu · iç kasa → Teyit (mirror onayı).
        // Üç panel hiyerarşisi de (ProcessPanelHostBase / CommodityProcessPanelBase / DebitNoteProcessPanel)
        // bunu tüketir → kural tek yerde yaşar.
        context.Services.AddScoped<Pages.CurrentTransactions.VoucherLinePersister>();

        // Identity Management Services
        context.Services.AddScoped<Services.IIdentityUserService, Services.IdentityUserService>();
        context.Services.AddScoped<Services.IIdentityRoleService, Services.IdentityRoleService>();
        context.Services.AddScoped<Services.Identity.UserCrudAdapter>();
        context.Services.AddScoped<Services.Identity.RoleCrudAdapter>();

        // Faz 4 — Referans lookup cache (read-koordinatör): CurrencyUnit (WASM parite; Server'da host DI kullanılır).
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

        if (OperatingSystem.IsBrowser())
        {
            // Resilience handler yalnız WASM'da (HTTP client WASM'a özel)
            context.Services
                .AddHttpClient(TradeXpressHttpApiClientModule.RemoteServiceName)
                .AddHttpMessageHandler(sp => new Integration.Framework.Blazor.Client.Resilience.ResilienceDelegatingHandler(
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<Integration.Framework.Blazor.Client.Resilience.ResilienceDelegatingHandler>>()));
        }

        ConfigureMenu(context);
    }
    
    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<TradeXpressResource>()
                .AddBaseTypes(typeof(AbpUiResource));
            options.DefaultResourceType = typeof(TradeXpressResource);
        });

        // Framework CRUD bileşenleri (toolbar/popup/footer) app'in resource'unu ctor'da kullansın —
        // böylece "New/Save/Cancel" gibi ham anahtarlar yerine Türkçe çeviriler gelir.
        Integration.Framework.Blazor.Client.Components.Crud.CrudComponentBase.DefaultLocalizationResource
            = typeof(TradeXpressResource);
    }


    private void ConfigureMenu(ServiceConfigurationContext context)
    {
        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new TradeXpressMenuContributor(context.Services.GetConfiguration()));
        });
    }

    private static void ConfigureAuthentication(WebAssemblyHostBuilder builder)
    {
        builder.Services.AddOidcAuthentication(options =>
        {
            builder.Configuration.Bind("AuthServer", options.ProviderOptions);
            options.UserOptions.NameClaim = OpenIddictConstants.Claims.Name;
            options.UserOptions.RoleClaim = OpenIddictConstants.Claims.Role;

            options.ProviderOptions.DefaultScopes.Add("TradeXpress");
            options.ProviderOptions.DefaultScopes.Add("roles");
            options.ProviderOptions.DefaultScopes.Add("email");
            options.ProviderOptions.DefaultScopes.Add("phone");
        });
    }
    
    private static void ConfigureHttpClient(ServiceConfigurationContext context, IWebAssemblyHostEnvironment environment)
    {
        context.Services.AddTransient(sp => new HttpClient
        {
            BaseAddress = new Uri(environment.BaseAddress)
        });
    }
}
