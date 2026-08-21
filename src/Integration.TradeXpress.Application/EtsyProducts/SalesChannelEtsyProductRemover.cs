using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy kanal ürününün SİLME GRAFININ tek sahibi — Trendyol ikizi
/// (<c>TrendyolProducts.SalesChannelTrTrendyolProductRemover</c>). Gerekçe orada.
/// </summary>
/// <remarks>⚠ <see cref="ExposeServicesAttribute"/> ZORUNLU — gerekçe Trendyol ikizinde.</remarks>
[ExposeServices(typeof(IProductChannelListingRemover), typeof(SalesChannelEtsyProductRemover))]
public class SalesChannelEtsyProductRemover : IProductChannelListingRemover, ITransientDependency
{
    private readonly IRepository<SalesChannelEtsyProduct, Guid> _repository;
    private readonly IRepository<SalesChannelEtsyProductStockItem, Guid> _stockItemRepository;
    private readonly IRepository<SalesChannelEtsyProductStockItemRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<SalesChannelEtsyProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<SalesChannelEtsyProductAttributeValue, Guid> _attributeValueRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public SalesChannelEtsyProductRemover(
        IRepository<SalesChannelEtsyProduct, Guid> repository,
        IRepository<SalesChannelEtsyProductStockItem, Guid> stockItemRepository,
        IRepository<SalesChannelEtsyProductStockItemRecipeLine, Guid> recipeLineRepository,
        IRepository<SalesChannelEtsyProductAttribute, Guid> attributeRepository,
        IRepository<SalesChannelEtsyProductAttributeValue, Guid> attributeValueRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _repository               = repository;
        _stockItemRepository      = stockItemRepository;
        _recipeLineRepository     = recipeLineRepository;
        _attributeRepository      = attributeRepository;
        _attributeValueRepository = attributeValueRepository;
        _asyncExecuter            = asyncExecuter;
    }

    public virtual async Task RemoveForProductAsync(Guid productId)
    {
        var records = await _asyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(r => r.ProductId == productId));

        foreach (var record in records)
        {
            await RemoveGraphAsync(record);
        }
    }

    /// <summary>Ana ürün pasifleşince Etsy kanal ürünleri pasif — yalnız yerel bayrak (Etsy dondurulmuş; uzak yol yok).</summary>
    public virtual async Task DeactivateForProductAsync(Guid productId)
    {
        var records = await _asyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(r => r.ProductId == productId && r.IsActive));

        foreach (var record in records)
        {
            record.SetActive(false);
            await _repository.UpdateAsync(record, autoSave: true);
        }
    }

    public virtual async Task RemoveGraphAsync(SalesChannelEtsyProduct entity)
    {
        await _recipeLineRepository.DeleteAsync(r => r.SalesChannelEtsyProductId == entity.Id, autoSave: true);
        await _stockItemRepository.DeleteAsync(v => v.SalesChannelEtsyProductId == entity.Id, autoSave: true);

        var attributeIds = await _asyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelEtsyProductId == entity.Id)
                .Select(a => a.Id));
        if (attributeIds.Count > 0)
        {
            await _attributeValueRepository.DeleteAsync(v => attributeIds.Contains(v.AttributeId), autoSave: true);
            await _attributeRepository.DeleteAsync(a => a.SalesChannelEtsyProductId == entity.Id, autoSave: true);
        }

        await _repository.DeleteAsync(entity, autoSave: true);
    }
}
