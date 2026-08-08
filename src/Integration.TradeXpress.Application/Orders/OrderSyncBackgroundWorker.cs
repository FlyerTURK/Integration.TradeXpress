using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş senkron arka plan işçisi — İKİ KOL:
/// <list type="number">
/// <item><b>SEED:</b> HİÇ siparişi olmayan kanalın tüm geçmişini streaming çeker (order başına commit).</item>
/// <item><b>DELTA:</b> dolu kanalların dar pencereli periyodik çekimi — <b>varsayılan KAPALI</b>
/// (<see cref="OrderSyncOptions.DeltaEnabled"/>).</item>
/// </list>
///
/// <para>Kanal başına bağımsız try/catch (biri düşse — kimlik/ağ/throttle — worker çökmez). YALNIZ Blazor
/// host'ta kayıtlı (çift-çalışma yok). Okumalar dış UoW'da; her order kendi UoW'unda commit.</para>
///
/// <para><b>Delta neden kapalı doğuyor:</b> açık olduğunda worker gerçek pazaryeri kimliğiyle 2 dakikada bir
/// GERÇEK API'ye çıkar ve çektiği her sipariş rezervasyon zincirini tetikler (stok düşer). Bunun kodun merge
/// edilmesiyle değil bilinçli bir kararla başlaması gerekir — deploy/restart kapıyı delemez.</para>
/// </summary>
public class OrderSyncBackgroundWorker : AsyncPeriodicBackgroundWorkerBase
{
    public OrderSyncBackgroundWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;   // boş kanal varsa hızlı seed; doluysa ucuz atlar
        Timer.RunOnStart = true;                                          // uygulama ayağa kalkar kalkmaz İLK tur (2dk bekleme yok)
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var manager = workerContext.ServiceProvider.GetRequiredService<OrderSyncManager>();
        var options = workerContext.ServiceProvider.GetRequiredService<IOptions<OrderSyncOptions>>().Value;

        // UoW yönetimi TAMAMEN manager'da: her tenant için Change SONRASI taze requiresNew UoW (DbContext o tenant'a
        // bağlanır → kanallar görünür), her order için ayrı requiresNew UoW (bağımsız commit → fresh akış).
        var seed = await manager.SyncEmptyChannelsAsync(workerContext.CancellationToken);
        Report("seed", seed);

        if (!options.DeltaEnabled)
        {
            return;
        }

        // SIRA ÖNEMLİ: önce seed, sonra delta. Ters sırada, yeni kurulmuş bir kanal delta kolunda "hiç siparişi
        // yok" diye atlanır ve seed'i bir tur gecikirdi.
        var delta = await manager.SyncActiveChannelsAsync(workerContext.CancellationToken);
        Report("delta", delta);
    }

    private void Report(string arm, OrderFetchResultDto report)
    {
        if (report.NewOrders == 0 && report.UpdatedOrders == 0 && report.RefreshedOrders == 0)
        {
            return;   // sessiz tur — log gürültüsü üretme
        }

        Logger.LogInformation(
            "Sipariş senkronu ({Arm}): {Channels} kanal, {New} yeni + {Updated} güncellenen + {Refreshed} tazelenen sipariş, {Lines} kalem.",
            arm, report.ChannelsProcessed, report.NewOrders, report.UpdatedOrders, report.RefreshedOrders, report.TotalLines);
    }
}
