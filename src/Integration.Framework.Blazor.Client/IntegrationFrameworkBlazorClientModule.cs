using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

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
        context.Services.AddScoped<IViewOpener, DefaultViewOpener>();

        // EntityProfile registry — entity kimliğinin (ikon/başlık/parent/permission/edit-host) TEK KAYNAĞI.
        // Tüketici modüller EntityProfile'larını singleton kaydeder; registry hepsini toplayıp indeksler.
        context.Services.AddSingleton<Profiles.IEntityProfileRegistry, Profiles.EntityProfileRegistry>();

        // ICrudStateService<,> her sayfa ayrı sınıf yazmadan açık-generic kayıttan çözümlenir.
        // SCOPED (interface IScopedDependency ile tutarlı): WASM'da uygulama ömrü boyunca closed-generic
        // başına TEK örnek → aynı entity'nin liste + edit (popup/sekme) + split paneli AYNI state'i paylaşır
        // (seçim, yüklü sayfa, TotalCount/PageSkip tek kaynaktan koordineli). Transient olsaydı her [Inject]
        // ayrı boş instance verirdi (popup gezinmesinin yetim kalma nedeni buydu).
        context.Services.AddScoped(
            typeof(ICrudStateService<,>),
            typeof(DefaultCrudStateService<,>));

        // Sekmeler arası değişim bildirimi (edit sekmesi kaydedince liste sekmesi yenilenir).
        // Scoped: server'da devre başına, WASM'da uygulama ömrü boyunca tek örnek → tüm sekmeler paylaşır.
        context.Services.AddScoped<
            Integration.Framework.Blazor.Client.Services.Mdi.IEntityChangeNotifier,
            Integration.Framework.Blazor.Client.Services.Mdi.EntityChangeNotifier>();
    }
}
