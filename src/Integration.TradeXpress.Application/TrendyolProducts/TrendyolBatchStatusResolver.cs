using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orchestration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Bir tenant turunun özeti — işçi loglar, test doğrular. <paramref name="SkippedNoAdmin"/> true ise
/// tenant'ta admin yoktu ve HİÇBİR kayıt sorgulanmadı (sessiz geçilmedi, loglandı).</summary>
public sealed record TrendyolBatchResolveReport(int Pending, int Resolved, int Failed, int MarkedStale, bool SkippedNoAdmin);

/// <summary>
/// TRENDYOL BATCH DURUM ÇÖZÜCÜSÜ — <see cref="TrendyolBatchStatusWorker"/>'ın uygulaması (2026-08-19 haritası,
/// öncelik #2). İşçi yalnız tenant'ları dolaşır; "hangi kayıtlar bekliyor, nasıl çözülür" BURADA yaşar ki
/// işçi bağlamı olmadan test edilebilsin.
///
/// <para><b>Neden yeniden yazıldı — işçi canlıda FİİLEN HİÇ ÇALIŞMIYORDU:</b> eski uygulama kullanıcısız ve
/// şirketsiz koşuyordu. Kullanıcısız bağlamda <c>WorkingCompanyContextProvider</c> <c>Guid.Empty</c> sentinel'i
/// döner ("hiç şirket yetkisi yok"), <c>ICompanyScoped</c> filtresi o sentinel'de TÜM şirket satırlarını eler
/// → "bekleyen" sorgusu her turda BOŞ dönüyordu. Tek satır bile geçseydi bu kez <c>RefreshStatusAsync</c>'in
/// <c>[Authorize]</c>'u kimliksiz çağrıyı reddedecekti. Hata yok, log yok; canlı host logunda işçiden sıfır iz.
/// Sonuç: batch finalizasyonu yalnız kullanıcının elle "Durumu Yenile"sine bağlıydı — işçinin tam da
/// önlemek için yazıldığı durum.</para>
///
/// <para><b>Kurulan desen (OrderSyncManager / ProductStockSyncJob ile aynı):</b> ① bekleyenler şirket filtresi
/// KAPALI listelenir (tenant izolasyonu çağıranın <c>CurrentTenant.Change</c>'iyle korunur) ve kaydın KENDİ
/// <c>CompanyId</c>'si alınır; ② tenant admin'i için principal üretilir (<see cref="OrchestrationIdentityScope"/>;
/// <c>[Authorize]</c> GEVŞETİLMEZ — §2); ③ kayıt başına <c>ICurrentCompany.Change(kaydın şirketi)</c> +
/// <c>ICurrentPrincipalAccessor.Change(admin)</c> altında, app service'in KENDİ <c>RefreshStatusAsync</c>'i
/// çağrılır — elle "Durumu Yenile" ile birebir aynı finalizasyon (ikinci bir kopya zamanla ayrışırdı).
/// Change'ler BU frame'de kurulur (AsyncLocal kuralı — scope doc'u).</para>
///
/// <para><b>Bayat batch artık gerçekten serbest kalır:</b> <see cref="StaleBatchAfter"/>'dan uzun PROCESSING'te
/// kalan kayıt <see cref="SalesChannelTrTrendyolProduct.MarkBatchStale"/> ile PROCESSING'ten ÇIKARILIR. Eski
/// yol yalnız hata metni yazıyordu; çifte-batch koruması <c>Status</c>'a baktığı için kilit hiç açılmıyordu.</para>
///
/// <para><b>Hata izolasyonu KAYIT başınadır:</b> tek ürünün arızası turun kalanını durdurmaz; loglanır.</para>
/// </summary>
public class TrendyolBatchStatusResolver : ITransientDependency
{
    /// <summary>PROCESSING'te bu süreden uzun kalan batch artık beklenmez (Trendyol yanıt vermiyor ya da batch
    /// kaybolmuş). Sonsuza kadar bekleyen kayıt SESSİZ bir kilittir — çifte-batch koruması yeni gönderimi de engeller.</summary>
    public static readonly TimeSpan StaleBatchAfter = TimeSpan.FromHours(24);

    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _repository;
    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly IClock _clock;
    private readonly ILogger<TrendyolBatchStatusResolver> _logger;

