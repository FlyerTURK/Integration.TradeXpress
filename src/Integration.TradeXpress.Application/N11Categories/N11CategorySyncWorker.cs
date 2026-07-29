using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori ağacını ve komisyonlarını periyodik (günlük; config <c>N11:CategorySync:SyncIntervalHours</c>)
/// kendi kendine tazeleyen arka plan işçisi — kullanıcı hiçbir düğmeye basmaz.
///
/// <para><see cref="AbpAsyncTimer.RunOnStart"/>=true → ilk tur uygulama ayağa kalkar kalkmaz çalışır; host
/// açılışını BLOKLAMAZ (timer thread'inde). Gerçek çekim yalnız
/// <see cref="N11CategorySyncManager.SyncIfStaleAsync"/> kapısı "bayat/boş" derse yapılır — taze bir DB ile
/// arka arkaya açılışlarda N11'e hiç istek gitmez.</para>
///
/// <para>Hata YUTULUR + <c>LogWarning</c>: kimlik yoksa ya da N11 erişilemezse döngü ölmemeli. YALNIZ Blazor
/// host'ta kayıtlı → çift çalışma yok (<see cref="EtsyTaxonomies.EtsyTaxonomySyncWorker"/> ikizi).</para>
/// </summary>
public class N11CategorySyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public N11CategorySyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IConfiguration configuration)
        : base(timer, serviceScopeFactory)
    {
        var hours = configuration.GetValue<int?>("N11:CategorySync:SyncIntervalHours") ?? 24;
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
            var manager = workerContext.ServiceProvider.GetRequiredService<N11CategorySyncManager>();
            var synced = await manager.SyncIfStaleAsync(manager.ResolveSyncInterval(), workerContext.CancellationToken);
            if (synced)
            {
                Logger.LogInformation("N11 kategori mutabakatı tamam (bayat/boş → reconcile).");
            }
        }
        catch (Exception ex)
        {
            // Worker döngüsü ölmesin: kimlik yoksa / N11 erişilemezse / ağ hatasında yut + logla.
            Logger.LogWarning(ex, "N11 kategori senkronu atlandı (kimlik/ağ?).");
        }
    }
}
