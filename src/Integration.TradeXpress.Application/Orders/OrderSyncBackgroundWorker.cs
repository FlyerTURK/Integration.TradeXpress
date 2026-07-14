using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş SEED arka plan işçisi — periyodik olarak TÜM tenant/kanalları tarar; HİÇ siparişi olmayan kanalı
/// pazaryerinden STREAMING (order başına commit) çekip doldurur (<see cref="OrderSyncManager.SyncEmptyChannelsAsync"/>).
/// Kanal başına bağımsız try/catch (biri düşse — kimlik/ağ/throttle — worker çökmez). Dolu kanalı ATLAR (ChannelHasOrders)
/// → maliyet düşük. YALNIZ Blazor host'ta kayıtlı (çift-çalışma yok). Okumalar dış UoW'da; her order kendi UoW'unda commit.
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

        // UoW yönetimi TAMAMEN manager'da: her tenant için Change SONRASI taze requiresNew UoW (DbContext o tenant'a
        // bağlanır → kanallar görünür), her order için ayrı requiresNew UoW (bağımsız commit → fresh akış).
        var report = await manager.SyncEmptyChannelsAsync(workerContext.CancellationToken);

        if (report.NewOrders > 0 || report.UpdatedOrders > 0)
        {
            Logger.LogInformation("Sipariş seed: {Channels} kanal işlendi, {New} yeni + {Updated} güncellenen sipariş, {Lines} kalem.",
                report.ChannelsProcessed, report.NewOrders, report.UpdatedOrders, report.TotalLines);
        }
    }
}
