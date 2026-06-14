using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Components.WebAssembly;
using Volo.Abp.Modularity;
using Integration.Framework;
using Integration.Framework.Blazor.Client.Services.Base;

namespace Integration.Framework.Blazor.Client;

/// <summary>
/// Integration <b>Framework</b> modülü (Blazor istemci katmanı). Generic CRUD
/// altyapısını (CrudPageBase, CrudLayout, GridListDataSource, resolver'lar, UiService)
/// taşır. Contracts modülünü transitive getirir; tüketici yalnız bunu DependsOn eder.
/// </summary>
[DependsOn(
    typeof(AbpAspNetCoreComponentsWebAssemblyModule),
    typeof(IntegrationFrameworkApplicationContractsModule)
)]
public class IntegrationFrameworkBlazorClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Register ITradeXpressUiService as Scoped
        context.Services.AddScoped<ITradeXpressUiService, TradeXpressUiService>();
    }
}
