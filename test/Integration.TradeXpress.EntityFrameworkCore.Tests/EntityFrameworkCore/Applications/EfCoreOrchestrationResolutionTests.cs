using System.Linq;
using Integration.TradeXpress.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

/// <summary>
/// ORKESTRASYON DI ÇÖZÜMLEMESİ — konteynerden GERÇEKTEN çözülüyor mu.
///
/// <para><b>Neden var (2026-08-08'de yaşandı):</b> <c>ProductStockSyncJob</c> 2026-07-25'ten beri her koşuda
/// <c>Cannot resolve parameter 'ICommodityStockReader stockReader'</c> ile düşüyordu; canlı kuyrukta
/// <b>175 job'ın 175'i</b> <c>IsAbandoned</c> idi ve bu <b>14 gün</b> fark edilmedi. Sebep tek bir eksik
/// <c>[ExposeServices]</c>: ABP bir arayüzü ancak sınıf adı o arayüzün adıyla bitiyorsa açar ve
/// <c>CommodityStockReaderService</c> ↛ <c>ICommodityStockReader</c>.</para>
///
/// <para><b>Neden ayrıca burada pinliyoruz:</b> kardeş <c>DependencyRegistrationConventionTests</c> attribute'ün
/// VARLIĞINI refleksiyonla denetler — ama attribute doğru yazılıp yanlış tip verilse ya da ileride bir modül
/// kaydı bunu ezse yine sessizce kırılırdı. Bu test GERÇEK konteyneri kullanır: tip çözülüyorsa iş görür.
/// Stok orkestrasyonunun tamamı (satılabilir adet → kanal push) bu tek çözüme bağlı.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreOrchestrationResolutionTests : TradeXpressEntityFrameworkCoreTestBase
{
    [Fact]
    public void Commodity_stock_reader_resolves_from_the_container()
    {
        GetRequiredService<ICommodityStockReader>().ShouldNotBeNull();
    }

    /// <summary>Asıl kurban: job'ın KENDİSİ kurulabiliyor mu. Tek bir bağımlılık çözülemezse ctor patlar ve
    /// job her turda düşer — tam olarak 14 gün boyunca olan buydu.</summary>
    [Fact]
    public void Product_stock_sync_job_can_be_constructed()
    {
        GetRequiredService<ProductStockSyncJob>().ShouldNotBeNull();
    }

    /// <summary>Job'ın gördüğü pusher COMPOSITE olmalı — somut bir kanal ayağı değil.
    ///
    /// <para><b>Neden pin:</b> iki somut sınıf aynı arayüzü uygulasaydı hangisinin çözüleceği KAYIT SIRASINA
    /// kalırdı ve bir kanal sessizce hiç push edilmezdi. Hata çıkmaz, log temiz kalır, yalnız o pazaryerindeki
    /// stok bayat kalır.</para></summary>
    [Fact]
    public void The_job_resolves_the_composite_pusher()
    {
        GetRequiredService<IChannelStockPusher>().ShouldBeOfType<CompositeChannelStockPusher>();
    }

    /// <summary>HER kanal ayağı composite'in koleksiyonuna GİRİYOR mu.
    ///
    /// <para>Üye sınıfların adı <c>IChannelStockPusherMember</c> ile bitmediği için ABP'nin varsayılan kaydı
    /// arayüzü AÇMAZ; <c>[ExposeServices]</c> unutulursa composite BOŞ koleksiyon alır ve hiçbir kanal push
    /// edilmez — üstelik hiçbir şey patlamaz. Bu test o sessizliği kırar.</para></summary>
    [Fact]
    public void Every_channel_pusher_member_is_registered()
    {
        var members = ServiceProvider.GetServices<IChannelStockPusherMember>().ToList();

        members.Select(m => m.ChannelName).ShouldBe(new[] { "N11", "Trendyol" }, ignoreOrder: true);
    }
}
