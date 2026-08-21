using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orchestration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.N11Products;

/// <summary>Bir tenant turunun özeti — işçi loglar, test doğrular. <paramref name="StillQueued"/> = sorgulandı ama
/// N11 hâlâ işliyor (durum değişmedi); <paramref name="Rejected"/> = N11 reddetti, kayıt işaretlendi
/// (<c>LastError</c> + kimlik temizlendi); <paramref name="Failed"/> = sorgu yapılamadı (ağ/kimlik).</summary>
public sealed record N11PendingPushResolveReport(
    int Pending, int Resolved, int StillQueued, int Rejected, int Failed, bool SkippedNoAdmin);

/// <summary>
/// N11 KUYRUK ÇÖZÜCÜSÜ — kuyruğa alınmış push task'larının akıbetini sorgular (2026-08-19 haritası, öncelik #2).
///
/// <para><b>Neden gerekliydi:</b> REST push'u "kuyruğa alındı" diye dönebilir (<c>MarkPushQueued</c>); sonucu
/// ancak task sorgulanınca belli olur. Sorgulayan tek yol <c>ResolvePendingPushAsync</c>'ti ve onu çağıran düğme
/// yalnız ÖLÜ (monte edilmemiş) bir panelde yaşıyordu → kuyruğa düşen push sonsuza dek "bekliyor" kalıyor,
/// reddedildiyse red gerekçesi hiç okunmuyor, 15 dk'lık repricing turu da üstüne yeni task yazıyordu.
/// Trendyol'un batch işçisinin N11 karşılığı budur — ve Trendyol'unkiyle AYNI kimlik/şirket desenini kullanır.</para>
///
/// <para><b>Desen (TrendyolBatchStatusResolver ile birebir):</b> bekleyenler şirket filtresi KAPALI listelenir
/// (tenant izolasyonu çağıranın <c>CurrentTenant.Change</c>'iyle korunur); tenant admin'i için principal üretilir
/// (<see cref="OrchestrationIdentityScope"/>; <c>[Authorize]</c> gevşetilmez); kayıt başına
/// <c>ICurrentCompany.Change</c> + <c>ICurrentPrincipalAccessor.Change</c> altında app service'in KENDİ
/// <c>ResolvePendingPushAsync</c>'i çağrılır — elle çözme ve <c>EnsureNoPendingPushAsync</c> ile aynı sonuç (tek kopya).</para>
///
/// <para><b>Red bir İŞÇİ hatası değildir:</b> <c>ResolvePendingPushAsync</c> reddi <c>BusinessException</c> ile
/// bildirir ve kaydı zaten işaretlemiştir (kimlik temizlenir, <c>LastError</c> dolar) — burada Information
/// seviyesinde sayılır, Warning'e düşmez. Gerçek arıza (ağ, kimlik, beklenmeyen) Warning'dir.</para>
///
/// <para><b>PASİF kayıtlar KAPSAM İÇİNDEDİR (bilinçli — daraltma):</b> bekleyen sorgusu <c>IsActive</c> süzmez.
/// Pasif kayıttaki tek olası task, pasifleştirme anının ADET-0 gönderimidir (<see cref="N11StockWithdrawer"/>,
/// 2026-08-21) ve çözümü <c>LastSent</c>'i 0'a çeker — çözülmeden kalsaydı yeniden aktifleşme kuyruk kapısına
/// takılır, adet-0'ın akıbeti de hiç öğrenilemezdi.</para>
/// </summary>
public class N11PendingPushResolver : ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly ILogger<N11PendingPushResolver> _logger;

    public N11PendingPushResolver(
        IRepository<SalesChannelTrN11Product, Guid> repository,
        ISalesChannelTrN11ProductAppService appService,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        OrchestrationIdentityScope identityScope,
        ILogger<N11PendingPushResolver> logger)
    {
        _repository = repository;
        _appService = appService;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _identityScope = identityScope;
        _logger = logger;
    }

    /// <summary>GEÇERLİ tenant'ın kuyruktaki N11 push'larını çözer. Çağıran tenant bağlamını ÖNCE kurar
    /// (<c>CurrentTenant.Change</c>); şirket ve kimlik burada kayıt başına kurulur.</summary>
    public virtual async Task<N11PendingPushResolveReport> ResolvePendingAsync(CancellationToken cancellationToken = default)
    {
        List<PendingPush> pending;
        using (_dataFilter.Disable<ICompanyScoped>())
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            pending = await _asyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(p => p.PendingPushTaskId != null)
                    .Select(p => new PendingPush(p.Id, p.CompanyId)));
            await uow.CompleteAsync();
        }

        if (pending.Count == 0)
        {
            return new N11PendingPushResolveReport(0, 0, 0, 0, 0, SkippedNoAdmin: false);
        }

        var principal = await _identityScope.BuildTenantAdminPrincipalAsync();
        if (principal is null)
        {
            _logger.LogWarning(
                "N11 kuyruk çözme turu atlandı: tenant admin bulunamadı — {Count} bekleyen task sorgulanamadı.",
                pending.Count);
            return new N11PendingPushResolveReport(pending.Count, 0, 0, 0, 0, SkippedNoAdmin: true);
        }

        var resolved = 0;
        var stillQueued = 0;
        var rejected = 0;
        var failed = 0;

        foreach (var push in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            using (_currentCompany.Change(push.CompanyId))
            using (_currentPrincipalAccessor.Change(principal))
            {
                try
                {
                    SalesChannelTrN11ProductDto dto;
                    using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
                    {
                        dto = await _appService.ResolvePendingPushAsync(push.Id);
                        await uow.CompleteAsync();
                    }

                    // Hâlâ kuyruktaysa kimlik durur (app service böyle bildirir); çözüldüyse temizlenmiştir.
                    if (dto.PendingPushTaskId is not null)
                    {
                        stillQueued++;
                    }
                    else
                    {
                        resolved++;
                    }
                }
                catch (BusinessException ex)
                {
                    // N11 reddetti: kayıt app service'te zaten işaretlendi (kimlik temizlendi, LastError dolu).
                    rejected++;
                    _logger.LogInformation(
                        "N11 kuyruktaki push REDDEDİLDİ, kayıt işaretlendi (ChannelProduct={ChannelProductId}, Company={CompanyId}): {Code}",
                        push.Id, push.CompanyId, ex.Code ?? ex.Message);
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(
                        ex, "N11 kuyruktaki push sorgulanamadı (ChannelProduct={ChannelProductId}, Company={CompanyId}).",
                        push.Id, push.CompanyId);
                }
            }
        }

        return new N11PendingPushResolveReport(pending.Count, resolved, stillQueued, rejected, failed, SkippedNoAdmin: false);
    }

    private sealed record PendingPush(Guid Id, Guid CompanyId);
}
