using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Web;
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
        var environment = context.Services.GetSingletonInstance<IWebAssemblyHostEnvironment>();
        var builder = context.Services.GetSingletonInstance<WebAssemblyHostBuilder>();
        var configuration = context.Services.GetConfiguration();

        ConfigureLocalization();
        ConfigureAuthentication(builder);
        ConfigureHttpClient(context, environment);
        context.Services.AddDevExpressBlazor(options => {
            options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
        });

        // Ayarlar paneli servisleri — WASM tek kullanıcılı olduğu için Singleton.
        context.Services.AddSingleton<Theming.IThemeService, Theming.ThemeService>();
        context.Services.AddSingleton<Theming.ISizeModeService, Theming.SizeModeService>();

        // MDI sekme altyapısı — WASM tek kullanıcı → Singleton (NavMenu/MdiTabHost/drill aynı koleksiyonu paylaşır).
        context.Services.AddSingleton<Services.Mdi.RouteResolver>();
        context.Services.AddSingleton<Services.Mdi.ITabManager, Services.Mdi.TabManager>();

        // Geliştirici Hata Paneli — yakalanan tüm runtime hatalarının tek merkezi (Singleton).
        context.Services.AddSingleton<Dev.DevErrorSink>();

        // Resilience: ABP "Default" remote-service client'ına geçici-hata retry handler'ı ekle.
        // Auth handler'ının içinde çalışır (token zaten iliştirilmiş); yalnız idempotent metotları
        // yeniden dener. Handler stateless → her seferinde yeni örnek.
        context.Services
            .AddHttpClient(TradeXpressHttpApiClientModule.RemoteServiceName)
            .AddHttpMessageHandler(sp => new Integration.Framework.Blazor.Client.Resilience.ResilienceDelegatingHandler(
                sp.GetService<Microsoft.Extensions.Logging.ILogger<Integration.Framework.Blazor.Client.Resilience.ResilienceDelegatingHandler>>()));

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
