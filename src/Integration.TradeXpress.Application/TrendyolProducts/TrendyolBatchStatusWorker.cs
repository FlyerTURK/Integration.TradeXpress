using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL BATCH DURUM İŞÇİSİ (5 dk) — bekleyen gönderimleri çözer. Uygulaması <see cref="TrendyolBatchStatusResolver"/>'da
/// (2026-08-19: işçi canlıda fiilen hiç çalışmıyordu — kimliksiz/şirketsiz bağlamda bekleyen sorgusu boş dönüyordu;
/// gerekçe ve kurulan desen resolver'ın doc'unda). Bu sınıf yalnız tenant'ları dolaşır ve tenant bağlamını kurar.
///
/// <para><b>Neden şart:</b> Trendyol yazma uçları asenkron. Gönderim <c>PROCESSING</c>'te kalır ve sonucu
/// SORULMADIKÇA öğrenilmez. Bu işçi olmadan finalizasyon yalnız kullanıcının elle "durum yenile"sine bağlı
/// kalır; kimse basmazsa <c>LastSent*</c> hiç dolmaz, dirty-check tabanı boş kalır ve çifte-batch koruması
/// o kaydı kalıcı olarak kilitler — ürün bir daha hiç senkronlanmaz. Yani otomasyon burada konfor değil,
/// zincirin çalışması için gereken parça.</para>
///
/// <para><b>YALNIZ Blazor host'ta kayıtlı</b> (OrderSync/Repricing deseni) — iki host'ta birden koşarsa aynı
/// batch iki kez finalize edilmeye çalışılır.</para>
///
/// <para><b>RunOnStart=false BİLİNÇLİ:</b> açılışta tüm bekleyenleri sorgulamak host kalkışını Trendyol'un
/// kotasına bağlar. İlk tur ilk periyotta koşar.</para>
///
/// <para><b>Hata izolasyonu TENANT ve KAYIT başınadır:</b> tek tenant'ın/ürünün arızası turun kalanını durdurmaz.
/// Sessiz değil — loglanır; tur özeti (bekleyen/çözülen/başarısız/bayat) Information seviyesinde yazılır ki
/// "işçi çalışıyor mu" sorusu bir daha log yokluğuyla cevaplanmasın.</para>
/// </summary>
public class TrendyolBatchStatusWorker : AsyncPeriodicBackgroundWorkerBase
{
    public TrendyolBatchStatusWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
        Timer.RunOnStart = false;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var tenantRepository = workerContext.ServiceProvider.GetRequiredService<ITenantRepository>();
        var currentTenant = workerContext.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var uowManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var resolver = workerContext.ServiceProvider.GetRequiredService<TrendyolBatchStatusResolver>();

        List<Guid> tenantIds;
        using (var uow = uowManager.Begin(requiresNew: true))
        {
            tenantIds = (await tenantRepository.GetListAsync()).Select(t => t.Id).ToList();
            await uow.CompleteAsync();
        }

        foreach (var tenantId in tenantIds)
        {
            if (workerContext.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using (currentTenant.Change(tenantId))
                {
                    var report = await resolver.ResolvePendingAsync(workerContext.CancellationToken);
                    if (report.Pending > 0)
                    {
                        Logger.LogInformation(
                            "Trendyol batch durum turu (Tenant={TenantId}): bekleyen {Pending} · çözülen {Resolved} · başarısız {Failed} · bayat {Stale}{Skipped}.",
                            tenantId, report.Pending, report.Resolved, report.Failed, report.MarkedStale,
                            report.SkippedNoAdmin ? " · ADMIN YOK, atlandı" : string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Trendyol batch durum turu tenant için başarısız: {TenantId}", tenantId);
            }
        }
    }
}
