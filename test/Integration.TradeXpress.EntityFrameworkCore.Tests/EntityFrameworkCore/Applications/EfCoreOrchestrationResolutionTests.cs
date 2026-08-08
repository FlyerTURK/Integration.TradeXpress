using Integration.TradeXpress.Orchestration;
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
}
