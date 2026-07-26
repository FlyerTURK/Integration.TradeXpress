using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.MultiCompany;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// ÜRÜN ORKESTRASYON MÜDÜRÜ (ADR-PRODUCT-ORCHESTRATION; ad 2026-07-25 Hakan kararı). Önündeki durum
/// tahtası EVENT'lerle güncellenir; işi ürün-başına, asenkron, paralel, birbirini KİLİTLEMEYEN job'lara dağıtır.
/// <para><b>Sinyaller:</b> bugün <see cref="MetalStockChangedEto"/> (VoucherLine — Dilim 1); ileride
/// RepricingCycleElapsedEto (15-dk fiyat döngüsü — Dilim 2) ve CompetitorSnapshotEto (rakip — Dilim 3)
/// aynı tahtaya düşer: her sinyal için yeni bir <c>IDistributedEventHandler</c> kolu açılır.</para>
/// <para><b>Kilitlenmezlik kuyruktan değil İŞ TASARIMINDAN gelir:</b> job ürün-başına dar, idempotent
/// (aynı event iki kez → aynı sonuç; N11 dirty-check fazla push'u eler). Taşıma ABP soyutlamaları —
/// production'da RabbitMQ paketiyle sıfır kod değişikliği (ADR kuyruk kararı).</para>
/// </summary>
public class ProductOrchestrationManager
    : IDistributedEventHandler<MetalStockChangedEto>,
      IDistributedEventHandler<RepricingCycleElapsedEto>,
      ITransientDependency
{
    private readonly RecipeMetalReverseIndex _reverseIndex;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentCompany _currentCompany;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<ProductOrchestrationManager> _logger;

    public ProductOrchestrationManager(
        RecipeMetalReverseIndex reverseIndex,
        IRepository<SalesChannelTrN11Product, Guid> n11ProductRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IBackgroundJobManager backgroundJobManager,
        ICurrentTenant currentTenant,
        ICurrentCompany currentCompany,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<ProductOrchestrationManager> logger)
    {
        _reverseIndex = reverseIndex;
        _n11ProductRepository = n11ProductRepository;
        _asyncExecuter = asyncExecuter;
        _backgroundJobManager = backgroundJobManager;
        _currentTenant = currentTenant;
        _currentCompany = currentCompany;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public virtual async Task HandleEventAsync(MetalStockChangedEto eventData)
    {
        // Ters-endeks şirket/tenant filtreli tablolara sorar → bağlam ELLE kurulur (event commit-sonrası,
        // ambient bağlam taşımaz; GetStockAsync/ICompanyOwned sözleşmesi).
        using (_currentTenant.Change(eventData.TenantId))
        using (_currentCompany.Change(eventData.CompanyId))
        // TAZE UoW ZORUNLU: bu handler voucher UoW'unun OnCompleted'ında koşar — o UoW'un DbContext'i
        // DISPOSE edilmiştir; ambient'e katılmak ObjectDisposedException verir (2026-07-25 test bulgusu).
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var affected = await _reverseIndex.FindAffectedProductsAsync(
                eventData.Keys.ConvertAll(k => new MetalStockKey(k.MetalId, k.MetalVariantId)));

            if (affected.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Maden stok değişimi {MetalCount} anahtar → {ProductCount} ürün etkilendi; ürün-başına job kuyruklanıyor.",
                eventData.Keys.Count, affected.Count);

            // Ürün-başına AYRI job: paralel işlenir, biri diğerini kilitlemez; job içi hata yalnız o ürünü etkiler.
            foreach (var product in affected)
            {
                await _backgroundJobManager.EnqueueAsync(new ProductStockSyncJobArgs
                {
                    TenantId  = eventData.TenantId,
                    CompanyId = eventData.CompanyId,
                    ProductId = product.ProductId,
                    Reason    = ProductSyncReason.StockChanged,
                });
            }

            await uow.CompleteAsync();
        }
    }

    /// <summary>15-DK FİYAT DÖNGÜSÜ (Dilim 2): kanal listelemesi OLAN her ürüne push job'ı — türetilmiş fiyat
    /// (NetCost×Marj, canlı kur) push anında yeniden hesaplanır; N11 dirty-check SAPMAMIŞ olanı elemez, yalnız
    /// değişen gider. Stok politikasından BAĞIMSIZ: fiyat tazeleme Fixed/Unlimited ürünü de kapsar
    /// (job stok hesabını yalnız Calculated'da yapar, push'u herkese).</summary>
    public virtual async Task HandleEventAsync(RepricingCycleElapsedEto eventData)
    {
        using (_currentTenant.Change(eventData.TenantId))
        using (_currentCompany.Change(eventData.CompanyId))
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var productIds = await _asyncExecuter.ToListAsync(
                (await _n11ProductRepository.GetQueryableAsync())
                    .Select(p => p.ProductId)
                    .Distinct());

            foreach (var productId in productIds)
            {
                await _backgroundJobManager.EnqueueAsync(new ProductStockSyncJobArgs
                {
                    TenantId  = eventData.TenantId,
                    CompanyId = eventData.CompanyId,
                    ProductId = productId,
                    Reason    = ProductSyncReason.Repricing,   // stok hesabı atlanır — yalnız fiyat push'u (bulgu #11)
                });
            }

            await uow.CompleteAsync();
        }
    }
}
