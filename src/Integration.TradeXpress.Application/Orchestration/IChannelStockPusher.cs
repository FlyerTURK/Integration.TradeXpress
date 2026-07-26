using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Ürünün kanal listelemelerine STOK+FİYAT senkronu gönderme soyutlaması (ADR: mock-first — testler sahte
/// implementasyonla koşar, N11'e TEK istek gitmez).
/// </summary>
public interface IChannelStockPusher
{
    /// <summary>Ürünün tüm kanal-ürünlerini hafif senkronla tazeler. Push HATASI fırlatılmaz — loglanır:
    /// bir kanalın geçici arızası diğer kanalları ve job'ın kendisini düşürmemeli (retry sonraki tetikte).</summary>
    Task PushProductAsync(Guid productId);
}

/// <summary>
/// GERÇEK pusher — N11 hafif stok+fiyat yolu (<c>SyncStockAndPriceAsync</c>: dirty-tracking'li, değişmemişse
/// N11'e HİÇ yazmaz → fiili debounce). Trendyol'un hafif yolu YOK (yalnız tam submit) — Dilim 2'de
/// updatePriceAndInventory client'ı gelince buraya eklenir (ADR "Dilimler").
/// <para><b>UoW sözleşmesi (2026-07-25 inceleme bulgusu #9):</b> DB okuma/yazma KENDİ kısa UoW'unda biter;
/// N11 HTTP çağrıları UoW DIŞINDA koşar — 60sn'lik dış istek açık DB transaction'ı rehin almaz.</para>
/// </summary>
public class N11ChannelStockPusher : IChannelStockPusher, ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _n11StockItemRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly ISalesChannelTrN11ProductAppService _n11ProductAppService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ILogger<N11ChannelStockPusher> _logger;

    public N11ChannelStockPusher(
        IRepository<SalesChannelTrN11Product, Guid> n11ProductRepository,
        IRepository<SalesChannelTrN11ProductStockItem, Guid> n11StockItemRepository,
        IRepository<Product, Guid> productRepository,
        ISalesChannelTrN11ProductAppService n11ProductAppService,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        ILogger<N11ChannelStockPusher> logger)
    {
        _n11ProductRepository = n11ProductRepository;
        _n11StockItemRepository = n11StockItemRepository;
        _productRepository = productRepository;
        _n11ProductAppService = n11ProductAppService;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _logger = logger;
    }

    public virtual async Task PushProductAsync(Guid productId)
    {
        List<Guid> channelProductIds;

        // ── DB adımı: kendi kısa UoW'u (bulgu #9 — job'ın çağrı yerinde ambient UoW yok; repository çağrısı
        //    UoW'suz patlar ya da push süresince açık kalan transaction'a yapışırdı).
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            channelProductIds = await _asyncExecuter.ToListAsync(
                (await _n11ProductRepository.GetQueryableAsync())
                    .Where(p => p.ProductId == productId)
                    .Select(p => p.Id));

            if (channelProductIds.Count > 0)
            {
                await ClearCalculatedOverrideStockAsync(productId, channelProductIds);
            }

            await uow.CompleteAsync();
        }

        // ── HTTP adımı: UoW DIŞI — N11 senkronu kendi app-service UoW'unu açar; hata fırlatılmaz.
        foreach (var channelProductId in channelProductIds)
        {
            try
            {
                await _n11ProductAppService.SyncStockAndPriceAsync(channelProductId);
            }
            catch (Exception ex)
            {
                // Kanal arızası job'ı DÜŞÜRMEZ: stok DB'de zaten güncel; push sonraki tetikte/elle tekrarlanır.
                // Sessiz yutma değil — ürün+kanal kimliğiyle loglanır.
                _logger.LogWarning(ex,
                    "Kanal stok push başarısız: Product={ProductId}, N11ChannelProduct={ChannelProductId}. "
                    + "Stok DB'de güncel; push sonraki tetikte tekrarlanır.", productId, channelProductId);
            }
        }
    }

    /// <summary>OverrideStock GÖLGELEMESİ (2026-07-25 inceleme bulgusu #23): push zinciri
    /// <c>OverrideStock ?? StockQuantity</c> okur — eski "Uygula" akışı muadil paket sayısını OverrideStock'a
    /// yazmıştı; Calculated üründe bu kalıntı, hesaplanan güncel stoğu SÜREKLİ gölgeler (oversell kapısı).
    /// Calculated üründe ERP-bağlı satırların (ProductVariantId dolu) OverrideStock'u temizlenir → kanal
    /// hesaplanan <c>StockQuantity</c>'yi görür. N11-only satıra (ProductVariantId null) DOKUNULMAZ —
    /// orada OverrideStock zorunlu tek kaynaktır (ERP fallback yok).</summary>
    private async Task ClearCalculatedOverrideStockAsync(Guid productId, List<Guid> channelProductIds)
    {
        var product = await _productRepository.FindAsync(productId);
        if (product is null || product.StockPolicy != ProductStockPolicy.Calculated)
        {
            return;
        }

        var shadowedItems = await _asyncExecuter.ToListAsync(
            (await _n11StockItemRepository.GetQueryableAsync())
                .Where(i => channelProductIds.Contains(i.SalesChannelTrN11ProductId)
                            && i.ProductVariantId != null
                            && i.OverrideStock != null));

        foreach (var item in shadowedItems)
        {
            item.SetOverrideStock(null);
            await _n11StockItemRepository.UpdateAsync(item, autoSave: true);
        }

        if (shadowedItems.Count > 0)
        {
            _logger.LogInformation(
                "Calculated ürün push öncesi {Count} kanal satırının OverrideStock gölgesi temizlendi (Product={ProductId}).",
                shadowedItems.Count, productId);
        }
    }
}
