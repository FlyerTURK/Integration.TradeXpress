using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Modularity;

namespace Integration.TradeXpress;

[DependsOn(
    typeof(TradeXpressApplicationModule),
    typeof(TradeXpressDomainTestModule)
)]
public class TradeXpressApplicationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Dış N11 SOAP/REST istemcileri testte SAHTE — test ortamında ağ yok; push planı sahtede yakalanıp
        // karakterizasyon assert'leriyle doğrulanır (SalesChannelTrN11ProductPushTests). Aynı singleton instance
        // hem interface hem somut tip üzerinden çözülür (test somut tipten yakalanan veriyi okur).
        context.Services.AddSingleton<FakeN11ProductClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11ProductClient>(sp => sp.GetRequiredService<FakeN11ProductClient>()));
        context.Services.AddSingleton<FakeN11CategoryClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11CategoryClient>(sp => sp.GetRequiredService<FakeN11CategoryClient>()));

        // Trendyol ürün REST istemcisi de testte SAHTE — import testleri sahte envanteri okur (READ-ONLY ilke);
        // gruplama mantığı gerçek client'ın static'inden gelir (davranış sahtelenmiyor, yalnız ağ kesiliyor).
        context.Services.AddSingleton<FakeTrendyolProductClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<ITrendyolProductClient>(sp => sp.GetRequiredService<FakeTrendyolProductClient>()));

        // Trendyol SİPARİŞ REST istemcisi de testte SAHTE — çekim testleri sahte sipariş envanterini okur (READ-ONLY);
        // sayfalama gerçek client'ın static'inden gelir (davranış sahtelenmiyor, yalnız ağ kesiliyor).
        context.Services.AddSingleton<FakeTrendyolOrderClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<ITrendyolOrderClient>(sp => sp.GetRequiredService<FakeTrendyolOrderClient>()));
    }
}
