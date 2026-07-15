using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// N11 sipariş kalemlerini yerel <see cref="EntityVariant"/>'a (Product varyantı) OTOMATİK eşleştirir (Sipariş Fazı O1, task #57) —
/// <c>OrderLine.StockCode</c> (=N11 <c>productSellerCode</c>) ile aynı kanaldaki <see cref="SalesChannelTrN11ProductSku.SellerStockCode"/>
/// eşleşirse <see cref="OrderLineOperationalData"/>'ya İNSERT-ONLY-IF-MISSING yazılır — zaten eşleşmiş/manuel
/// düzeltilmiş satırlara ASLA dokunmaz (resync'te tekrar tekrar çağrılsa bile idempotent).
///
/// <para><b>Belirsizlik = eşleştirme YOK:</b> birden fazla aday (aynı SellerStockCode farklı varyantlarda) varsa
/// eşleştirilmez — yanlış görsel/ürün gösterme riski alınmaz, manuel eşleştirmeye bırakılır.</para>
///
/// <para><b>Trendyol henüz kapsam dışı</b> — farklı Sku deseni (SellerStockCode yerine doğrudan StockItem.ProductVariantId),
/// ayrı bir eşleştirme stratejisi gerektirir (Trendyol siparişi olmadığından ŞİMDİLİK ertelendi).</para>
/// </summary>
public class OrderLineProductMatcher : DomainService
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    // Sku.ProductVariantId artık JENERİK EntityVariant.Id taşır (agnostik varyant geçişi) — eşleştirme agnostik tabloya çözülür.
    private readonly IRepository<EntityVariant, Guid> _productVariantRepository;
    private readonly IRepository<OrderLineOperationalData, Guid> _operationalLineRepository;
    private readonly OrderLineProductSnapshotBuilder _snapshotBuilder;

    public OrderLineProductMatcher(
        IRepository<SalesChannelTrN11Product, Guid> n11ProductRepository,
        IRepository<EntityVariant, Guid> productVariantRepository,
        IRepository<OrderLineOperationalData, Guid> operationalLineRepository,
        OrderLineProductSnapshotBuilder snapshotBuilder)
    {
        _n11ProductRepository = n11ProductRepository;
        _productVariantRepository = productVariantRepository;
        _operationalLineRepository = operationalLineRepository;
        _snapshotBuilder = snapshotBuilder;
    }

    public async Task MatchLinesAsync(
        Guid companyId,
        Guid salesChannelId,
        SalesChannelType channelType,
        Guid orderId,
        IReadOnlyList<RemoteOrderLine> lines,
        CancellationToken cancellationToken = default)
    {
        if (channelType != SalesChannelType.TrN11)
        {
            return;
        }

        var candidateLines = lines
            .Where(l => !string.IsNullOrEmpty(l.RemoteLineId) && !string.IsNullOrEmpty(l.StockCode))
            .ToList();
        if (candidateLines.Count == 0)
        {
            return;
        }

        var remoteLineIds = candidateLines.Select(l => l.RemoteLineId!).ToList();
        var alreadyMatched = (await AsyncExecuter.ToListAsync(
                (await _operationalLineRepository.GetQueryableAsync())
                    .Where(x => x.OrderId == orderId && remoteLineIds.Contains(x.RemoteLineId))
                    .Select(x => x.RemoteLineId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmatched = candidateLines.Where(l => !alreadyMatched.Contains(l.RemoteLineId!)).ToList();
        if (unmatched.Count == 0)
        {
            return;
        }

        var channelProducts = await AsyncExecuter.ToListAsync(
            (await _n11ProductRepository.GetQueryableAsync()).Where(p => p.SalesChannelId == salesChannelId));
        var skusByStockCode = channelProducts
            .SelectMany(p => p.Skus)
            .GroupBy(s => s.SellerStockCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(s => s.ProductVariantId).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);

        var now = Clock.Now.ToUniversalTime();
        foreach (var line in unmatched)
        {
            if (!skusByStockCode.TryGetValue(line.StockCode!, out var candidateVariantIds) || candidateVariantIds.Count != 1)
            {
                continue;   // yok ya da belirsiz (birden fazla aday)
            }

            // ProductVariantId N11-only satırlarda GERÇEK EntityVariant OLMAYABİLİR (StockItem id olabilir) —
            // bulunamazsa sessizce atla (eşleşme yok sayılır).
            var variant = await _productVariantRepository.FindAsync(candidateVariantIds[0]);
            if (variant is null)
            {
                continue;
            }

            var (name, imageUrl) = await _snapshotBuilder.BuildAsync(variant);
            var operational = new OrderLineOperationalData(companyId, orderId, line.RemoteLineId!);
            operational.SetProductMatch(variant.Id, name, imageUrl, now);
            await _operationalLineRepository.InsertAsync(operational, autoSave: true);
        }
    }
}
