using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// TRENDYOL GÜNLÜK MUTABAKAT ÇÖZÜCÜSÜ (2026-08-21) — kanalın FİİLÎ durumunu okuyup <c>LastSent*</c> tabanını
/// gözlemle düzeltir. <see cref="ChannelReconciliationWorker"/>'ın Trendyol kolu; gerekçe ve desen
/// <see cref="N11ReconciliationResolver"/> ile birebir (satıcı panelinden elle değişiklik / kaçan batch →
/// dirty-check "değişiklik yok" der, sapma sonsuza dek kalırdı). Okuma ucu import'un kullandığı salt-GET
/// listelemedir (<c>GetAllSellerProductsAsync</c>; kalem başına <c>quantity</c> + <c>listPrice</c> +
/// <c>salePrice</c> döndürür — 2026-08-16'da canlı kanıtlı).
///
/// <para><b>Sapma bulununca (AKTİF kayıt):</b> taban kanalın bildirdiği değere çekilir
/// (<see cref="SalesChannelTrTrendyolProduct.ReconcileObservedSkuState"/>) + Warning log; normal senkron turu
/// sistem değerini kanala kendiliğinden geri yazar (otorite devri). PushHistory'ye YAZILMAZ.</para>
///
/// <para><b>PASİF kayıt = kanalda ARŞİV</b> (2026-08-17 kararı): listeleme varsayılanı arşivliyi zaten
/// döndürmez; pasif kaydın SKU'su yine de görünüyor ve satılabilir adet taşıyorsa yalnız Warning — taban
/// değiştirilmez ki otomatik push tetiklenmesin.</para>
///
/// <para><b>PROCESSING batch'li kayıt ATLANIR:</b> gözlem, sonuçlanmamış batch'in ÖNCESİNİ gösterebilir —
/// taban yazılsaydı <see cref="TrendyolProducts.TrendyolBatchStatusResolver"/>'ın terfisiyle yarışılırdı.</para>
/// </summary>
public class TrendyolReconciliationResolver : ITransientDependency
{
    /// <summary>Listeleme sayfa boyutu — import'un kullandığı varsayılanla aynı (istemci totalPages döngüsünü
    /// kendisi yürütür).</summary>
    private const int QueryPageSize = 200;

    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _repository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly ITrendyolProductClient _productClient;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly ILogger<TrendyolReconciliationResolver> _logger;

