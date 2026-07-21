using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// Etsy seller taxonomy'yi periyodik (günlük, config <c>Etsy:Taxonomy:SyncIntervalHours</c> → 24s) TAM-RECONCILE eden arka
/// plan işçisi (ekle/güncelle/HARD-sil). <see cref="AbpAsyncTimer.RunOnStart"/>=true → İLK tur AÇILIŞTA çalışır (ayrı
/// fire-and-forget gerekmez; <see cref="Orders.OrderSyncBackgroundWorker"/> ile aynı proven desen) — host açılışını
/// BLOKLAMAZ (timer thread'inde). Gerçek reconcile yalnız <see cref="EtsyTaxonomySyncManager.SyncIfStaleAsync"/> kapısı
/// bayat/boş derse tetiklenir. Hata YUTULUR + <c>LogWarning</c> (kanal yoksa/Etsy erişilemezse döngü ölmesin). YALNIZ
/// Blazor host'ta kayıtlı → çift-çalışma yok.
/// </summary>
public class EtsyTaxonomySyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public EtsyTaxonomySyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IConfiguration configuration)
        : base(timer, serviceScopeFactory)
    {
        var hours = configuration.GetValue<int?>("Etsy:Taxonomy:SyncIntervalHours") ?? 24;
        if (hours <= 0)
        {
            hours = 24;
        }

        Timer.Period = (int)TimeSpan.FromHours(hours).TotalMilliseconds;
        Timer.RunOnStart = true;   // uygulama ayağa kalkar kalkmaz İLK bayatlık kontrolü (Period beklemeden)
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        try
        {
            var manager = workerContext.ServiceProvider.GetRequiredService<EtsyTaxonomySyncManager>();
            var synced = await manager.SyncIfStaleAsync(manager.ResolveSyncInterval(), workerContext.CancellationToken);
            if (synced)
            {
                Logger.LogInformation("Etsy taxonomy re-sync tamam (bayat/boş → reconcile).");
            }
        }
        catch (Exception ex)
        {
            // Worker döngüsü ölmesin: kanal yoksa / Etsy erişilemezse / ağ hatasında yut + logla (görev kuralı).
            Logger.LogWarning(ex, "Etsy taxonomy sync atlandı (kanal/ağ?).");
        }
    }
}
