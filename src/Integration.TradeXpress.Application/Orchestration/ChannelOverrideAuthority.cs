using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// KANAL OVERRIDE OTORİTESİ — pazaryeri satırındaki <c>OverridePrice</c>/<c>OverrideStock</c> gölgelerini
/// yöneten TEK yer (2026-08-06'da <c>N11ChannelStockPusher</c>'dan çıkarıldı; Trendyol ve Etsy'de bu ağ
/// hiç YOKTU — aynı delik iki kanalda açık duruyordu).
///
/// <para><b>Push zinciri override'ı ÖNCELER</b> (<c>OverrideStock ?? StockQuantity</c>,
/// <c>OverridePrice ?? türetilen</c>). Bu yüzden orada kalan bir kalıntı, hesaplanan güncel değeri SÜREKLİ
/// gölgeler ve kimse fark etmez — aşırı satışın en sessiz biçimi.</para>
///
/// <para><b>İki farklı iş, bilerek AYRI metot:</b>
/// <list type="bullet">
///   <item><see cref="ClearShadowedStockAsync"/> — HER push öncesi; yalnız STOK gölgesi, yalnız
///   <c>Calculated</c> üründe. Fiyat override'ı kullanıcının meşru kanal-özel kararı olabilir, dokunulmaz.</item>
///   <item><see cref="TransferAuthorityAsync"/> — sınıflandırma anında BİR KEZ; stok VE fiyat.
///   2026-08-05 Hakan kararı: <i>"sistemimize bağlandıktan sonraki stok ve fiyatı sistem belirler"</i> —
///   içe aktarımın yazdığı pazaryeri yansıması o andan itibaren geçersizdir.</item>
/// </list></para>
///
/// <para><b>ProductVariantId null olan satıra DOKUNULMAZ</b> (üç kanalda da): o satır ERP'ye bağlı değildir,
/// override orada zorunlu TEK kaynaktır — temizlemek listeyi kaynaksız bırakırdı.</para>
/// </summary>
public class ChannelOverrideAuthority : ITransientDependency
{
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _n11StockItemRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _trendyolProductRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _trendyolStockItemRepository;
    private readonly IRepository<SalesChannelEtsyProduct, Guid> _etsyProductRepository;
    private readonly IRepository<SalesChannelEtsyProductStockItem, Guid> _etsyStockItemRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ILogger<ChannelOverrideAuthority> _logger;

    public ChannelOverrideAuthority(
        IRepository<Product, Guid> productRepository,
        IRepository<SalesChannelTrN11Product, Guid> n11ProductRepository,
        IRepository<SalesChannelTrN11ProductStockItem, Guid> n11StockItemRepository,
        IRepository<SalesChannelTrTrendyolProduct, Guid> trendyolProductRepository,
        IRepository<SalesChannelTrTrendyolProductStockItem, Guid> trendyolStockItemRepository,
        IRepository<SalesChannelEtsyProduct, Guid> etsyProductRepository,
        IRepository<SalesChannelEtsyProductStockItem, Guid> etsyStockItemRepository,
        IAsyncQueryableExecuter asyncExecuter,
        ILogger<ChannelOverrideAuthority> logger)
    {
        _productRepository            = productRepository;
        _n11ProductRepository         = n11ProductRepository;
        _n11StockItemRepository       = n11StockItemRepository;
        _trendyolProductRepository    = trendyolProductRepository;
        _trendyolStockItemRepository  = trendyolStockItemRepository;
        _etsyProductRepository        = etsyProductRepository;
        _etsyStockItemRepository      = etsyStockItemRepository;
        _asyncExecuter                = asyncExecuter;
        _logger                       = logger;
    }

    /// <summary>Push ÖNCESİ stok gölgesi temizliği (2026-07-25 inceleme bulgusu #23): eski "Uygula" akışı
    /// muadil paket sayısını <c>OverrideStock</c>'a yazmıştı; <c>Calculated</c> üründe bu kalıntı hesaplanan
    /// güncel stoğu sürekli gölgeler. Fiyat override'ına DOKUNULMAZ.</summary>
    public virtual async Task<int> ClearShadowedStockAsync(Guid productId)
    {
        var product = await _productRepository.FindAsync(productId);
        if (product is null || product.StockPolicy != ProductStockPolicy.Calculated)
        {
            return 0;
        }

        var cleared = await ApplyAsync(productId, clearPrice: false);
        if (cleared > 0)
        {
            _logger.LogInformation(
                "Calculated ürün push öncesi {Count} kanal satırının OverrideStock gölgesi temizlendi (Product={ProductId}).",
                cleared, productId);
        }

        return cleared;
    }

