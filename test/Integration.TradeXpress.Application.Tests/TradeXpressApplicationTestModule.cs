using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Modularity;
using Integration.TradeXpress.N11Products.Rest;

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
        // Push artik REST'ten gidiyor (SOAP urun uclari N11 tarafinda kapatildi) → REST istemcisi ve
        // task sorgulayici da sahtelenir; aksi halde testler aga cikmaya calisirdi.
        context.Services.AddSingleton<FakeN11ProductRestClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11ProductRestClient>(sp => sp.GetRequiredService<FakeN11ProductRestClient>()));
        context.Services.AddSingleton<FakeN11TaskPoller>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11TaskPoller>(sp => sp.GetRequiredService<FakeN11TaskPoller>()));
        context.Services.AddSingleton<FakeN11ProductQueryClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11ProductQueryClient>(sp => sp.GetRequiredService<FakeN11ProductQueryClient>()));
        context.Services.AddSingleton<FakeN11CategoryClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11CategoryClient>(sp => sp.GetRequiredService<FakeN11CategoryClient>()));

        // Trendyol ürün REST istemcisi de testte SAHTE — import testleri sahte envanteri okur (READ-ONLY ilke);
        // gruplama mantığı gerçek client'ın static'inden gelir (davranış sahtelenmiyor, yalnız ağ kesiliyor).
        context.Services.AddSingleton<FakeTrendyolProductClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<ITrendyolProductClient>(sp => sp.GetRequiredService<FakeTrendyolProductClient>()));

        // Trendyol KATEGORİ istemcisi de sahte — tam-push testi kategori tanımına karşı doğrulama yapar; gerçek
        // istemci cache MISS'te ağa çıkardı. Konmayan kategori için fırlatır (kazayla tanım isteği görünür kalır).
        context.Services.AddSingleton<FakeTrendyolCategoryClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<TrendyolCategories.ITrendyolCategoryClient>(sp => sp.GetRequiredService<FakeTrendyolCategoryClient>()));

        // Trendyol SİPARİŞ REST istemcisi de testte SAHTE — çekim testleri sahte sipariş envanterini okur (READ-ONLY);
        // sayfalama gerçek client'ın static'inden gelir (davranış sahtelenmiyor, yalnız ağ kesiliyor).
        context.Services.AddSingleton<FakeTrendyolOrderClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<ITrendyolOrderClient>(sp => sp.GetRequiredService<FakeTrendyolOrderClient>()));

        // Etsy listeleme istemcisi de testte SAHTE — içe aktarım testleri sahte mağazayı okur (READ-ONLY ilke);
        // varyasyon fotoğrafı ucu dahil ağın tamamı kesilir, davranış (eşleştirme/indirme) gerçek koddan geçer.
        context.Services.AddSingleton<FakeEtsyProductClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IEtsyProductClient>(sp => sp.GetRequiredService<FakeEtsyProductClient>()));

        // Pazaryeri GÖRSEL indiricisi de sahte — yalnız indirme adımı (bkz. FakeMarketplaceImageDownloader);
        // bağlama akışının tamamı gerçek koddan geçer. Sahte olmadan her URL testte sessizce başarısız oluyor ve
        // içe aktarımın görsel dalı (varyant bağlamı dahil) HİÇ koşmuyordu.
        context.Services.AddTransient<FakeMarketplaceImageDownloader>();
        context.Services.Replace(ServiceDescriptor.Transient<MarketplaceImageDownloader>(
            sp => sp.GetRequiredService<FakeMarketplaceImageDownloader>()));

        // N11 SİPARİŞ SOAP istemcisi de sahte — senkron ZİNCİRİ (çekim → eşleştirme → rezervasyon → iptal
        // bildirimi) testte gerçek ağ olmadan uçtan uca koşsun. Sahte, hangi PENCEREYLE çağrıldığını kaydeder:
        // seed ile delta kolunun karışması ancak böyle görülebilir (ikisi de aynı siparişleri döndürür).
        context.Services.AddSingleton<FakeN11OrderClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IN11OrderClient>(sp => sp.GetRequiredService<FakeN11OrderClient>()));
    }
}
