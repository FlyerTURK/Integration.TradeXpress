using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol kanal ürününün SİLME GRAFININ tek sahibi. Üç çağıranı var ve üçü de aynı grafı silmek zorundadır:
/// kullanıcının silme komutu (<c>SalesChannelTrTrendyolProductAppService.DeleteAsync</c>) · şablon ürün silinirken
/// kanal temizliği (<see cref="IProductChannelListingRemover"/>) · içe aktarımın öksüz kayıt temizliği.
/// Grafı üç yerde tekrar yazmak, birinde bağımlı tablo unutulunca sessiz yetim üretirdi.
/// </summary>
/// <remarks>⚠ <see cref="ExposeServicesAttribute"/> ZORUNLU: ABP'nin varsayılan kuralı yalnız ADI EŞLEŞEN arayüzü
/// (<c>IFoo</c> ↔ <c>Foo</c>) açar; <see cref="IProductChannelListingRemover"/> adı eşleşmediği için otomatik
/// KAYDEDİLMEZ. Bu unutulunca <c>ProductAppService</c>'in enjekte ettiği koleksiyon BOŞ gelir ve ürün silme yolu
/// hatasız/logsuz biçimde hiçbir kanal kaydını temizlemez — düzeltmek istediğimiz hatanın ta kendisi geri döner.
/// (<c>Deleting_the_template_product_also_removes_its_channel_records</c> testi bunu yakaladı.)</remarks>
[ExposeServices(typeof(IProductChannelListingRemover), typeof(SalesChannelTrTrendyolProductRemover))]
public class SalesChannelTrTrendyolProductRemover : IProductChannelListingRemover, ITransientDependency
{
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _repository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _stockItemRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid> _attributeValueRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public SalesChannelTrTrendyolProductRemover(
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        IRepository<SalesChannelTrTrendyolProductStockItem, Guid> stockItemRepository,
        IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> recipeLineRepository,
        IRepository<SalesChannelTrTrendyolProductAttribute, Guid> attributeRepository,
        IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid> attributeValueRepository,
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

    /// <summary>Kanal ürününü bağımlılarıyla (override başlıkları · reçete satırları · özellik/değer grafı)
    /// soft-delete eder. Sıra ÖNEMLİ: değerler özelliklerden, bağımlılar ana kayıttan önce.</summary>
    public virtual async Task RemoveGraphAsync(SalesChannelTrTrendyolProduct entity)
    {
        await _recipeLineRepository.DeleteAsync(r => r.SalesChannelTrTrendyolProductId == entity.Id, autoSave: true);
        await _stockItemRepository.DeleteAsync(v => v.SalesChannelTrTrendyolProductId == entity.Id, autoSave: true);

        var attributeIds = await _asyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelTrTrendyolProductId == entity.Id)
                .Select(a => a.Id));
        if (attributeIds.Count > 0)
        {
            await _attributeValueRepository.DeleteAsync(v => attributeIds.Contains(v.AttributeId), autoSave: true);
            await _attributeRepository.DeleteAsync(a => a.SalesChannelTrTrendyolProductId == entity.Id, autoSave: true);
        }

        await _repository.DeleteAsync(entity, autoSave: true);
    }
}