    /// <summary>OTORİTE DEVRİ (2026-08-05 Hakan kararı): ürün sisteme bağlandı — pazaryerinde duran stok VE
    /// fiyat geçersizdir, ikisini de sistem belirler. Birleştirme/koruma mantığı YOK.
    /// <para>Politika kontrolü YOK (<see cref="ClearShadowedStockAsync"/>'ten farkı): bu metot ürün
    /// <c>Calculated</c>'a çevrildikten SONRA çağrılır ve devir, politikadan bağımsız bir kullanıcı kararıdır.</para></summary>
    public virtual async Task<int> TransferAuthorityAsync(Guid productId)
    {
        var cleared = await ApplyAsync(productId, clearPrice: true);
        if (cleared > 0)
        {
            _logger.LogInformation(
                "Otorite devri: {Count} kanal satırının pazaryeri stok/fiyat aynası temizlendi (Product={ProductId}).",
                cleared, productId);
        }

        return cleared;
    }

    /// <summary>Üç kanalın satırlarını gezer. Ortak arayüz YOK — üç entity'nin FK adı farklı ve ortak bir
    /// soyutlama uydurmak (yalnız bu iş için) üç tabloyu yapay olarak birbirine bağlardı.</summary>
    private async Task<int> ApplyAsync(Guid productId, bool clearPrice)
    {
        var cleared = 0;

        var n11Ids = await _asyncExecuter.ToListAsync(
            (await _n11ProductRepository.GetQueryableAsync())
                .Where(p => p.ProductId == productId).Select(p => p.Id));
        if (n11Ids.Count > 0)
        {
            var items = await _asyncExecuter.ToListAsync(
                (await _n11StockItemRepository.GetQueryableAsync())
                    .Where(i => n11Ids.Contains(i.SalesChannelTrN11ProductId)
                                && i.ProductVariantId != null
                                && (i.OverrideStock != null || (clearPrice && i.OverridePrice != null))));
            foreach (var item in items)
            {
                item.SetOverrideStock(null);
                if (clearPrice)
                {
                    item.SetOverridePrice(null, null);
                }

                await _n11StockItemRepository.UpdateAsync(item, autoSave: true);
                cleared++;
            }
        }

        var trendyolIds = await _asyncExecuter.ToListAsync(
            (await _trendyolProductRepository.GetQueryableAsync())
                .Where(p => p.ProductId == productId).Select(p => p.Id));
        if (trendyolIds.Count > 0)
        {
            var items = await _asyncExecuter.ToListAsync(
                (await _trendyolStockItemRepository.GetQueryableAsync())
                    .Where(i => trendyolIds.Contains(i.SalesChannelTrTrendyolProductId)
                                && i.ProductVariantId != null
                                && (i.OverrideStock != null || (clearPrice && i.OverridePrice != null))));
            foreach (var item in items)
            {
                item.SetOverrideStock(null);
                if (clearPrice)
                {
                    item.SetOverridePrice(null, null);
                }

                await _trendyolStockItemRepository.UpdateAsync(item, autoSave: true);
                cleared++;
            }
        }

        var etsyIds = await _asyncExecuter.ToListAsync(
            (await _etsyProductRepository.GetQueryableAsync())
                .Where(p => p.ProductId == productId).Select(p => p.Id));
        if (etsyIds.Count > 0)
        {
            var items = await _asyncExecuter.ToListAsync(
                (await _etsyStockItemRepository.GetQueryableAsync())
                    .Where(i => etsyIds.Contains(i.SalesChannelEtsyProductId)
                                && i.ProductVariantId != null
                                && (i.OverrideStock != null || (clearPrice && i.OverridePrice != null))));
            foreach (var item in items)
            {
                item.SetOverrideStock(null);
                if (clearPrice)
                {
                    item.SetOverridePrice(null, null);
                }

                await _etsyStockItemRepository.UpdateAsync(item, autoSave: true);
                cleared++;
            }
        }

        return cleared;
    }
}
