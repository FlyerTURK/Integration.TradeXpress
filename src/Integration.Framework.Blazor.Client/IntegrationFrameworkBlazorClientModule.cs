using Microsoft.Extensions.DependencyInjection;
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
    typeof(IntegrationFrameworkApplicationContractsModule)
)]
public class IntegrationFrameworkBlazorClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddScoped<ITradeXpressUiService, TradeXpressUiService>();

        // ICrudStateService<,,,> kullanan her sayfa ayrı bir sınıf yazmadan çözümlenir.
        context.Services.AddTransient(
            typeof(ICrudStateService<,,,>),
            typeof(DefaultCrudStateService<,,,>));

        // Sekmeler arası değişim bildirimi (edit sekmesi kaydedince liste sekmesi yenilenir).
        // Scoped: server'da devre başına, WASM'da uygulama ömrü boyunca tek örnek → tüm sekmeler paylaşır.
        context.Services.AddScoped<
            Integration.Framework.Blazor.Client.Services.Mdi.IEntityChangeNotifier,
            Integration.Framework.Blazor.Client.Services.Mdi.EntityChangeNotifier>();
    }
}