    public TrendyolReconciliationResolver(
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        ITrendyolProductClient productClient,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        OrchestrationIdentityScope identityScope,
        ILogger<TrendyolReconciliationResolver> logger)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _productClient = productClient;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _identityScope = identityScope;
        _logger = logger;
    }

    /// <summary>GEÇERLİ tenant'ın Trendyol kanal-ürünlerini kanalın fiilî listeleme durumuyla mutabık kılar.
    /// Çağıran tenant bağlamını ÖNCE kurar (<c>CurrentTenant.Change</c>); şirket ve kimlik burada kayıt
    /// başına kurulur.</summary>
    public virtual async Task<ChannelReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        List<RecordSnapshot> records;
        List<ChannelCredentialRow> channels;
        using (_dataFilter.Disable<ICompanyScoped>())
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            records = await _asyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Select(p => new RecordSnapshot(
                        p.Id, p.CompanyId, p.SalesChannelId, p.IsActive,
                        p.Status == TrendyolProductConsts.ProcessingBatchStatus)));

            var channelIds = records.Select(r => r.SalesChannelId).Distinct().ToList();
            channels = await _asyncExecuter.ToListAsync(
                (await _channelRepository.GetQueryableAsync())
                    .Where(c => channelIds.Contains(c.Id))
                    .Select(c => new ChannelCredentialRow(c.Id, c.SellerId, c.ApiKey, c.ApiSecret)));

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
                "Trendyol mutabakat turu atlandı: tenant admin bulunamadı — {Count} kayıt taranamadı.",
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

            // PROCESSING batch'li kayıtlar kanal okuma maliyeti ödenmeden ayıklanır; kalan liste boşsa kanal hiç okunmaz.
            var dueRecords = channelGroup.ToList();
            skippedPending += dueRecords.RemoveAll(r => r.HasPendingBatch);
            if (dueRecords.Count == 0)
            {
                continue;
            }

            var channel = channels.FirstOrDefault(c => c.Id == channelGroup.Key);
            if (channel is null)
            {
                // Kanal kaydı bulunamadı (silinmiş?) — kayıtları sessizce "sapmasız" saymak yerine kanal arızası.
                failedChannels++;
                _logger.LogWarning(
                    "Trendyol mutabakatı: kanal {ChannelId} bulunamadı — {Count} kayıt taranamadı.",
                    channelGroup.Key, dueRecords.Count);
                continue;
            }

            // Kanalın fiilî durumu TEK okumada (SALT GET). Gruplu yanıt kalemlere düzlenir; barcode kanal
            // genelinde kimliktir — mükerrer düşerse ilk kazanır.
            Dictionary<string, TrendyolRemoteVariant> remote;
            try
            {
                var credentials = new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
                var products = await _productClient.GetAllSellerProductsAsync(credentials, QueryPageSize, cancellationToken);

                remote = new Dictionary<string, TrendyolRemoteVariant>(StringComparer.OrdinalIgnoreCase);
                foreach (var variant in products.SelectMany(p => p.Variants).Where(v => v.Barcode.Length > 0))
                {
                    remote.TryAdd(variant.Barcode, variant);
                }
            }
            catch (Exception ex)
            {
                failedChannels++;
                _logger.LogWarning(
                    ex, "Trendyol mutabakatı: kanal {ChannelId} listelemesi okunamadı — {Count} kayıt bu turda taranamadı.",
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
                            ex, "Trendyol mutabakatı kayıt için başarısız (ChannelProduct={ChannelProductId}, Company={CompanyId}).",
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
        RecordSnapshot record, Dictionary<string, TrendyolRemoteVariant> remote)
    {
        var corrected = 0;
        var passive = 0;
        var missing = 0;

        using var uow = _unitOfWorkManager.Begin(requiresNew: true);
        var entity = await _repository.GetAsync(record.Id);

        // TOCTOU sertleştirmesi (2026-08-21 hakem bulgusu — N11 ikizi ile aynı): batch bayrağı snapshot
        // anında okunmuştu; kanal GET'i sürerken yeni bir batch açılmış olabilir. Taze entity üzerinde
        // yeniden kontrol — PROCESSING batch'in altına taban yazılmaz.
        if (entity.Status == TrendyolProductConsts.ProcessingBatchStatus)
        {
            return new RecordResult(CorrectedSkus: 0, PassiveDrifts: 0, MissingSkus: 0, SkippedPending: true);
        }

        foreach (var sku in entity.Skus)
        {
            var found = remote.TryGetValue(sku.Barcode, out var variant);

            if (!entity.IsActive)
            {
                // PASİF = kanalda arşiv beklenir (listede hiç görünmemeli). Görünüyor ve satılabilir adet
                // taşıyorsa yalnız Warning — taban değiştirilmez ki otomatik push tetiklenmesin.
                if (found && variant!.Quantity > 0)
                {
                    passive++;
                    _logger.LogWarning(
                        "Trendyol mutabakatı: PASİF kayıtta kanal hâlâ satılabilir adet gösteriyor " +
                        "(ChannelProduct={ChannelProductId}, Barcode={Barcode}, KanalAdet={Quantity}) — taban değiştirilmedi, karar kullanıcıya ait.",
                        record.Id, sku.Barcode, variant.Quantity);
                }

                continue;
            }

            if (!found)
            {
                // Tabanı dolu SKU kanalda hiç yok: değer sapması değil, listelemenin kendisi kayıp — otomatik
                // yeniden-oluşturma tetiklenmez (taban null'a çekilseydi bir sonraki tur push denerdi).
                if (sku.LastSentQuantity is not null || sku.LastSentListPrice is not null || sku.LastSentSalePrice is not null)
                {
                    missing++;
                    _logger.LogWarning(
                        "Trendyol mutabakatı: SKU kanal listelemesinde YOK ama taban dolu " +
                        "(ChannelProduct={ChannelProductId}, Barcode={Barcode}) — taban değiştirilmedi.",
                        record.Id, sku.Barcode);
                }

                continue;
            }

            var drift = entity.ReconcileObservedSkuState(
                sku.Barcode, variant!.Quantity, variant.ListPrice, variant.SalePrice);
            if (drift is not null)
            {
                corrected++;
                _logger.LogWarning(
                    "Trendyol mutabakatı SAPMA düzeltti (ChannelProduct={ChannelProductId}, Barcode={Barcode}): " +
                    "adet {LocalQuantity}→{ObservedQuantity}, liste {LocalListPrice}→{ObservedListPrice}, " +
                    "satış {LocalSalePrice}→{ObservedSalePrice}. Normal senkron turu sistem değerini kanala geri yazacak.",
                    record.Id, drift.Barcode,
                    drift.LocalQuantity, drift.ObservedQuantity,
                    drift.LocalListPrice, drift.ObservedListPrice,
                    drift.LocalSalePrice, drift.ObservedSalePrice);
            }
        }

        if (corrected > 0)
        {
            await _repository.UpdateAsync(entity, autoSave: true);
        }

        await uow.CompleteAsync();
        return new RecordResult(corrected, passive, missing);
    }

    private sealed record RecordSnapshot(Guid Id, Guid CompanyId, Guid SalesChannelId, bool IsActive, bool HasPendingBatch);

    private sealed record ChannelCredentialRow(Guid Id, string SellerId, string ApiKey, string ApiSecret);

    private sealed record RecordResult(int CorrectedSkus, int PassiveDrifts, int MissingSkus, bool SkippedPending = false);
}
