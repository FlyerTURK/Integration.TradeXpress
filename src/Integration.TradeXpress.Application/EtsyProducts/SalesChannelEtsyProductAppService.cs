using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.SalesChannels.Variants;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy ürün listeleme CRUD (PUSH-hariç dilim) — <b>company-owned + per-tenant</b>. Listeleme yapılandırması
/// (taksonomi/attribute/kargo profili/işleme süresi/kişiselleştirme/özel bilgi) bizde tutulur. Varyant yönetimi N11/
/// Trendyol final deseniyle BİREBİR: kanal-özel özellik/değer grafı (klon-sonra-ayrış) → kartezyen kombinasyon
/// (StockItem) reconcile (CombinationSignature anahtarlı) → override/reçete satırları. Etsy kanalında kategori-komisyon
/// tablosu YOKTUR → yan-maliyet planı yalnız kanal gider ayarlarından kurulur (Trendyol ile hizalı). Push/Sync/Preview
/// metotları push dilimi geldiğinde eklenecek (bu dosyada YOK).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public partial class SalesChannelEtsyProductAppService : TradeXpressAppService, ISalesChannelEtsyProductAppService
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<SalesChannelEtsyProduct, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<EntityAttribute, Guid> _attributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _attributeValueRepository;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _variantAttributeRepository;
    private readonly IRepository<SalesChannelEtsy, Guid> _channelRepository;
    private readonly IRepository<SalesChannelEtsyProductStockItem, Guid> _stockItemRepository;
    private readonly IRepository<SalesChannelEtsyProductStockItemRecipeLine, Guid> _channelRecipeLineRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _erpRecipeLineRepository;
    private readonly IRepository<SalesChannelEtsyProductAttribute, Guid> _channelAttributeRepository;
    private readonly IRepository<SalesChannelEtsyProductAttributeValue, Guid> _channelAttributeValueRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly EntityVariantManager _variantManager;
    private readonly IEtsyProductClient _etsyProductClient;
    private readonly RecipeCostPopulator _recipeCostPopulator;
    private readonly ICurrentCompany _currentCompany;
    private readonly MarketplaceImageDownloader _imageDownloader;
    private readonly IEtsyTaxonomyAppService _taxonomyAppService;

    public SalesChannelEtsyProductAppService(
        IRepository<SalesChannelEtsyProduct, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> variantDetailRepository,
        IRepository<EntityAttribute, Guid> attributeRepository,
        IRepository<EntityAttributeValue, Guid> attributeValueRepository,
        IRepository<EntityVariantAttributeValue, Guid> variantAttributeRepository,
        IRepository<SalesChannelEtsy, Guid> channelRepository,
        IRepository<SalesChannelEtsyProductStockItem, Guid> stockItemRepository,
        IRepository<SalesChannelEtsyProductStockItemRecipeLine, Guid> channelRecipeLineRepository,
        IRepository<ProductVariantRecipeLine, Guid> erpRecipeLineRepository,
        IRepository<SalesChannelEtsyProductAttribute, Guid> channelAttributeRepository,
        IRepository<SalesChannelEtsyProductAttributeValue, Guid> channelAttributeValueRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        EntityVariantManager variantManager,
        IEtsyProductClient etsyProductClient,
        RecipeCostPopulator recipeCostPopulator,
        ICurrentCompany currentCompany,
        MarketplaceImageDownloader imageDownloader,
        IEtsyTaxonomyAppService taxonomyAppService)
    {
        _repository = repository;
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _variantDetailRepository = variantDetailRepository;
        _attributeRepository = attributeRepository;
        _attributeValueRepository = attributeValueRepository;
        _variantAttributeRepository = variantAttributeRepository;
        _channelRepository = channelRepository;
        _stockItemRepository = stockItemRepository;
        _channelRecipeLineRepository = channelRecipeLineRepository;
        _erpRecipeLineRepository = erpRecipeLineRepository;
        _channelAttributeRepository = channelAttributeRepository;
        _channelAttributeValueRepository = channelAttributeValueRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _variantManager = variantManager;
        _etsyProductClient = etsyProductClient;
        _recipeCostPopulator = recipeCostPopulator;
        _currentCompany = currentCompany;
        _imageDownloader = imageDownloader;
        _taxonomyAppService = taxonomyAppService;
    }

    public virtual async Task<List<SalesChannelEtsyProductDto>> GetListForProductAsync(Guid productId)
    {
        var companyId = EnsureCurrentCompanyId();

        // Yalnız CANLI kanalların kayıtları — soft-delete edilmiş kanalın yetim kayıtları drill'e sızmasın (N11/Trendyol ile aynı).
        var liveChannelIds = await AsyncExecuter.ToListAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.Id));

        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.ProductId == productId && liveChannelIds.Contains(x.SalesChannelId))
                .OrderBy(x => x.SellerSkuBase));

        var dtos = new List<SalesChannelEtsyProductDto>(items.Count);
        foreach (var item in items)
        {
            var dto = ObjectMapper.Map<SalesChannelEtsyProduct, SalesChannelEtsyProductDto>(item);
            await PopulateStockItemGraphAsync(item, dto);
            dtos.Add(dto);
        }

        // Taksonomi görüntü adları TEK toplu sorguda çözülür (tekrar eden id'ler tek kez; ürün başına N kanal-ürün).
        await PopulateTaxonomyDisplayAsync(dtos);
        return dtos;
    }

    public virtual async Task<SalesChannelEtsyProductDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var dto = ObjectMapper.Map<SalesChannelEtsyProduct, SalesChannelEtsyProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        await PopulateTaxonomyDisplayAsync(new[] { dto });
        return dto;
    }

    /// <summary>Okuma-anı taksonomi görüntü zenginleştirmesi (KALICI ad saklanmaz): her dolu <see cref="SalesChannelEtsyProductDto.TaxonomyId"/>
    /// synced taxonomy tablosundan tam yola çözülür. Bulundu → <see cref="SalesChannelEtsyProductDto.TaxonomyName"/>=yol,
    /// <c>IsStale=false</c>; bulunamadı (reconcile sildi/değişti) → <c>TaxonomyName=null, IsStale=true</c> ("bayat, yeniden
    /// seç"); id null → ikisi de default. TEK toplu çözüm (tekrar eden id'ler dahil); ASLA throw etmez.</summary>
    private async Task PopulateTaxonomyDisplayAsync(IReadOnlyCollection<SalesChannelEtsyProductDto> dtos)
    {
        var externalIds = dtos
            .Where(d => d.TaxonomyId.HasValue)
            .Select(d => d.TaxonomyId!.Value.ToString())
            .ToList();
        if (externalIds.Count == 0)
        {
            return;
        }

        var paths = await _taxonomyAppService.GetPathsAsync(externalIds);
        foreach (var dto in dtos)
        {
            if (dto.TaxonomyId is not { } id)
            {
                continue;
            }

            if (paths.TryGetValue(id.ToString(), out var fullPath))
            {
                dto.TaxonomyName = fullPath;
                dto.TaxonomyIsStale = false;
            }
            else
            {
                dto.TaxonomyName = null;
                dto.TaxonomyIsStale = true;   // id dolu ama tabloda yok → bayat kategori
            }
        }
    }

    /// <summary>Okuma tarafı dispatch (N11/Trendyol ile birebir): özellik modu AKTİFSE (en az 1 persist edilmiş özellik)
    /// kartezyen kombinasyon grafı, DEĞİLSE legacy ERP-doğrudan graf doldurulur. Özellik modu HİÇ aktive edilmemişse
    /// klon-sonra-ayrış TETİKLENİR: ERP ProductAttribute/Value'lardan TASLAK özellik grafı üretilir (Id boş = henüz
    /// persist YOK) — kullanıcı Kaydet'e bastığında SaveAttributesGraphAsync kalıcılaştırır. Salt-okuma DB'ye YAZMAZ.</summary>
    private async Task PopulateStockItemGraphAsync(SalesChannelEtsyProduct entity, SalesChannelEtsyProductDto dto)
    {
        var attributeEntities = await LoadChannelAttributeEntitiesAsync(entity.Id);
        var channelAttributeValues = await LoadChannelAttributeValueEntitiesAsync(attributeEntities.Select(a => a.Id).ToList());
        dto.ProductAttributes = attributeEntities.Count > 0
            ? BuildAttributesDto(attributeEntities, channelAttributeValues)
            : await BuildDraftAttributesFromErpAsync(entity.ProductId);
        dto.StockItems = attributeEntities.Count > 0
            ? await BuildAttributeStockItemsAsync(entity, ToAttributeWithValues(attributeEntities, channelAttributeValues))
            : await BuildStockItemGraphAsync(entity);
    }

    /// <summary>Klon-sonra-ayrış TETİĞİ: channelAttribute modu hiç aktive edilmemiş bir kanal-ürün açıldığında ERP
    /// ProductAttribute/Value'lardan TASLAK özellik grafı üretir (Id boş — henüz persist YOK, salt görüntü). ERP
    /// niteliksiz ürün (tek varyant) için boş liste — kullanıcı isterse elle özellik ekler.</summary>
    private async Task<List<SalesChannelEtsyProductAttributeDto>> BuildDraftAttributesFromErpAsync(Guid productId)
    {
        var attributes = await AsyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.EntityName == ProductEntityName && a.EntityId == productId)
                .OrderBy(a => a.DisplayOrder));
        if (attributes.Count == 0)
        {
            return new List<SalesChannelEtsyProductAttributeDto>();
        }

        var attributeIds = attributes.Select(a => a.Id).ToList();
        var values = await AsyncExecuter.ToListAsync(
            (await _attributeValueRepository.GetQueryableAsync())
                .Where(v => attributeIds.Contains(v.EntityAttributeId))
                .OrderBy(v => v.DisplayOrder));
        var valuesByAttribute = values.GroupBy(v => v.EntityAttributeId).ToDictionary(g => g.Key, g => g.ToList());

        return attributes.Select(a => new SalesChannelEtsyProductAttributeDto
        {
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<EntityAttributeValue>())
                .Select(v => new SalesChannelEtsyProductAttributeValueDto
                {
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<SalesChannelEtsyProductDto> CreateAsync(SalesChannelEtsyProductCreateDto input)
    {
        // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR (N11/Trendyol ile aynı 2026-07-07 kararı); kanal set-once.
        var channel = await GetOwnedChannelAsync(input.SalesChannelId);
        var product = await GetOwnedProductAsync(input.ProductId);
        var sequenceNo = await NextSequenceNoAsync(channel.Id, product.Id);

        var entity = new SalesChannelEtsyProduct(
            channel.CompanyId,
            channel.Id,
            input.ProductId,
            BuildSellerSkuBase(product.Code, sequenceNo),
            sequenceNo,
            input.ListingType);
        ApplyInput(entity, input);
        await _repository.InsertAsync(entity, autoSave: true);
        await SaveStockItemsAsync(entity, input.ProductAttributes, input.StockItems);

        var dto = ObjectMapper.Map<SalesChannelEtsyProduct, SalesChannelEtsyProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        await PopulateTaxonomyDisplayAsync(new[] { dto });
        return dto;
    }

    /// <summary>Kayıt sırası: aynı ürün+kanal içindeki max SequenceNo + 1 — SİLİNMİŞLER DAHİL (soft-delete filtresi
    /// kapalı) ki silinen kaydın Etsy'de yaşayan listelemesinin SKU tabanı yeniden üretilip EZİLMESİN.</summary>
    private async Task<int> NextSequenceNoAsync(Guid salesChannelId, Guid productId)
    {
        using (DataFilter.Disable<ISoftDelete>())
        {
            var maxExisting = await AsyncExecuter.MaxAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.SalesChannelId == salesChannelId && x.ProductId == productId),
                x => (int?)x.SequenceNo);
            return (maxExisting ?? 0) + 1;
        }
    }

    /// <summary>Etsy satıcı SKU tabanı: "{ÜrünKodu}-{Sıra}" — kayıt-bazlı benzersiz + insan-okunur (frozen).</summary>
    private static string BuildSellerSkuBase(string productCode, int sequenceNo)
    {
        return $"{productCode}-{sequenceNo}";
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelEtsyProductDto> UpdateAsync(Guid id, SalesChannelEtsyProductUpdateDto input)
    {
        var entity = await GetOwnedAsync(id);
        ApplyInput(entity, input);
        await _repository.UpdateAsync(entity, autoSave: true);
        await SaveStockItemsAsync(entity, input.ProductAttributes, input.StockItems);

        var dto = ObjectMapper.Map<SalesChannelEtsyProduct, SalesChannelEtsyProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        await PopulateTaxonomyDisplayAsync(new[] { dto });
        return dto;
    }

    /// <summary>Yazma tarafı dispatch (N11/Trendyol ile birebir): özellik grafını persist eder + persist-sonrası
    /// özellik-modu AKTİFSE kartezyen reconcile + combo-satır override/reçete kaydı; DEĞİLSE legacy ERP-doğrudan override yolu.</summary>
    private async Task SaveStockItemsAsync(
        SalesChannelEtsyProduct entity,
        List<SalesChannelEtsyProductAttributeDto> attributesInput,
        List<SalesChannelEtsyProductStockItemGraphDto> stockItemsInput)
    {
        var attributeModeActive = await SaveAttributesAndReconcileAsync(entity, attributesInput);
        if (attributeModeActive)
        {
            await SaveAttributeStockItemOverridesAsync(entity, stockItemsInput);
        }
        else
        {
            await SaveStockItemOverridesAsync(entity, stockItemsInput);
        }
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        // Kanal-özel varyant override başlıkları + reçete satırları + özellik/değer grafı (ayrı tablolar) —
        // kanal-ürünle birlikte temizlenir.
        await _channelRecipeLineRepository.DeleteAsync(r => r.SalesChannelEtsyProductId == entity.Id, autoSave: true);
        await _stockItemRepository.DeleteAsync(v => v.SalesChannelEtsyProductId == entity.Id, autoSave: true);
        var channelAttributeIds = await AsyncExecuter.ToListAsync(
            (await _channelAttributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelEtsyProductId == entity.Id)
                .Select(a => a.Id));
        if (channelAttributeIds.Count > 0)
        {
            await _channelAttributeValueRepository.DeleteAsync(v => channelAttributeIds.Contains(v.AttributeId), autoSave: true);
            await _channelAttributeRepository.DeleteAsync(a => a.SalesChannelEtsyProductId == entity.Id, autoSave: true);
        }

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── Etsy varyant ÖZELLİKLERİ (klon-sonra-ayrış) + kartezyen kombinasyon RECONCILE ─────────────────────
    // ProductAttributes = Etsy'nin KENDİ varyant özellikleri (N11/Trendyol deseninin portu). Tanımlıysa (persist edilmiş
    // en az 1 özellik varsa) kanal-ürünün kombinasyon seti ARTIK bu özelliklerin kartezyen kombinasyonundan üretilir —
    // legacy ERP-doğrudan graf (BuildStockItemGraphAsync/SaveStockItemOverridesAsync) devre dışı kalır. Reconcile anahtarı
    // CombinationSignature ("{AttributeId}={ValueId}|...", AttributeId sıralı) — STABİL ID'lerden kurulur, ERP
    // ProductVariantId yalnız fiyat/stok fallback KAYNAĞI (bir kerelik fırsatçı eşleştirme; reconcile anahtarı DEĞİL).
    // Özellik/değer silinip kombinasyon artık üretilemezse o satır + reçetesi TEMİZLENİR.

    /// <summary>Bellek-içi özellik + değer görünümü — reconcile matematiği (kartezyen + imza) için.</summary>
    private sealed record AttributeWithValues(Guid AttributeId, string AttributeName, List<(Guid ValueId, string Value)> Values);

    private async Task<List<SalesChannelEtsyProductAttribute>> LoadChannelAttributeEntitiesAsync(Guid channelProductId)
    {
        return await AsyncExecuter.ToListAsync(
            (await _channelAttributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelEtsyProductId == channelProductId)
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.CreationTime));
    }

    private async Task<List<SalesChannelEtsyProductAttributeValue>> LoadChannelAttributeValueEntitiesAsync(List<Guid> channelAttributeIds)
    {
        if (channelAttributeIds.Count == 0)
        {
            return new List<SalesChannelEtsyProductAttributeValue>();
        }

        return await AsyncExecuter.ToListAsync(
            (await _channelAttributeValueRepository.GetQueryableAsync())
                .Where(v => channelAttributeIds.Contains(v.AttributeId))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.CreationTime));
    }

    private static List<SalesChannelEtsyProductAttributeDto> BuildAttributesDto(
        List<SalesChannelEtsyProductAttribute> channelAttributes, List<SalesChannelEtsyProductAttributeValue> values)
    {
        var valuesByChannelAttribute = values.GroupBy(v => v.AttributeId).ToDictionary(g => g.Key, g => g.ToList());
        return channelAttributes.Select(a => new SalesChannelEtsyProductAttributeDto
        {
            Id = a.Id,
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByChannelAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<SalesChannelEtsyProductAttributeValue>())
                .Select(v => new SalesChannelEtsyProductAttributeValueDto
                {
                    Id = v.Id,
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    private static List<AttributeWithValues> ToAttributeWithValues(
        List<SalesChannelEtsyProductAttribute> channelAttributes, List<SalesChannelEtsyProductAttributeValue> values)
    {
        var valuesByChannelAttribute = values.GroupBy(v => v.AttributeId).ToDictionary(g => g.Key, g => g.ToList());
        return channelAttributes.Select(a => new AttributeWithValues(
            a.Id,
            a.Name,
            (valuesByChannelAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<SalesChannelEtsyProductAttributeValue>())
                .Select(v => (v.Id, v.Value))
                .ToList())).ToList();
    }

    /// <summary>Özellik + değer grafını persist eder (RecipeLines ile AYNI iki-öge diff deseni: silinenler → upsert;
    /// ClientKey→Id input DTO'suna geri yazılır). Boş/null girdi no-op (mevcut özelliklere DOKUNMAZ).</summary>
    private async Task SaveAttributesGraphAsync(SalesChannelEtsyProduct channelProduct, List<SalesChannelEtsyProductAttributeDto>? attributesInput)
    {
        if (attributesInput is not { Count: > 0 })
        {
            return;
        }

        await EnsureAttributeCountWithinLimitAsync(channelProduct.Id, attributesInput);

        foreach (var channelAttribute in attributesInput.Where(a => a.IsDeleted && a.Id != Guid.Empty))
        {
            await _channelAttributeValueRepository.DeleteAsync(v => v.AttributeId == channelAttribute.Id, autoSave: true);
            await _channelAttributeRepository.DeleteAsync(channelAttribute.Id, autoSave: true);
        }

        foreach (var channelAttribute in attributesInput.Where(a => !a.IsDeleted))
        {
            SalesChannelEtsyProductAttribute entity;
            if (channelAttribute.Id == Guid.Empty)
            {
                entity = new SalesChannelEtsyProductAttribute(channelProduct.CompanyId, channelProduct.Id, channelAttribute.Name, channelAttribute.DisplayOrder);
                await _channelAttributeRepository.InsertAsync(entity, autoSave: true);
                channelAttribute.Id = entity.Id;
            }
            else
            {
                entity = await _channelAttributeRepository.GetAsync(channelAttribute.Id);
                entity.SetName(channelAttribute.Name);
                entity.SetDisplayOrder(channelAttribute.DisplayOrder);
                await _channelAttributeRepository.UpdateAsync(entity, autoSave: true);
            }

            foreach (var value in channelAttribute.Values.Where(v => v.IsDeleted && v.Id != Guid.Empty))
            {
                await _channelAttributeValueRepository.DeleteAsync(value.Id, autoSave: true);
            }

            foreach (var value in channelAttribute.Values.Where(v => !v.IsDeleted))
            {
                if (value.Id == Guid.Empty)
                {
                    var valueEntity = new SalesChannelEtsyProductAttributeValue(channelProduct.CompanyId, channelAttribute.Id, value.Value, value.DisplayOrder);
                    await _channelAttributeValueRepository.InsertAsync(valueEntity, autoSave: true);
                    value.Id = valueEntity.Id;
                }
                else
                {
                    var valueEntity = await _channelAttributeValueRepository.GetAsync(value.Id);
                    valueEntity.SetValue(value.Value);
                    valueEntity.SetDisplayOrder(value.DisplayOrder);
                    await _channelAttributeValueRepository.UpdateAsync(valueEntity, autoSave: true);
                }
            }
        }
    }

    /// <summary>Persist SONRASI oluşacak özellik sayısını (mevcut − silinen + yeni) ERP simetriği üst-sınıra
    /// (<see cref="ProductAttributeConsts.MaxAttributesPerProduct"/> = 5) karşı doğrular — persist BAŞLAMADAN fail-fast
    /// (N11/Trendyol guard'ının portu). Üst-sınır CombinationSignature kolon kapasitesini de korur.</summary>
    private async Task EnsureAttributeCountWithinLimitAsync(Guid channelProductId, List<SalesChannelEtsyProductAttributeDto> attributesInput)
    {
        var deletedIds = attributesInput
            .Where(a => a.IsDeleted && a.Id != Guid.Empty)
            .Select(a => a.Id)
            .ToHashSet();
        var survivingExistingCount = (await LoadChannelAttributeEntitiesAsync(channelProductId))
            .Count(a => !deletedIds.Contains(a.Id));
        var newCount = attributesInput.Count(a => !a.IsDeleted && a.Id == Guid.Empty);
        if (survivingExistingCount + newCount > ProductAttributeConsts.MaxAttributesPerProduct)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:TooManyAttributes")
                .WithData("Max", ProductAttributeConsts.MaxAttributesPerProduct);
        }
    }

    /// <summary>Özellik grafını persist eder + persist-sonrası DB durumuna göre kartezyen kombinasyon satırlarını
    /// reconcile eder. Döndürdüğü bool = channelAttribute-modu AKTİF mi (en az 1 persist edilmiş özellik var) — false
    /// ise çağıran legacy ERP-doğrudan yola (<see cref="BuildStockItemGraphAsync"/>/<see cref="SaveStockItemOverridesAsync"/>) düşer.</summary>
    private async Task<bool> SaveAttributesAndReconcileAsync(SalesChannelEtsyProduct channelProduct, List<SalesChannelEtsyProductAttributeDto>? attributesInput)
    {
        await SaveAttributesGraphAsync(channelProduct, attributesInput);

        var attributeEntities = await LoadChannelAttributeEntitiesAsync(channelProduct.Id);
        if (attributeEntities.Count == 0)
        {
            return false;
        }

        var channelAttributeValues = await LoadChannelAttributeValueEntitiesAsync(attributeEntities.Select(a => a.Id).ToList());
        await SynchronizeStockItemsAsync(channelProduct, ToAttributeWithValues(attributeEntities, channelAttributeValues));
        return true;
    }

    /// <summary>Kanal özelliklerinin (AttributeId, ValueId) kartezyeni — matematik <see cref="VariantCombinationEngine"/>'e
    /// devredilmiştir (N11/Trendyol bağlama şekliyle BİREBİR). "0 özellik → kombinasyon yok" yorumu çağıran guard'ıdır.</summary>
    private static List<List<(Guid AttributeId, Guid ValueId)>> BuildCombinations(List<AttributeWithValues> channelAttributes)
    {
        if (channelAttributes.Count == 0)
        {
            return new List<List<(Guid AttributeId, Guid ValueId)>>();
        }

        var axes = channelAttributes
            .Select(a => (Axis: a.AttributeId, Values: (IReadOnlyList<Guid>)a.Values.Select(v => v.ValueId).ToList()))
            .ToList();
        return VariantCombinationEngine.BuildCartesian<Guid, Guid>(axes);
    }

    /// <summary>Kombinasyon imzası — N11/Trendyol ile AYNI format ("{AttributeId}={ValueId}|...", AttributeId artan
    /// sıralı). BİLİNÇLİ olarak <see cref="VariantCombinationEngine.BuildKey"/>'e delege EDİLMEZ (format farklı; snapshot).</summary>
    private static string BuildCombinationSignature(IEnumerable<(Guid AttributeId, Guid ValueId)> pairs)
    {
        return string.Join('|', pairs.OrderBy(p => p.AttributeId).Select(p => $"{p.AttributeId}={p.ValueId}"));
    }

    /// <summary>ERP varyantlarının (AttributeName, ValueText) normalize edilmiş küme indeksi — fırsatçı eşleştirme kaynağı.</summary>
    private async Task<Dictionary<Guid, HashSet<(string Name, string Value)>>> BuildErpVariantOptionSetIndexAsync(Guid productId)
    {
        var variantIds = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId).Select(v => v.Id));
        var options = await LoadVariantOptionsAsync(productId, variantIds);
        return options.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(p => (Name: NormalizeForMatch(p.Name), Value: NormalizeForMatch(p.Value))).ToHashSet());
    }

    private static string NormalizeForMatch(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    /// <summary>Bir Etsy kombinasyonunun (Attribute.Name/AttributeValue.Value seti) ERP varyantlarından TAM örtüşen
    /// tekini bulur (bir kerelik fırsatçı eşleştirme — reconcile anahtarı DEĞİL). Örtüşme YOKSA ya da BİRDEN FAZLA
    /// varyant aynı sete sahipse (belirsiz) null döner — yanlış atamaktansa Etsy-only kalması güvenli.</summary>
    private static Guid? MatchErpVariant(List<(string Name, string Value)> comboOptionSet, Dictionary<Guid, HashSet<(string Name, string Value)>> erpIndex)
    {
        var normalizedCombo = comboOptionSet.Select(p => (Name: NormalizeForMatch(p.Name), Value: NormalizeForMatch(p.Value))).ToHashSet();
        Guid? match = null;
        foreach (var (variantId, optionSet) in erpIndex)
        {
            if (optionSet.Count != normalizedCombo.Count || !optionSet.SetEquals(normalizedCombo))
            {
                continue;
            }

            if (match is not null)
            {
                return null;   // birden fazla ERP varyantı aynı sete sahip → belirsiz
            }

            match = variantId;
        }

        return match;
    }

    /// <summary>Kartezyen kombinasyon satırlarını (<see cref="SalesChannelEtsyProductStockItem"/>, CombinationSignature
    /// ile) mevcut özellik/değer setiyle reconcile eder — diff/sıra mekaniği <see cref="VariantSetReconciler"/>'da
    /// (N11/Trendyol BİREBİR): artık üretilemeyen kombinasyonlar (satır + reçetesi) removeAsync'te SİLİNİR (orphan
    /// temizliği), eksik kombinasyonlar addAsync'te İNSERT edilir (fırsatçı ERP eşleştirmesiyle — KANAL politikası).
    /// Var olan satırlara (imzası hâlâ üretilebilir) DOKUNULMAZ — kullanıcı override/reçete verisi korunur.</summary>
    private async Task SynchronizeStockItemsAsync(SalesChannelEtsyProduct channelProduct, List<AttributeWithValues> channelAttributes)
    {
        var combos = BuildCombinations(channelAttributes);
        var comboBySignature = new Dictionary<string, List<(Guid AttributeId, Guid ValueId)>>(StringComparer.Ordinal);
        foreach (var combo in combos)
        {
            comboBySignature[BuildCombinationSignature(combo)] = combo;
        }

        var existingHeaders = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelEtsyProductId == channelProduct.Id && h.CombinationSignature != null));

        // ERP indeksi TEMBEL: ilk eksik kombinasyonda yüklenir — eksik yoksa ERP sorgusu hiç atılmaz.
        Dictionary<Guid, HashSet<(string Name, string Value)>>? erpIndex = null;
        var attributeById = channelAttributes.ToDictionary(a => a.AttributeId);

        await VariantSetReconciler.ReconcileAsync(
            targetKeys: combos.Select(BuildCombinationSignature).ToList(),
            existingItems: existingHeaders,
            keySelector: h => h.CombinationSignature!,
            removeAsync: async orphan =>
            {
                await _channelRecipeLineRepository.DeleteAsync(
                    r => r.SalesChannelEtsyProductId == channelProduct.Id && r.StockItemId == orphan.Id,
                    autoSave: true);
                await _stockItemRepository.DeleteAsync(orphan, autoSave: true);
            },
            addAsync: async signature =>
            {
                erpIndex ??= await BuildErpVariantOptionSetIndexAsync(channelProduct.ProductId);
                var combo = comboBySignature[signature];
                var optionSet = combo
                    .Select(p => (Name: attributeById[p.AttributeId].AttributeName, Value: attributeById[p.AttributeId].Values.First(v => v.ValueId == p.ValueId).Value))
                    .ToList();
                var matchedVariantId = MatchErpVariant(optionSet, erpIndex);

                var header = new SalesChannelEtsyProductStockItem(channelProduct.CompanyId, channelProduct.Id, matchedVariantId);
                header.SetCombinationSignature(signature);
                await _stockItemRepository.InsertAsync(header, autoSave: true);
            });
    }

    private static string BuildCombinationLabel(string signature, Dictionary<Guid, AttributeWithValues> attributeById)
    {
        var parts = new List<string>();
        foreach (var pair in signature.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var segments = pair.Split('=');
            if (segments.Length != 2 || !Guid.TryParse(segments[0], out var attributeId) || !Guid.TryParse(segments[1], out var valueId))
            {
                continue;
            }

            if (attributeById.TryGetValue(attributeId, out var channelAttribute))
            {
                var value = channelAttribute.Values.FirstOrDefault(v => v.ValueId == valueId).Value;
                parts.Add($"{channelAttribute.AttributeName}: {value}");
            }
        }

        return string.Join("; ", parts);
    }

    /// <summary>Kartezyen kombinasyon satırlarını graf DTO'suna projekte eder (reconcile'ın ÜRETTİĞİ set — reconcile
    /// bu metottan ÖNCE çağrılmış olmalı). ERP-backed (ProductVariantId dolu) satırda da anchor HALA header.Id'dir.</summary>
    private async Task<List<SalesChannelEtsyProductStockItemGraphDto>> BuildAttributeStockItemsAsync(
        SalesChannelEtsyProduct channelProduct, List<AttributeWithValues> channelAttributes)
    {
        var headers = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelEtsyProductId == channelProduct.Id && h.CombinationSignature != null)
                .OrderBy(h => h.CreationTime));
        if (headers.Count == 0)
        {
            return new List<SalesChannelEtsyProductStockItemGraphDto>();
        }

        var headerIds = headers.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelEtsyProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var erpVariantIds = headers.Where(h => h.ProductVariantId is not null).Select(h => h.ProductVariantId!.Value).Distinct().ToList();
        var erpVariantsById = erpVariantIds.Count == 0
            ? new Dictionary<Guid, EntityVariant>()
            : (await AsyncExecuter.ToListAsync(
                    (await _variantRepository.GetQueryableAsync()).Where(v => erpVariantIds.Contains(v.Id))))
                .ToDictionary(v => v.Id);

        // ERP reçetesi + yan-maliyet planı — kaydedilmiş kanal reçetesi OLMAYAN, ERP-eşleşmiş kombinasyon satırında
        // klon-sonra-ayrış (legacy graf BuildStockItemGraphAsync ile AYNI davranış; kartezyen üretilen satır
        // reçetesiz/yan-maliyetsiz kalmasın). Etsy-only satırda (ERP eşleşmesi yok) taban maliyet bilinmez → reçete boş.
        var erpByVariant = erpVariantIds.Count == 0
            ? new Dictionary<Guid, List<ProductVariantRecipeLine>>()
            : (await AsyncExecuter.ToListAsync(
                    (await _erpRecipeLineRepository.GetQueryableAsync())
                        .Where(r => erpVariantIds.Contains(r.ProductVariantId))))
                .GroupBy(r => r.ProductVariantId)
                .ToDictionary(g => g.Key, g => g.ToList());
        var sideCostPlan = await BuildSideCostPlanAsync(channelProduct);

        var attributeById = channelAttributes.ToDictionary(a => a.AttributeId);
        var nodes = new List<SalesChannelEtsyProductStockItemGraphDto>(headers.Count);
        foreach (var header in headers)
        {
            var erpVariant = header.ProductVariantId is { } erpId && erpVariantsById.TryGetValue(erpId, out var v) ? v : null;

            List<ProductRecipeLineGraphDto> recipeLines;
            if (savedByHeader.TryGetValue(header.Id, out var saved))
            {
                recipeLines = MapSavedRecipeLines(saved);
            }
            else if (header.ProductVariantId is { } variantId && erpByVariant.TryGetValue(variantId, out var erp))
            {
                // Klon-sonra-ayrış: ERP reçetesi kanal reçetesinin başlangıcı — yan-maliyet satırları burada eklenir.
                recipeLines = CloneErpRecipeLines(erp);
                SideCostRecipeComposer.EnsureLines(
                    recipeLines, sideCostPlan with { VariantOptInEnabled = header.InsuredShippingEnabled });
            }
            else
            {
                recipeLines = new List<ProductRecipeLineGraphDto>();
            }

            var node = new SalesChannelEtsyProductStockItemGraphDto
            {
                Id = header.Id,
                ProductVariantId = header.ProductVariantId,
                VariantCode = erpVariant?.Code ?? string.Empty,
                VariantName = erpVariant?.Name ?? string.Empty,
                CombinationLabel = BuildCombinationLabel(header.CombinationSignature!, attributeById),
                OverridePrice = header.OverridePrice,
                OverridePriceCurrencyUnitId = header.OverridePriceCurrencyUnitId,
                OverrideStock = header.OverrideStock,
                Margin = header.Margin,
                InsuredShippingEnabled = header.InsuredShippingEnabled,
                RecipeLines = recipeLines,
            };
            nodes.Add(node);
        }

        await PopulateNodeCostsAsync(nodes);
        return nodes;
    }

    /// <summary>Kartezyen kombinasyon satırlarının (zaten reconcile ile server-side üretilmiş) düzenlenebilir
    /// alanlarını (OverridePrice/OverrideStock/Margin/RecipeLines) kullanıcı girdisinden persist eder. Client YENİ
    /// satır AÇAMAZ (Id boş düğüm atlanır — reconcile tek üretici); yabancı/bayat Id sessizce atlanır. Etsy-only
    /// (ProductVariantId null) satırda ERP fallback'i YOKTUR → OverridePrice + OverrideStock ZORUNLU (fail-fast).</summary>
    private async Task SaveAttributeStockItemOverridesAsync(SalesChannelEtsyProduct channelProduct, List<SalesChannelEtsyProductStockItemGraphDto>? variants)
    {
        if (variants is not { Count: > 0 })
        {
            return;
        }

        SideCostPlan? sideCostPlan = null;   // tembel — yalnız sigorta anahtarı değişen satır varsa kurulur

        foreach (var node in variants)
        {
            if (node.Id == Guid.Empty)
            {
                continue;
            }

            var header = await _stockItemRepository.FindAsync(node.Id);
            if (header is null || header.SalesChannelEtsyProductId != channelProduct.Id)
            {
                continue;
            }

            if (header.ProductVariantId is null && (node.OverridePrice is null || node.OverrideStock is null))
            {
                throw new BusinessException("TradeXpress:Etsy:ProductVariant:OverrideRequiredForEtsyOnly");
            }

            var insuredShippingChanged = header.InsuredShippingEnabled != node.InsuredShippingEnabled;
            header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
            header.SetOverrideStock(node.OverrideStock);
            header.SetMargin(node.Margin);
            header.SetInsuredShippingEnabled(node.InsuredShippingEnabled);
            await _stockItemRepository.UpdateAsync(header, autoSave: true);

            // Sigortalı-gönderim anahtarı bu save'de DEĞİŞTİYSE reçeteye hemen işlenir (yalnız sigorta satırı —
            // kullanıcının sildiği diğer otomatik satırlar geri getirilmez); yoksa türetilmiş fiyat açık
            // "Giderleri Yeniden Uygula"ya kadar bayat kalırdı (N11/Trendyol simetriği).
            if (insuredShippingChanged)
            {
                sideCostPlan ??= await BuildSideCostPlanAsync(channelProduct);
                node.RecipeLines ??= new List<ProductRecipeLineGraphDto>();
                SideCostRecipeComposer.SyncVariantOptInLines(
                    node.RecipeLines, sideCostPlan with { VariantOptInEnabled = node.InsuredShippingEnabled });
            }

            await SaveChannelRecipeLinesAsync(channelProduct, header.Id, node.RecipeLines);
        }
    }

    // ── Kanal-özel varyant override (fiyat/stok/marj + reçete) — LEGACY ERP-doğrudan yol ─────────────
    // Graf = ERP varyant seti (aktif) ⋈ kaydedilmiş kanal override (LEFT JOIN). Kaydedilmiş reçete varsa ondan,
    // yoksa ERP reçetesi KLONLANIR. NetCost + türetilmiş fiyat CANLI hesaplanır (ProductAppService ile ORTAK motor).

    /// <summary>Bir kanal-ürünün varyant override grafını kurar: aktif ERP varyantları × kaydedilmiş override başlığı
    /// (fiyat/stok/marj) + reçete (kaydedilmişse ondan, yoksa ERP reçetesinden klon). NetCost + türetilmiş fiyat
    /// (NetCost×(1+Margin/100)) canlı hesaplanır. Varyant yoksa boş liste.</summary>
    private async Task<List<SalesChannelEtsyProductStockItemGraphDto>> BuildStockItemGraphAsync(SalesChannelEtsyProduct channelProduct)
    {
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == channelProduct.ProductId && v.IsActive)
                .OrderByDescending(v => v.IsMain).ThenBy(v => v.Code));
        if (variants.Count == 0)
        {
            return new List<SalesChannelEtsyProductStockItemGraphDto>();
        }

        var variantIds = variants.Select(v => v.Id).ToList();

        // Yalnız ERP-backed başlıklar (ProductVariantId dolu) — Etsy-only satırlar bu ERP-varyant grafına girmez
        // (kendi grubunda listelenir; bkz. BuildAttributeStockItemsAsync).
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelEtsyProductId == channelProduct.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        // Reçete satırları override BAŞLIĞININ kendi Id'sine bağlı (StockItemId) — önce header.Id'ye, sonra ERP varyantına eşlenir.
        var headerIds = headers.Values.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelEtsyProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ERP reçetesi — yalnız kaydedilmiş kanal reçetesi OLMAYAN varyantlarda klonlanır (LEFT JOIN eksiği ERP'den).
        var erpByVariant = (await AsyncExecuter.ToListAsync(
                (await _erpRecipeLineRepository.GetQueryableAsync())
                    .Where(r => variantIds.Contains(r.ProductVariantId))))
            .GroupBy(r => r.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Yan-maliyet planı (kanal ayarı; komisyon = kanal varsayılanı) — yalnız KLON yoluna uygulanır (kaydedilmiş
        // reçeteye dokunulmaz; silinen otomatik satır kendiliğinden geri gelmesin — açık "yeniden uygula" var).
        var sideCostPlan = await BuildSideCostPlanAsync(channelProduct);

        var nodes = new List<SalesChannelEtsyProductStockItemGraphDto>(variants.Count);
        foreach (var v in variants)
        {
            var node = new SalesChannelEtsyProductStockItemGraphDto
            {
                ProductVariantId = v.Id,
                VariantCode = v.Code,
                VariantName = v.Name,
            };

            if (headers.TryGetValue(v.Id, out var header))
            {
                node.Id = header.Id;
                node.OverridePrice = header.OverridePrice;
                node.OverridePriceCurrencyUnitId = header.OverridePriceCurrencyUnitId;
                node.OverrideStock = header.OverrideStock;
                node.Margin = header.Margin;
                node.InsuredShippingEnabled = header.InsuredShippingEnabled;
            }

            // BİLİNEN SINIR (klon-sonra-ayrış deseni): "kaydedilmiş reçete var mı" ayrımı SATIR-varlığına dayalıdır —
            // kullanıcı kaydedilmiş reçetenin TÜM satırlarını silerse "hiç kurulmadı" ile ayırt edilemez ve sonraki
            // açılışta klon dalı ERP reçetesini + yan-maliyet satırlarını yeniden üretir. Kabul edilmiş davranış:
            // boş reçete = "ERP'den yeniden devral" sinyali sayılır.
            if (header is not null && savedByHeader.TryGetValue(header.Id, out var saved))
            {
                node.RecipeLines = MapSavedRecipeLines(saved);
            }
            else if (erpByVariant.TryGetValue(v.Id, out var erp))
            {
                // Klon-sonra-ayrış: ERP reçetesi kanal reçetesinin başlangıcı — yan-maliyet satırları burada eklenir.
                node.RecipeLines = CloneErpRecipeLines(erp);
                SideCostRecipeComposer.EnsureLines(
                    node.RecipeLines, sideCostPlan with { VariantOptInEnabled = node.InsuredShippingEnabled });
            }
            else
            {
                node.RecipeLines = new List<ProductRecipeLineGraphDto>();
            }

            nodes.Add(node);
        }

        await PopulateNodeCostsAsync(nodes);
        return nodes;
    }

    /// <summary>Düğümlerin CANLI net maliyet + türetilmiş fiyatını doldurur (kartezyen ve legacy graf ORTAK sonu).</summary>
    private async Task PopulateNodeCostsAsync(List<SalesChannelEtsyProductStockItemGraphDto> nodes)
    {
        var costs = await _recipeCostPopulator.PopulateAsync(nodes.Select(n => n.RecipeLines).ToList());
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            node.NetCost = costs[i].NetCost;
            node.NetCostCurrency = costs[i].NetCostCurrency;
            node.NetCostMissingRate = costs[i].NetCostMissingRate;
            node.DerivedPrice = costs[i].NetCost is { } nc && !costs[i].NetCostMissingRate
                ? DerivedPriceCalculator.Calculate(nc, node.Margin)
                : null;
        }
    }

    /// <summary>Yan-maliyet planını kurar: kanal gider satırları; çözülmüş komisyon oranı YOK (Etsy kategori komisyon
    /// tablosu yok — Trendyol ile hizalı; komisyon gider satırı oranını doğrudan Value'sundan alır). Varyant opt-in
    /// anahtarı varyant-başı olduğundan burada KAPALI döner — çağıran <c>plan with { VariantOptInEnabled = ... }</c> ile açar.</summary>
    private async Task<SideCostPlan> BuildSideCostPlanAsync(SalesChannelEtsyProduct channelProduct)
    {
        var channel = await _channelRepository.FindAsync(channelProduct.SalesChannelId);
        return SideCostPlan.From(channel?.SideCosts, resolvedCommissionRate: null, variantOptInEnabled: false);
    }

    /// <summary>Kanal-özel varyant override grafını persist eder (LEGACY ERP-doğrudan yol) — override sinyali
    /// (OverridePrice/OverrideStock/Margin herhangi biri dolu) olan varyantın başlığı + reçetesi yazılır; TÜMÜ boşsa
    /// (saf ERP devralma) kaydedilmiş override/reçete TEMİZLENİR (ölü satır şişmesini önle). Türetilmiş fiyat/NetCost
    /// hesap alanları PERSIST EDİLMEZ (canlı).</summary>
    private async Task SaveStockItemOverridesAsync(SalesChannelEtsyProduct channelProduct, List<SalesChannelEtsyProductStockItemGraphDto> variants)
    {
        if (variants == null || variants.Count == 0)
        {
            return;
        }

        // Yalnız ERP-backed başlıklar — Etsy-only satırlar (ProductVariantId null) bu ERP-anchor'lı override yolundan
        // GEÇMEZ, kartezyen motor (SynchronizeStockItemsAsync) tarafından ayrıca üretilir/güncellenir.
        var existingHeaders = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelEtsyProductId == channelProduct.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        SideCostPlan? sideCostPlan = null;   // tembel — yalnız sigorta anahtarı değişen satır varsa kurulur

        foreach (var node in variants)
        {
            if (node.ProductVariantId is null || node.ProductVariantId == Guid.Empty)
            {
                continue;   // anchor yok (Etsy-only ya da bayat düğüm) → atla; kartezyen motor ele alır
            }

            // Persist sinyali: override alanı VEYA kanal-özel reçete girilmişse korunur (reçete-only + boş marj de
            // emek → silinmesin). Hepsi gerçekten boşsa (saf ERP devralma) kaydedilmiş override/reçete temizlenir.
            var hasRecipe = node.RecipeLines?.Any(l => !l.IsDeleted) == true;
            var hasOverride = node.OverridePrice is not null || node.OverrideStock is not null
                || node.Margin is not null || node.InsuredShippingEnabled || hasRecipe;
            existingHeaders.TryGetValue(node.ProductVariantId.Value, out var header);

            if (!hasOverride)
            {
                // Saf devralma → kaydedilmiş override başlığı + reçete satırlarını sil (ERP'ye geri dön). Reçete
                // satırları header'ın KENDİ Id'sine bağlı (StockItemId) — önce onunla sil, sonra başlığı.
                if (header is not null)
                {
                    await _channelRecipeLineRepository.DeleteAsync(
                        r => r.SalesChannelEtsyProductId == channelProduct.Id && r.StockItemId == header.Id,
                        autoSave: true);
                    await _stockItemRepository.DeleteAsync(header, autoSave: true);
                }

                continue;
            }

            var insuredShippingChanged = (header?.InsuredShippingEnabled ?? false) != node.InsuredShippingEnabled;
            if (header is null)
            {
                header = new SalesChannelEtsyProductStockItem(channelProduct.CompanyId, channelProduct.Id, node.ProductVariantId);
                header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
                header.SetOverrideStock(node.OverrideStock);
                header.SetMargin(node.Margin);
                header.SetInsuredShippingEnabled(node.InsuredShippingEnabled);
                await _stockItemRepository.InsertAsync(header, autoSave: true);
            }
            else
            {
                header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
                header.SetOverrideStock(node.OverrideStock);
                header.SetMargin(node.Margin);
                header.SetInsuredShippingEnabled(node.InsuredShippingEnabled);
                await _stockItemRepository.UpdateAsync(header, autoSave: true);
            }

            // Sigortalı-gönderim anahtarı bu save'de DEĞİŞTİYSE reçeteye hemen işlenir (yalnız sigorta satırı —
            // kullanıcının sildiği diğer otomatik satırlar geri getirilmez); yoksa türetilmiş fiyat açık
            // "Giderleri Yeniden Uygula"ya kadar bayat kalırdı (N11/Trendyol simetriği).
            if (insuredShippingChanged)
            {
                sideCostPlan ??= await BuildSideCostPlanAsync(channelProduct);
                node.RecipeLines ??= new List<ProductRecipeLineGraphDto>();
                SideCostRecipeComposer.SyncVariantOptInLines(
                    node.RecipeLines, sideCostPlan with { VariantOptInEnabled = node.InsuredShippingEnabled });
            }

            await SaveChannelRecipeLinesAsync(channelProduct, header.Id, node.RecipeLines);
        }
    }

    /// <summary>Bir override BAŞLIĞININ (ERP-backed veya Etsy-only fark etmez — <paramref name="stockItemId"/> her
    /// zaman <see cref="SalesChannelEtsyProductStockItem"/>'ın KENDİ Id'sidir) kanal-özel reçete satırlarını persist
    /// eder (ERP SaveRecipeLinesAsync deseni, iki-geçişli): silinenler → LineOrder 0..n yeniden-numara → referans
    /// doğrulama → skaler insert/update (1. geçiş) → türev SelectedLines kaynak Id CSV çözümü (2. geçiş).
    /// ComponentType set-once (ctor'da).</summary>
    private async Task SaveChannelRecipeLinesAsync(SalesChannelEtsyProduct channelProduct, Guid stockItemId, List<ProductRecipeLineGraphDto> lines)
    {
        lines ??= new List<ProductRecipeLineGraphDto>();

        foreach (var l in lines.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _channelRecipeLineRepository.DeleteAsync(l.Id, autoSave: true);
        }

        var survivors = lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineOrder).ToList();
        for (var i = 0; i < survivors.Count; i++)
        {
            survivors[i].LineOrder = i;
        }

        RecipeCostPopulator.ValidateDerivedReferences(survivors);

        // 1. geçiş: skaler alanlar (türev SelectedLines kaynakları HARİÇ) → ClientKey→Id (+ entity) sözlükleri.
        var idByClientKey = new Dictionary<Guid, Guid>();
        var entityByClientKey = new Dictionary<Guid, SalesChannelEtsyProductStockItemRecipeLine>();
        foreach (var l in survivors)
        {
            SalesChannelEtsyProductStockItemRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new SalesChannelEtsyProductStockItemRecipeLine(
                    channelProduct.CompanyId, channelProduct.Id, stockItemId, l.ComponentType, l.LineOrder);
                ApplyChannelRecipeLineFields(entity, l);
                await _channelRecipeLineRepository.InsertAsync(entity, autoSave: true);
                l.Id = entity.Id;
            }
            else
            {
                entity = await _channelRecipeLineRepository.GetAsync(l.Id);
                entity.SetOrder(l.LineOrder);
                ApplyChannelRecipeLineFields(entity, l);
                await _channelRecipeLineRepository.UpdateAsync(entity, autoSave: true);
            }

            idByClientKey[l.ClientKey] = l.Id;
            entityByClientKey[l.ClientKey] = entity;
        }

        // 2. geçiş: türev SelectedLines kaynak ClientKey'lerini çözülmüş Id CSV'sine çevir + persist.
        foreach (var l in survivors.Where(x => x.ComponentType == RecipeComponentType.Service
            && x.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines))
        {
            var csv = string.Join('|', l.DerivedSourceKeys.Select(k => idByClientKey[k].ToString()));
            var entity = entityByClientKey[l.ClientKey];
            entity.SetDerivedSources(csv);
            await _channelRecipeLineRepository.UpdateAsync(entity, autoSave: true);
        }
    }

    /// <summary>Graf düğümünün alanlarını kanal reçete satırına uygular (ERP ApplyRecipeLineFields ile birebir;
    /// ComponentType ctor'da atanır → burada değiştirilmez).</summary>
    private static void ApplyChannelRecipeLineFields(SalesChannelEtsyProductStockItemRecipeLine entity, ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.CatalogCommodity)
        {
            entity.SetCatalogCommodity(
                l.CommodityProcessType.GetValueOrDefault(),
                l.CommodityId,
                l.Quantity,
                l.Amount,
                l.Factor,
                l.ValuationUnitId,
                l.PaymentType,
                l.PayFactor,
                l.PayUnitId);
        }
        else
        {
            entity.SetService(
                l.CommodityId,
                l.DerivedBaseMode.GetValueOrDefault(RecipeDerivedBaseMode.AllAbove),
                l.DerivedOperation.GetValueOrDefault(RecipeDerivedOperation.Percent),
                l.DerivedOperand,
                l.PayUnitId);
        }

        entity.SetDescription(l.Description);
        entity.SetSideCostKind(l.SideCostKind);
    }

    /// <summary>Kaydedilmiş kanal reçete satırlarını graf DTO'suna projekte eder (Id KORUNUR — mevcut satır) +
    /// türev SelectedLines kaynaklarını taze ClientKey'lere çözer (ORTAK resolver).</summary>
    private static List<ProductRecipeLineGraphDto> MapSavedRecipeLines(List<SalesChannelEtsyProductStockItemRecipeLine> saved)
    {
        var ordered = saved.OrderBy(r => r.LineOrder).ThenBy(r => r.CreationTime).ToList();
        var dtos = ordered.Select(r => new ProductRecipeLineGraphDto
        {
            Id = r.Id,
            LineOrder = r.LineOrder,
            ComponentType = r.ComponentType,
            CommodityProcessType = r.CommodityProcessType,
            CommodityId = r.CommodityId,
            Quantity = r.Quantity,
            Amount = r.Amount,
            Factor = r.Factor,
            ValuationUnitId = r.ValuationUnitId,
            PaymentType = r.PaymentType,
            PayFactor = r.PayFactor,
            PayUnitId = r.PayUnitId,
            ManualAmount = r.ManualAmount ?? 0m,
            ManualUnitId = r.ManualUnitId,
            Description = r.Description,
            DerivedBaseMode = r.DerivedBaseMode,
            DerivedOperation = r.DerivedOperation,
            DerivedOperand = r.DerivedOperand,
            SideCostKind = r.SideCostKind,
        }).ToList();

        var sourceCsvById = ordered
            .Where(e => e.ComponentType == RecipeComponentType.Service && !string.IsNullOrEmpty(e.DerivedSourceLineIds))
            .ToDictionary(e => e.Id, e => e.DerivedSourceLineIds!);
        RecipeCostPopulator.ResolveDerivedSourceKeys(dtos, sourceCsvById);

        return dtos;
    }

    /// <summary>ERP varyant reçete satırlarını KANAL grafına klonlar (Id BOŞ = kaydedilirse yeni bağımsız kanal satırı;
    /// ERP satırıyla kalıcı bağı yok). Türev SelectedLines kaynakları ERP satır Id'sinden klon ClientKey'ine çevrilir
    /// (tek geçiş; klonlar taze ClientKey). İlk açılışta ERP reçetesi = kanal reçetesi başlangıcı, sonra bağımsız.</summary>
    private static List<ProductRecipeLineGraphDto> CloneErpRecipeLines(List<ProductVariantRecipeLine> erpLines)
    {
        var ordered = erpLines.OrderBy(r => r.LineOrder).ThenBy(r => r.CreationTime).ToList();
        var dtos = ordered.Select(r => new ProductRecipeLineGraphDto
        {
            LineOrder = r.LineOrder,
            ComponentType = r.ComponentType,
            CommodityProcessType = r.CommodityProcessType,
            CommodityId = r.CommodityId,
            Quantity = r.Quantity,
            Amount = r.Amount,
            Factor = r.Factor,
            ValuationUnitId = r.ValuationUnitId,
            PaymentType = r.PaymentType,
            PayFactor = r.PayFactor,
            PayUnitId = r.PayUnitId,
            ManualAmount = r.ManualAmount ?? 0m,
            ManualUnitId = r.ManualUnitId,
            Description = r.Description,
            DerivedBaseMode = r.DerivedBaseMode,
            DerivedOperation = r.DerivedOperation,
            DerivedOperand = r.DerivedOperand,
            SideCostKind = r.SideCostKind,
        }).ToList();

        var clientKeyByErpId = new Dictionary<Guid, Guid>();
        for (var i = 0; i < ordered.Count; i++)
        {
            clientKeyByErpId[ordered[i].Id] = dtos[i].ClientKey;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var src = ordered[i];
            if (src.ComponentType == RecipeComponentType.Service
                && src.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines
                && !string.IsNullOrEmpty(src.DerivedSourceLineIds))
            {
                dtos[i].DerivedSourceKeys = src.DerivedSourceLineIds!
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => Guid.TryParse(part, out var eid) && clientKeyByErpId.TryGetValue(eid, out var ck)
                        ? ck
                        : (Guid?)null)
                    .Where(ck => ck.HasValue)
                    .Select(ck => ck!.Value)
                    .ToList();
            }
        }

        return dtos;
    }

    /// <summary>Varyant başına ERP option çiftleri (name/value) — ProductVariantAttributeValue → attribute adı + değer.
    /// Fırsatçı ERP eşleştirme indeksinin kaynağı (N11/Trendyol LoadVariantOptionsAsync paritesi).</summary>
    private async Task<Dictionary<Guid, List<(string Name, string Value)>>> LoadVariantOptionsAsync(Guid productId, List<Guid> variantIds)
    {
        var result = new Dictionary<Guid, List<(string Name, string Value)>>();
        if (variantIds.Count == 0)
        {
            return result;
        }

        var attributeNames = (await AsyncExecuter.ToListAsync(
                (await _attributeRepository.GetQueryableAsync())
                    .Where(a => a.EntityName == ProductEntityName && a.EntityId == productId)))
            .ToDictionary(a => a.Id, a => a.Name);
        if (attributeNames.Count == 0)
        {
            return result;   // niteliksiz ürün (tek varyant) → option attribute yok
        }

        var valueTexts = (await AsyncExecuter.ToListAsync(
                (await _attributeValueRepository.GetQueryableAsync())
                    .Where(v => attributeNames.Keys.Contains(v.EntityAttributeId))))
            .ToDictionary(v => v.Id, v => v.Value);

        var links = await AsyncExecuter.ToListAsync(
            (await _variantAttributeRepository.GetQueryableAsync())
                .Where(l => variantIds.Contains(l.EntityVariantId)));

        foreach (var link in links)
        {
            if (!attributeNames.TryGetValue(link.EntityAttributeId, out var name) ||
                !valueTexts.TryGetValue(link.EntityAttributeValueId, out var value))
            {
                continue;
            }

            if (!result.TryGetValue(link.EntityVariantId, out var list))
            {
                list = new List<(string Name, string Value)>();
                result[link.EntityVariantId] = list;
            }

            list.Add((name, value));
        }

        return result;
    }

    // ── Uygulama + güvenlik ─────────────────────────────────────────────────────────────────────────

    private void ApplyInput(SalesChannelEtsyProduct entity, ISalesChannelEtsyProductInput input)
    {
        entity.SetTaxonomy(input.TaxonomyId);
        entity.SetListingType(input.ListingType);
        entity.SetShippingProfile(input.ShippingProfileId);
        entity.SetReturnPolicy(input.ReturnPolicyId);
        entity.SetShopSection(input.ShopSectionId);
        entity.SetProcessing(input.ProcessingMin, input.ProcessingMax);
        entity.SetTitleOverride(input.TitleOverride);
        entity.SetDescriptionOverride(input.DescriptionOverride);
        entity.SetPersonalization(input.IsPersonalizable, input.PersonalizationInstructions,
            input.PersonalizationIsRequired, input.PersonalizationCharCountMax);
        entity.SetAutoRenew(input.ShouldAutoRenew);
        entity.SetPreparingDay(input.PreparingDay);
        entity.SetCurrencyUnit(input.CurrencyUnitId);
        entity.SetSellerNote(input.SellerNote);
        entity.SetActive(input.IsActive);
        entity.SetListingAttributes(input.ListingAttributes.Select(a => new SalesChannelEtsyProductListingAttribute(a.Name, a.Value)));
        entity.SetTags(input.Tags.Select(v => new SalesChannelEtsyProductTag(v)));
        entity.SetMaterials(input.Materials.Select(v => new SalesChannelEtsyProductMaterial(v)));
        entity.SetSpecialInfo(input.SpecialInfo.Select(s => new SalesChannelEtsyProductSpecialInfo(s.Key, s.Value)));
    }

    private async Task<SalesChannelEtsyProduct> GetOwnedAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.Id == id && x.CompanyId == companyId));
        if (entity is null)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:RecordNotFound");
        }

        return entity;
    }

    private async Task<SalesChannelEtsy> GetOwnedChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.Id == salesChannelId && x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ChannelNotFound");
        }

        return channel;
    }

    // Kanalın Etsy kimlik demetini (kanal id + x-api-key = {keystring}:{secret} + shopId) çözer — mağaza/kargo/iade/section
    // GET+write dilimlerinin ORTAK ön-adımı (DRY). Mağaza çözülmemişse dostane fail-fast (OAuth shopId'yi getirmediyse
    // shop-scoped uç çağrılamaz).
    private async Task<EtsyCredentials> ResolveEtsyCredentialsAsync(Guid salesChannelId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);
        if (string.IsNullOrWhiteSpace(channel.ShopId))
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ShopNotResolved");
        }

        return new EtsyCredentials(channel.Id, $"{channel.Keystring}:{channel.SharedSecret}", channel.ShopId!);
    }

    private async Task<Product> GetOwnedProductAsync(Guid productId)
    {
        var companyId = EnsureCurrentCompanyId();
        var product = await AsyncExecuter.FirstOrDefaultAsync(
            (await _productRepository.GetQueryableAsync()).Where(x => x.Id == productId && x.CompanyId == companyId));
        if (product is null)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ProductNotFound");
        }

        return product;
    }

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:CompanyRequired");
        }

        return companyId;
    }
}
