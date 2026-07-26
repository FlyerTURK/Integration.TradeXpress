using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// 15-DK REPRICING İŞÇİSİ (ADR Dilim 2) — tüm tenant'ların tüm şirketleri için
/// <see cref="RepricingCycleElapsedEto"/> yayımlar; ağır işi YAPMAZ (müdür + ürün-başına job'lar yapar).
/// <para>OrderSyncBackgroundWorker deseni: YALNIZ Blazor host'ta kayıtlı (çift-çalışma yok); tenant başına
/// <c>CurrentTenant.Change</c> SONRASI taze UoW. Push maliyeti N11 dirty-check ile sınırlı — fiyatı/stoğu
/// SAPMAMIŞ ürün için N11'e hiç yazılmaz; döngünün gerçek maliyeti maliyet-yeniden-hesabıdır (15 dk'da bir).</para>
/// <para><c>RunOnStart=false</c> BİLİNÇLİ: açılışta tüm ürünleri yeniden fiyatlamak host kalkışını dış servis
/// kotasına bağlar; ilk tur ilk periyotta koşar.</para>
/// </summary>
public class RepricingCycleWorker : AsyncPeriodicBackgroundWorkerBase
{
    public RepricingCycleWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;   // Hakan'ın "15 dakikada bir" kararı
        Timer.RunOnStart = false;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var tenantRepository = workerContext.ServiceProvider.GetRequiredService<ITenantRepository>();
        var companyRepository = workerContext.ServiceProvider.GetRequiredService<IRepository<Company, Guid>>();
        var eventBus = workerContext.ServiceProvider.GetRequiredService<IDistributedEventBus>();
        var currentTenant = workerContext.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var uowManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var asyncExecuter = workerContext.ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>();

        // Tenant listesi host kapsamında okunur (OrderSyncManager deseni); host'un şirketi yok → dahil edilmez.
        System.Collections.Generic.List<Guid> tenantIds;
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
                using (var uow = uowManager.Begin(requiresNew: true))
                {
                    var companyIds = await asyncExecuter.ToListAsync(
                        (await companyRepository.GetQueryableAsync()).Select(c => c.Id));

                    foreach (var companyId in companyIds)
                    {
                        await eventBus.PublishAsync(new RepricingCycleElapsedEto
                        {
                            TenantId  = tenantId,
                            CompanyId = companyId,
                        });
                    }

                    await uow.CompleteAsync();
                }
            }
            catch (Exception ex)
            {
                // Tenant-başına izolasyon: birinin arızası (bozuk veri/bağlantı) diğer tenant'ların
                // fiyat tazelemesini durdurmaz (OrderSync worker deseni). Sessiz değil — loglanır.
                Logger.LogWarning(ex, "Repricing turu tenant için başarısız: {TenantId}", tenantId);
            }
        }
    }
}
