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

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// GÜNLÜK MUTABAKAT İŞÇİSİ (24 saat) — kanalların FİİLÎ listeleme durumunu okuyup <c>LastSent*</c> tabanını
/// gözlemle düzeltir (2026-08-21; best-practice karnesinin 1 numaralı eksiği). Uygulama
/// <see cref="N11ReconciliationResolver"/> + <see cref="TrendyolReconciliationResolver"/>'da; bu sınıf yalnız
/// tenant'ları dolaşır ve tenant bağlamını kurar (<c>N11PendingPushResolverWorker</c> ile birebir aynı desen).
///
/// <para><b>Neden şart:</b> tüm oversell/fiyat savunmaları BİZİM gönderdiğimizi bilir. Satıcı panelinden elle
/// değişiklik ya da kaçan bir batch kanalda farklı fiyat/adet bırakır; dirty-check <c>LastSent</c>'e baktığı
/// için "değişiklik yok" der ve sapma SONSUZA DEK kalır. Bu işçi kanalın fiilî durumunu okur, sapan tabanı
/// gözleme çeker — normal senkron turu (15 dk) doğruyu kendiliğinden geri yazar. Etsy'nin salt-okuma
/// listelemesi bu turda YOK (push/senkron zinciri de yok — sapmayı düzeltebilecek geri yazım yolu kurulmadan
/// taban oynatmak anlamsız).</para>
///
/// <para><b>YALNIZ Blazor host'ta kayıtlı</b> — iki host'ta birden koşarsa aynı kanal iki kez okunur ve tabana
/// iki el birden yazar. <b>RunOnStart=true:</b> 24 saatlik periyot host'un kesintisiz bir gün ayakta kalmasını
/// varsayamaz (geliştirmede host sık yeniden başlar; RunOnStart=false olsaydı tur fiilen hiç koşmazdı —
/// TrendyolBatchStatusWorker'ın "hiç çalışmayan işçi" dersi). Bedeli açılış başına kanal başına TEK salt-GET
/// listelemedir.</para>
///
/// <para>Tur özeti kanal başına Information seviyesinde yazılır (taranan · sapan · düzeltilen SKU · pasif
/// sapma · eksik SKU · arıza).</para>
/// </summary>
public class ChannelReconciliationWorker : AsyncPeriodicBackgroundWorkerBase
{
    /// <summary>Tur sıklığı — GÜNLÜK. Mutabakat bir emniyet ağıdır, senkron yolu değildir: dakikalık sapmaları
    /// zaten 15 dk'lık repricing turu kapatır; buranın işi panel/batch kaynaklı KALICI sapmayı bir günden uzun
    /// yaşatmamaktır. Daha sık koşmak kanal başına tam listeleme okuması yüzünden kota harcar.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromHours(24);

    public ChannelReconciliationWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)Period.TotalMilliseconds;
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var tenantRepository = workerContext.ServiceProvider.GetRequiredService<ITenantRepository>();
        var currentTenant = workerContext.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var uowManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var n11Resolver = workerContext.ServiceProvider.GetRequiredService<N11ReconciliationResolver>();
        var trendyolResolver = workerContext.ServiceProvider.GetRequiredService<TrendyolReconciliationResolver>();

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
                    var n11 = await n11Resolver.ReconcileAsync(workerContext.CancellationToken);
                    LogReport("N11", tenantId, n11);

                    var trendyol = await trendyolResolver.ReconcileAsync(workerContext.CancellationToken);
                    LogReport("Trendyol", tenantId, trendyol);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Mutabakat turu tenant için başarısız: {TenantId}", tenantId);
            }
        }
    }

    private void LogReport(string channelKind, Guid tenantId, ChannelReconciliationReport report)
    {
        if (!report.HasActivity)
        {
            return;   // kanal-ürünü olmayan tenant için satır basma — günlük tur log'u gürültüye dönmesin
        }

        Logger.LogInformation(
            "Mutabakat turu ({Channel}, Tenant={TenantId}): taranan {Scanned} · sapan kayıt {Drifted} · düzeltilen SKU {Corrected} · " +
            "pasif sapma {Passive} · eksik SKU {Missing} · bekleyen push nedeniyle atlanan {SkippedPending} · " +
            "kayıt arızası {FailedRecords} · kanal arızası {FailedChannels}{Skipped}.",
            channelKind, tenantId, report.Scanned, report.DriftedRecords, report.CorrectedSkus,
            report.PassiveDrifts, report.MissingSkus, report.SkippedPending,
            report.FailedRecords, report.FailedChannels,
            report.SkippedNoAdmin ? " · ADMIN YOK, atlandı" : string.Empty);
    }
}
