using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 kanal ürününün SİLME GRAFININ tek sahibi — Trendyol ikizi
/// (<c>TrendyolProducts.SalesChannelTrTrendyolProductRemover</c>). Gerekçe orada.
/// </summary>
/// <remarks>⚠ <see cref="ExposeServicesAttribute"/> ZORUNLU — gerekçe Trendyol ikizinde.</remarks>
[ExposeServices(typeof(IProductChannelListingRemover), typeof(SalesChannelTrN11ProductRemover))]
public class SalesChannelTrN11ProductRemover : IProductChannelListingRemover, ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _stockItemRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttributeValue, Guid> _attributeValueRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public SalesChannelTrN11ProductRemover(
        IRepository<SalesChannelTrN11Product, Guid> repository,
        IRepository<SalesChannelTrN11ProductStockItem, Guid> stockItemRepository,
        IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> recipeLineRepository,
        IRepository<SalesChannelTrN11ProductAttribute, Guid> attributeRepository,
        IRepository<SalesChannelTrN11ProductAttributeValue, Guid> attributeValueRepository,
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

    public virtual async Task RemoveGraphAsync(SalesChannelTrN11Product entity)
    {
        await _recipeLineRepository.DeleteAsync(r => r.SalesChannelTrN11ProductId == entity.Id, autoSave: true);
        await _stockItemRepository.DeleteAsync(v => v.SalesChannelTrN11ProductId == entity.Id, autoSave: true);

        var attributeIds = await _asyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelTrN11ProductId == entity.Id)
                .Select(a => a.Id));
        if (attributeIds.Count > 0)
        {
            await _attributeValueRepository.DeleteAsync(v => attributeIds.Contains(v.AttributeId), autoSave: true);
            await _attributeRepository.DeleteAsync(a => a.SalesChannelTrN11ProductId == entity.Id, autoSave: true);
        }

        await _repository.DeleteAsync(entity, autoSave: true);
    }
}