    public TrendyolBatchStatusResolver(
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        ISalesChannelTrTrendyolProductAppService appService,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        OrchestrationIdentityScope identityScope,
        IClock clock,
        ILogger<TrendyolBatchStatusResolver> logger)
    {
        _repository = repository;
        _appService = appService;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _identityScope = identityScope;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>GEÇERLİ tenant'ın bekleyen Trendyol batch'lerini çözer. Çağıran tenant bağlamını ÖNCE kurar
    /// (<c>CurrentTenant.Change</c>); şirket ve kimlik burada kayıt başına kurulur.</summary>
    public virtual async Task<TrendyolBatchResolveReport> ResolvePendingAsync(CancellationToken cancellationToken = default)
    {
        List<PendingBatch> pending;
        using (_dataFilter.Disable<ICompanyScoped>())
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            pending = await _asyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(p => p.Status == TrendyolProductConsts.ProcessingBatchStatus && p.BatchRequestId != null)
                    .Select(p => new PendingBatch(p.Id, p.CompanyId, p.LastSyncedAt)));
            await uow.CompleteAsync();
        }

        if (pending.Count == 0)
        {
            return new TrendyolBatchResolveReport(0, 0, 0, 0, SkippedNoAdmin: false);
        }

        var principal = await _identityScope.BuildTenantAdminPrincipalAsync();
        if (principal is null)
        {
            _logger.LogWarning(
                "Trendyol batch durum turu atlandı: tenant admin bulunamadı — {Count} bekleyen batch sorgulanamadı.",
                pending.Count);
            return new TrendyolBatchResolveReport(pending.Count, 0, 0, 0, SkippedNoAdmin: true);
        }

        var resolved = 0;
        var failed = 0;
        var stale = 0;

        foreach (var batch in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            using (_currentCompany.Change(batch.CompanyId))
            using (_currentPrincipalAccessor.Change(principal))
            {
                try
                {
                    using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
                    {
                        // Durum sorgusu + finalizasyon TEK yerden: app service'in kendi yolu (elle "Durumu Yenile"
                        // ile birebir aynı davranış). İkinci bir finalizasyon kopyası zamanla ayrışırdı.
                        await _appService.RefreshStatusAsync(batch.Id);
                        await uow.CompleteAsync();
                    }

                    resolved++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(
                        ex, "Trendyol batch durumu çözülemedi (ChannelProduct={ChannelProductId}, Company={CompanyId}).",
                        batch.Id, batch.CompanyId);

                    if (await MarkStaleIfTimedOutAsync(batch))
                    {
                        stale++;
                    }
                }
            }
        }

        return new TrendyolBatchResolveReport(pending.Count, resolved, failed, stale, SkippedNoAdmin: false);
    }

    /// <summary>Süresi dolmuş batch'i bekleme listesinden çıkarır (bkz. <see cref="SalesChannelTrTrendyolProduct.MarkBatchStale"/>).
    /// Yalnız hata yolunda çağrılır: cevap veren batch bayat sayılmaz. true = işaretlendi.</summary>
    private async Task<bool> MarkStaleIfTimedOutAsync(PendingBatch batch)
    {
        var now = _clock.Now.ToUniversalTime();
        if (batch.SubmittedAt is not { } submitted || now - submitted < StaleBatchAfter)
        {
            return false;
        }

        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var entity = await _repository.GetAsync(batch.Id);
            entity.MarkBatchStale(now);
            await _repository.UpdateAsync(entity, autoSave: true);
            await uow.CompleteAsync();

            _logger.LogWarning(
                "Trendyol batch BAYAT işaretlendi: {Hours} saattir PROCESSING'teydi (ChannelProduct={ChannelProductId}, Batch={BatchId}).",
                (int)(now - submitted).TotalHours, batch.Id, entity.BatchRequestId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bayat Trendyol batch'i işaretlenemedi: {ChannelProductId}", batch.Id);
            return false;
        }
    }

    private sealed record PendingBatch(Guid Id, Guid CompanyId, DateTime? SubmittedAt);
}
