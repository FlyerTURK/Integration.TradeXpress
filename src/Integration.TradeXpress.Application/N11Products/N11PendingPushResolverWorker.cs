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

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 KUYRUK ÇÖZÜCÜ İŞÇİSİ (5 dk) — kuyruğa alınmış push task'larının akıbetini sorgular. Uygulaması
/// <see cref="N11PendingPushResolver"/>'da; bu sınıf yalnız tenant'ları dolaşır ve tenant bağlamını kurar
/// (<c>TrendyolBatchStatusWorker</c> ile birebir aynı desen).
///
/// <para><b>Neden şart:</b> REST push'u kuyruğa düşebilir ve sonucu sorulmadıkça öğrenilmez. Bu işçi olmadan
/// "kuyruğa alındı" diyen kayıt sonsuza dek bekliyor görünür; reddedildiyse gerekçe hiç okunmaz; kuyruk kontrolü
/// (<c>EnsureNoPendingPushAsync</c>) da o kaydın yeni push'unu haklı olarak durdurduğu için ürün kilitli kalır.
/// Kontrol ile işçi birlikte anlamlıdır: kontrol üst üste yazmayı önler, işçi kilidi açar.</para>
///
/// <para><b>YALNIZ Blazor host'ta kayıtlı</b> — iki host'ta birden koşarsa aynı task iki kez finalize edilmeye
/// çalışılır. <b>RunOnStart=false:</b> açılışta N11 kotasını harcamamak için ilk tur ilk periyotta.</para>
///
/// <para>Tur özeti (bekleyen/çözülen/kuyrukta/reddedilen/başarısız) Information seviyesinde yazılır.</para>
/// </summary>
public class N11PendingPushResolverWorker : AsyncPeriodicBackgroundWorkerBase
{
    public N11PendingPushResolverWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
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
        var resolver = workerContext.ServiceProvider.GetRequiredService<N11PendingPushResolver>();

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
                            "N11 kuyruk çözme turu (Tenant={TenantId}): bekleyen {Pending} · çözülen {Resolved} · hâlâ kuyrukta {Queued} · reddedilen {Rejected} · başarısız {Failed}{Skipped}.",
                            tenantId, report.Pending, report.Resolved, report.StillQueued, report.Rejected, report.Failed,
                            report.SkippedNoAdmin ? " · ADMIN YOK, atlandı" : string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "N11 kuyruk çözme turu tenant için başarısız: {TenantId}", tenantId);
            }
        }
    }
}
