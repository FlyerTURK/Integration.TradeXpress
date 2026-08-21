using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// N11 GÜNLÜK MUTABAKAT ÇÖZÜCÜSÜ (2026-08-21) — kanalın FİİLÎ durumunu okuyup <c>LastSent*</c> tabanını
/// gözlemle düzeltir. <see cref="ChannelReconciliationWorker"/>'ın N11 kolu; işçi yalnız tenant'ları dolaşır,
/// "hangi kayıt, nasıl düzeltilir" BURADA yaşar ki işçi bağlamı olmadan test edilebilsin.
///
/// <para><b>Neden gerekliydi — kapanan delik:</b> tüm oversell/fiyat savunmaları BİZİM gönderdiğimizi bilir
/// (dirty-check <c>LastSent</c>'e bakar). Satıcı panelinden elle değişiklik ya da kaçan bir task kanalda farklı
/// fiyat/adet bırakır; dirty-check "değişiklik yok" der ve sapma SONSUZA DEK kalırdı — hiçbir mekanizma kanalın
/// fiilî durumunu okumuyordu. Okuma ucu zaten vardı (<c>GET /ms/product-query</c>, import'un kullandığı yol;
/// yanıt SKU başına <c>quantity</c> + <c>salePrice</c> döndürür) — eksik olan bu turdu.</para>
///
/// <para><b>Sapma bulununca (AKTİF kayıt):</b> taban kanalın bildirdiği değere çekilir
/// (<see cref="SalesChannelTrN11Product.ReconcileObservedSkuState"/>) + Warning log. Böylece normal senkron
/// turu (15 dk dirty-check) bizim doğruyu KENDİLİĞİNDEN geri yazar — otorite devri: kanalda elle yapılan
/// değişiklik geçersizdir, değerleri sistem belirler. PushHistory'ye YAZILMAZ (biz bir şey göndermedik).</para>
///
/// <para><b>PASİF kayıt:</b> beklenen kanal durumu adet-0'dır (adet-0'ı pasifleşme yolu zaten gönderdi —
/// <c>N11StockWithdrawer</c>). Kanal hâlâ satılabilir adet gösteriyorsa yalnız Warning: taban 0'a ÇEKİLMEZ ki
/// otomatik push tetiklenmesin; kullanıcı isterse aktifleyip kapatır ya da elle karar verir.</para>
///
/// <para><b>Bekleyen task'lı kayıt ATLANIR</b> (<c>PendingPushTaskId</c>): gözlem, kuyruktaki gönderimin
/// ÖNCESİNİ gösterebilir — taban yazılsaydı <see cref="N11PendingPushResolver"/>'ın çözümüyle yarışılırdı.</para>
///
/// <para><b>Kimlik/şirket deseni</b> (TrendyolBatchStatusResolver ile birebir — CLAUDE.md §6): kayıtlar şirket
/// filtresi KAPALI listelenir (tenant izolasyonu çağıranın <c>CurrentTenant.Change</c>'iyle korunur); tenant
/// admin principal'ı üretilir (<see cref="OrchestrationIdentityScope"/>); kayıt başına
/// <c>ICurrentCompany.Change(kaydın şirketi)</c> + <c>ICurrentPrincipalAccessor.Change(admin)</c> ÇAĞIRANIN
/// frame'inde kurulur. Hata izolasyonu kayıt başınadır; listelemesi okunamayan kanalın kayıtları o turda
/// taranmaz (sayaçta görünür, sessiz geçilmez).</para>
/// </summary>
public class N11ReconciliationResolver : ITransientDependency
{
    /// <summary>Listeleme sayfa boyutu — istemci dokümanın 250 tavanına zaten kırpar; tavanı açıkça istiyoruz
    /// ki mağaza en az istekle okunsun (import ile aynı gerekçe).</summary>
    private const int QueryPageSize = 250;

    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IN11ProductQueryClient _queryClient;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly ILogger<N11ReconciliationResolver> _logger;

