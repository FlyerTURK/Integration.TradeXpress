using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL BATCH DURUM İŞÇİSİ (5 dk) — bekleyen gönderimleri çözer.
///
/// <para><b>Neden şart:</b> Trendyol yazma uçları asenkron. Gönderim <c>PROCESSING</c>'te kalır ve sonucu
/// SORULMADIKÇA öğrenilmez. Bu işçi olmadan finalizasyon yalnız kullanıcının elle "durum yenile"sine bağlı
/// kalırdı; kimse basmazsa <c>LastSent*</c> hiç dolmaz, dirty-check tabanı boş kalır ve çifte-batch koruması
/// o kaydı kalıcı olarak kilitler — ürün bir daha hiç senkronlanmaz. Yani otomasyon burada konfor değil,
/// zincirin çalışması için gereken parça.</para>
///
/// <para><b>YALNIZ Blazor host'ta kayıtlı</b> (OrderSync/Repricing deseni) — iki host'ta birden koşarsa aynı
/// batch iki kez finalize edilmeye çalışılır.</para>
///
/// <para><b>RunOnStart=false BİLİNÇLİ:</b> açılışta tüm bekleyenleri sorgulamak host kalkışını Trendyol'un
/// kotasına bağlar. İlk tur ilk periyotta koşar.</para>
///
/// <para><b>Hata izolasyonu KAYIT başınadır:</b> tek bir ürünün arızası (bozuk batch id, kanal kimliği
/// değişmiş) turun geri kalanını durdurmaz. Sessiz değil — loglanır.</para>
/// </summary>
public class TrendyolBatchStatusWorker : AsyncPeriodicBackgroundWorkerBase
{
    /// <summary>PROCESSING'te bu süreden uzun kalan batch artık beklenmez. Trendyol yanıt vermiyor ya da batch
    /// kaybolmuş demektir; sonsuza kadar bekleyen kayıt SESSİZ bir kilittir (çifte-batch guard'ı yeni gönderimi
    /// de engeller). Süre dolunca kayıt hata olarak işaretlenir ve bekleyenler atılır → bir sonraki senkron
    /// baştan gönderir.</summary>
    private static readonly TimeSpan StaleBatchAfter = TimeSpan.FromHours(24);

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
                    await ResolveTenantAsync(workerContext, tenantId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Trendyol batch durum turu tenant için başarısız: {TenantId}", tenantId);
            }
        }
    }

    private async Task ResolveTenantAsync(PeriodicBackgroundWorkerContext workerContext, Guid tenantId)
    {
        var repository = workerContext.ServiceProvider
            .GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        var appService = workerContext.ServiceProvider.GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        var uowManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var asyncExecuter = workerContext.ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>();

        List<(Guid Id, DateTime? SubmittedAt)> pending;
        using (var uow = uowManager.Begin(requiresNew: true))
        {
            pending = await asyncExecuter.ToListAsync(
                (await repository.GetQueryableAsync())
                    .Where(p => p.Status == "PROCESSING" && p.BatchRequestId != null)
                    .Select(p => new ValueTuple<Guid, DateTime?>(p.Id, p.LastSyncedAt)));
            await uow.CompleteAsync();
        }

        foreach (var (id, submittedAt) in pending)
        {
            if (workerContext.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using (var uow = uowManager.Begin(requiresNew: true))
                {
                    // Durum sorgusu + finalizasyon TEK yerden: app service'in kendi yolu (elle "durum yenile"
                    // ile birebir aynı davranış). İkinci bir finalizasyon kopyası zamanla ayrışırdı.
                    await appService.RefreshStatusAsync(id);
                    await uow.CompleteAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex, "Trendyol batch durumu çözülemedi (Tenant={TenantId}, ChannelProduct={ChannelProductId})",
                    tenantId, id);

                await MarkStaleIfTimedOutAsync(uowManager, repository, id, submittedAt);
            }
        }
    }

    /// <summary>24 saatten uzun süredir PROCESSING'te bekleyen kaydı hata olarak işaretler ve bekleyen
    /// gönderim değerlerini atar. Aksi hâlde kayıt sessizce kilitli kalırdı: çifte-batch guard'ı yeni
    /// senkronu reddeder, durum da hiç çözülmez.</summary>
    private async Task MarkStaleIfTimedOutAsync(
        IUnitOfWorkManager uowManager,
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        Guid id,
        DateTime? submittedAt)
    {
        if (submittedAt is not { } submitted || DateTime.UtcNow - submitted < StaleBatchAfter)
        {
            return;
        }

        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var entity = await repository.GetAsync(id);
            entity.ClearPendingSkuPushes();
            entity.MarkSyncFailed("BatchStale", DateTime.UtcNow);
            await repository.UpdateAsync(entity, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Bayat Trendyol batch'i işaretlenemedi: {ChannelProductId}", id);
        }
    }
}