    public N11ReconciliationResolver(
        IRepository<SalesChannelTrN11Product, Guid> repository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IN11ProductQueryClient queryClient,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        OrchestrationIdentityScope identityScope,
        ILogger<N11ReconciliationResolver> logger)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _queryClient = queryClient;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _identityScope = identityScope;
        _logger = logger;
    }

    /// <summary>GEÇERLİ tenant'ın N11 kanal-ürünlerini kanalın fiilî listeleme durumuyla mutabık kılar.
    /// Çağıran tenant bağlamını ÖNCE kurar (<c>CurrentTenant.Change</c>); şirket ve kimlik burada kayıt
    /// başına kurulur.</summary>
    public virtual async Task<ChannelReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        List<RecordSnapshot> records;
        List<ChannelCredentials> channels;
        using (_dataFilter.Disable<ICompanyScoped>())
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            records = await _asyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Select(p => new RecordSnapshot(
                        p.Id, p.CompanyId, p.SalesChannelId, p.IsActive, p.PendingPushTaskId != null)));

            var channelIds = records.Select(r => r.SalesChannelId).Distinct().ToList();
            channels = await _asyncExecuter.ToListAsync(
                (await _channelRepository.GetQueryableAsync())
                    .Where(c => channelIds.Contains(c.Id))
                    .Select(c => new ChannelCredentials(c.Id, c.AppKey, c.AppSecret)));

            await uow.CompleteAsync();
        }

        if (records.Count == 0)
        {
            return ChannelReconciliationReport.Empty;
        }

        var principal = await _identityScope.BuildTenantAdminPrincipalAsync();
        if (principal is null)
        {
            _logger.LogWarning(
                "N11 mutabakat turu atlandı: tenant admin bulunamadı — {Count} kayıt taranamadı.",
                records.Count);
            return ChannelReconciliationReport.NoAdmin;
        }

        var scanned = 0;
        var skippedPending = 0;
        var driftedRecords = 0;
        var correctedSkus = 0;
        var passiveDrifts = 0;
        var missingSkus = 0;
        var failedRecords = 0;
        var failedChannels = 0;

        foreach (var channelGroup in records.GroupBy(r => r.SalesChannelId))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Bekleyen task'lı kayıtlar kanal okuma maliyeti ödenmeden ayıklanır; geriye kalan "vadesi gelen" liste boşsa kanal hiç okunmaz.
            var dueRecords = channelGroup.ToList();
            skippedPending += dueRecords.RemoveAll(r => r.HasPendingPush);
            if (dueRecords.Count == 0)
            {
                continue;
            }

            var credentials = channels.FirstOrDefault(c => c.Id == channelGroup.Key);
            if (credentials is null)
            {
                // Kanal kaydı bulunamadı (silinmiş?) — kayıtları sessizce "sapmasız" saymak yerine kanal arızası.
                failedChannels++;
                _logger.LogWarning(
                    "N11 mutabakatı: kanal {ChannelId} bulunamadı — {Count} kayıt taranamadı.",
                    channelGroup.Key, dueRecords.Count);
                continue;
            }

            // Kanalın fiilî durumu TEK okumada (SALT GET; kayıt başına istek atılmaz). Satırlar SKU
            // başınadır (REST'te her stok kodu bağımsız satır); sayfalar arası mükerrer düşebilir → ilk kazanır.
            Dictionary<string, N11RestProductSummary> remote;
            try
            {
                var rows = await _queryClient.QueryAllAsync(
                    new N11ProductQueryFilter(
                        Page: 0, Size: QueryPageSize, StockCode: null, SaleStatus: null,
                        ProductStatus: null, BrandName: null, CategoryIds: null),
                    credentials.AppKey, credentials.AppSecret, cancellationToken);

                remote = new Dictionary<string, N11RestProductSummary>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows.Where(r => r.StockCode.Length > 0))
                {
                    remote.TryAdd(row.StockCode, row);
                }
            }
            catch (Exception ex)
            {
                failedChannels++;
                _logger.LogWarning(
                    ex, "N11 mutabakatı: kanal {ChannelId} listelemesi okunamadı — {Count} kayıt bu turda taranamadı.",
                    channelGroup.Key, dueRecords.Count);
                continue;
            }

            foreach (var record in dueRecords)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                using (_currentCompany.Change(record.CompanyId))
                using (_currentPrincipalAccessor.Change(principal))
                {
                    try
                    {
                        var result = await ReconcileRecordAsync(record, remote);
                        if (result.SkippedPending)
                        {
                            skippedPending++;
                            continue;
                        }

                        scanned++;
                        correctedSkus += result.CorrectedSkus;
                        passiveDrifts += result.PassiveDrifts;
                        missingSkus += result.MissingSkus;
                        if (result.CorrectedSkus > 0)
                        {
                            driftedRecords++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedRecords++;
                        _logger.LogWarning(
                            ex, "N11 mutabakatı kayıt için başarısız (ChannelProduct={ChannelProductId}, Company={CompanyId}).",
                            record.Id, record.CompanyId);
                    }
                }
            }
        }

        return new ChannelReconciliationReport(
            scanned, skippedPending, driftedRecords, correctedSkus, passiveDrifts, missingSkus,
            failedRecords, failedChannels, SkippedNoAdmin: false);
    }

    /// <summary>Tek kaydın SKU'larını kanal gözlemiyle karşılaştırır; aktifte tabanı düzeltir, pasifte ve
    /// eksikte yalnız loglar. Çağıran şirket+kimlik bağlamını kurmuş olmalıdır.</summary>
    private async Task<RecordResult> ReconcileRecordAsync(
        RecordSnapshot record, Dictionary<string, N11RestProductSummary> remote)
    {
        var corrected = 0;
        var passive = 0;
        var missing = 0;

        using var uow = _unitOfWorkManager.Begin(requiresNew: true);
        var entity = await _repository.GetAsync(record.Id);

        // TOCTOU sertleştirmesi (2026-08-21 hakem bulgusu): pending bayrağı snapshot ANINDA okunmuştu; kanal
        // GET'i büyük mağazada dakikalar sürebilir ve bu pencerede 15 dk'lık senkron yeni bir push submit
        // edebilir. TAZE entity üzerinde yeniden kontrol — uçuştaki gönderimin altına taban yazılmaz.
        if (entity.PendingPushTaskId != null)
        {
            return new RecordResult(CorrectedSkus: 0, PassiveDrifts: 0, MissingSkus: 0, SkippedPending: true);
        }

        foreach (var sku in entity.Skus)
        {
            var found = remote.TryGetValue(sku.SellerStockCode, out var row);

            if (!entity.IsActive)
            {
                // PASİF: beklenen kanal durumu adet-0. Sapma varsa Warning yeter — taban 0'a çekilmez ki
                // otomatik push tetiklenmesin (adet-0'ı zaten pasifleşme yolu gönderdi).
                if (found && row!.Quantity is > 0)
                {
                    passive++;
                    _logger.LogWarning(
                        "N11 mutabakatı: PASİF kayıtta kanal hâlâ satılabilir adet gösteriyor " +
                        "(ChannelProduct={ChannelProductId}, SKU={StockCode}, KanalAdet={Quantity}) — taban değiştirilmedi, karar kullanıcıya ait.",
                        record.Id, sku.SellerStockCode, row.Quantity);
                }

                continue;
            }

            if (!found)
            {
                // Tabanı dolu SKU kanalda hiç yok: değer sapması değil, listelemenin kendisi kayıp — otomatik
                // yeniden-oluşturma tetiklenmez (taban null'a çekilseydi bir sonraki tur push denerdi).
                if (sku.LastSentQuantity is not null || sku.LastSentOptionPrice is not null)
                {
                    missing++;
                    _logger.LogWarning(
                        "N11 mutabakatı: SKU kanal listelemesinde YOK ama taban dolu " +
                        "(ChannelProduct={ChannelProductId}, SKU={StockCode}) — taban değiştirilmedi.",
                        record.Id, sku.SellerStockCode);
                }

                continue;
            }

            var drift = entity.ReconcileObservedSkuState(sku.SellerStockCode, row!.Quantity, row.SalePrice);
            if (drift is not null)
            {
                corrected++;
                _logger.LogWarning(
                    "N11 mutabakatı SAPMA düzeltti (ChannelProduct={ChannelProductId}, SKU={StockCode}): " +
                    "adet {LocalQuantity}→{ObservedQuantity}, fiyat {LocalPrice}→{ObservedPrice}. " +
                    "Normal senkron turu sistem değerini kanala geri yazacak.",
                    record.Id, drift.SellerStockCode,
                    drift.LocalQuantity, drift.ObservedQuantity, drift.LocalPrice, drift.ObservedPrice);
            }
        }

        if (corrected > 0)
        {
            await _repository.UpdateAsync(entity, autoSave: true);
        }

        await uow.CompleteAsync();
        return new RecordResult(corrected, passive, missing);
    }

    private sealed record RecordSnapshot(Guid Id, Guid CompanyId, Guid SalesChannelId, bool IsActive, bool HasPendingPush);

    private sealed record ChannelCredentials(Guid Id, string AppKey, string AppSecret);

    private sealed record RecordResult(int CorrectedSkus, int PassiveDrifts, int MissingSkus, bool SkippedPending = false);
}
