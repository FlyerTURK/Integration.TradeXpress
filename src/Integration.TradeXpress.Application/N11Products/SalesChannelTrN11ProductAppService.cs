using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Channels;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.SalesChannels.Variants;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 ürün listeleme CRUD + push — <b>company-owned + per-tenant</b>. Listeleme yapılandırması (kategori/attribute/
/// kargo şablonu/condition/özel bilgi) bizde tutulur; <see cref="PushToN11Async"/> ürünü + varyantlarını (stockItems)
/// + fiyat/stok/görselleriyle N11'e SaveProduct ile gönderir (kanalın KENDİ kimliğiyle) ve durumu işaretler.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelTrN11ProductAppService : TradeXpressAppService, ISalesChannelTrN11ProductAppService
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<EntityAttribute, Guid> _attributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _attributeValueRepository;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _variantAttributeRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _stockItemRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _channelRecipeLineRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _erpRecipeLineRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttribute, Guid> _channelAttributeRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttributeValue, Guid> _channelAttributeValueRepository;
    private readonly IRepository<N11Category, Guid> _n11CategoryRepository;
    private readonly IRepository<N11ShipmentTemplate, Guid> _n11ShipmentTemplateRepository;   // yalnız OKUMA — push ad çözümü (K8-Faz1)
    private readonly RecipeCostPopulator _recipeCostPopulator;
    private readonly SubstitutionChannelPlanProvider _substitutionPlanProvider;
    private readonly ICurrentCompany _currentCompany;
    private readonly IN11ProductClient _client;
    private readonly IPublicImageLinkProvider _publicImageLink;
    private readonly IN11CategoryClient _categoryClient;
    private readonly N11ProductPushValidator _pushValidator;
    private readonly IDistributedCache<N11LeafAttributes> _leafAttributeCache;
    private readonly IBlobContainer<ProductImagesContainer> _imageContainer;

    public SalesChannelTrN11ProductAppService(
        IRepository<SalesChannelTrN11Product, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> variantDetailRepository,
        IRepository<EntityAttribute, Guid> attributeRepository,
        IRepository<EntityAttributeValue, Guid> attributeValueRepository,
        IRepository<EntityVariantAttributeValue, Guid> variantAttributeRepository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IRepository<CurrencyUnit, Guid> currencyRepository,
        IRepository<SalesChannelTrN11ProductStockItem, Guid> stockItemRepository,
        IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> channelRecipeLineRepository,
        IRepository<ProductVariantRecipeLine, Guid> erpRecipeLineRepository,
        IRepository<SalesChannelTrN11ProductAttribute, Guid> channelAttributeRepository,
        IRepository<SalesChannelTrN11ProductAttributeValue, Guid> channelAttributeValueRepository,
        IRepository<N11Category, Guid> n11CategoryRepository,
        IRepository<N11ShipmentTemplate, Guid> n11ShipmentTemplateRepository,
        RecipeCostPopulator recipeCostPopulator,
        SubstitutionChannelPlanProvider substitutionPlanProvider,
        ICurrentCompany currentCompany,
        IN11ProductClient client,
        IPublicImageLinkProvider publicImageLink,
        IN11CategoryClient categoryClient,
        N11ProductPushValidator pushValidator,
        IDistributedCache<N11LeafAttributes> leafAttributeCache,
        IBlobContainer<ProductImagesContainer> imageContainer)
    {
        _repository = repository;
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _variantDetailRepository = variantDetailRepository;
        _attributeRepository = attributeRepository;
        _attributeValueRepository = attributeValueRepository;
        _variantAttributeRepository = variantAttributeRepository;
        _channelRepository = channelRepository;
        _currencyRepository = currencyRepository;
        _stockItemRepository = stockItemRepository;
        _channelRecipeLineRepository = channelRecipeLineRepository;
        _erpRecipeLineRepository = erpRecipeLineRepository;
        _channelAttributeRepository = channelAttributeRepository;
        _channelAttributeValueRepository = channelAttributeValueRepository;
        _n11CategoryRepository = n11CategoryRepository;
        _n11ShipmentTemplateRepository = n11ShipmentTemplateRepository;
        _recipeCostPopulator = recipeCostPopulator;
        _substitutionPlanProvider = substitutionPlanProvider;
        _currentCompany = currentCompany;
        _client = client;
        _publicImageLink = publicImageLink;
        _categoryClient = categoryClient;
        _pushValidator = pushValidator;
        _leafAttributeCache = leafAttributeCache;
        _imageContainer = imageContainer;
    }

    public virtual async Task<List<SalesChannelTrN11ProductDto>> GetListForProductAsync(Guid productId)
    {
        var companyId = EnsureCurrentCompanyId();

        // Yalnız CANLI kanalların kayıtları — soft-delete edilmiş kanalın yetim kayıtları drill'e sızmasın
        // (kanal kolonu boş/ham GUID görünür + push ChannelNotFound verirdi).
        var liveChannelIds = await AsyncExecuter.ToListAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.Id));

        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.ProductId == productId && liveChannelIds.Contains(x.SalesChannelId))
                .OrderBy(x => x.CategoryName));

        var dtos = new List<SalesChannelTrN11ProductDto>(items.Count);
        foreach (var item in items)
        {
            var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(item);
            await PopulateStockItemGraphAsync(item, dto);
            dtos.Add(dto);
        }

        return dtos;
    }

    public virtual async Task<SalesChannelTrN11ProductDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Okuma tarafı dispatch: özellik modu AKTİFSE (en az 1 persist edilmiş özellik) kartezyen kombinasyon
    /// grafı, DEĞİLSE legacy ERP-doğrudan graf doldurulur. Özellik modu HİÇ aktive edilmemişse (persist edilmiş özellik
    /// yok) klon-sonra-ayrış TETİKLENİR: ERP ProductAttribute/Value'lardan bir TASLAK özellik grafı üretilir (Id boş
    /// = henüz persist YOK) — kullanıcı düzenleyip Kaydet'e bastığında SaveAttributesGraphAsync bunu kalıcılaştırır.
    /// Salt-okuma çağrılarında (ör. drill listesi) DB'ye YAZILMAZ; klon yalnız kullanıcı save'inde gerçekleşir.</summary>
    private async Task PopulateStockItemGraphAsync(SalesChannelTrN11Product entity, SalesChannelTrN11ProductDto dto)
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
    private async Task<List<SalesChannelTrN11ProductAttributeDto>> BuildDraftAttributesFromErpAsync(Guid productId)
    {
        var attributes = await AsyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.EntityName == ProductEntityName && a.EntityId == productId)
                .OrderBy(a => a.DisplayOrder));
        if (attributes.Count == 0)
        {
            return new List<SalesChannelTrN11ProductAttributeDto>();
        }

        var attributeIds = attributes.Select(a => a.Id).ToList();
        var values = await AsyncExecuter.ToListAsync(
            (await _attributeValueRepository.GetQueryableAsync())
                .Where(v => attributeIds.Contains(v.EntityAttributeId))
                .OrderBy(v => v.DisplayOrder));
        var valuesByAttribute = values.GroupBy(v => v.EntityAttributeId).ToDictionary(g => g.Key, g => g.ToList());

        return attributes.Select(a => new SalesChannelTrN11ProductAttributeDto
        {
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<EntityAttributeValue>())
                .Select(v => new SalesChannelTrN11ProductAttributeValueDto
                {
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    public virtual async Task<List<SalesChannelTrN11ProductDto>> GetListForChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId)
                .OrderBy(x => x.CategoryName));
        return items.Select(x => ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(x)).ToList();
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<SalesChannelTrN11ProductDto> CreateAsync(SalesChannelTrN11ProductCreateDto input)
    {
        // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR (2026-07-07 kullanıcı kararı) — benzersizlik
        // kontrolü yok; her kayıt KENDİ SellerCode'uyla N11'de AYRI listeleme olur ("Farklı Code oluşturulur").
        // Kanal set-once: create'te belirlenir, sonra değiştirilemez (Update input'unda alan yok).
        var channel = await GetOwnedChannelAsync(input.SalesChannelId);
        var product = await GetOwnedProductAsync(input.ProductId);
        var sequenceNo = await NextSequenceNoAsync(channel.Id, product.Id);

        var entity = new SalesChannelTrN11Product(
            channel.CompanyId,
            channel.Id,
            input.ProductId,
            BuildSellerCode(product.Code, sequenceNo),
            sequenceNo,
            input.CategoryExternalId,
            input.ShipmentTemplateName,
            input.Condition);
        ApplyInput(entity, input);
        await _repository.InsertAsync(entity, autoSave: true);
        await SaveStockItemsAsync(entity, input.ProductAttributes, input.StockItems);

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Kayıt sırası: aynı ürün+kanal içindeki max SequenceNo + 1 — SİLİNMİŞLER DAHİL (soft-delete
    /// filtresi kapalı) ki silinen kaydın N11'de yaşamaya devam eden listelemesinin kodu yeniden üretilip EZİLMESİN.</summary>
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

    /// <summary>N11 upsert kimliği: "{ÜrünKodu}-{Sıra}" — kayıt-bazlı benzersiz + insan-okunur.</summary>
    private static string BuildSellerCode(string productCode, int sequenceNo)
    {
        return $"{productCode}-{sequenceNo}";
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrN11ProductDto> UpdateAsync(Guid id, SalesChannelTrN11ProductUpdateDto input)
    {
        var entity = await GetOwnedAsync(id);
        ApplyInput(entity, input);
        await _repository.UpdateAsync(entity, autoSave: true);
        await SaveStockItemsAsync(entity, input.ProductAttributes, input.StockItems);

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Yazma tarafı dispatch: özellik grafını persist eder + persist-sonrası özellik-modu AKTİFSE (en az 1
    /// özellik var) kartezyen reconcile + combo-satır override/reçete kaydı; DEĞİLSE legacy ERP-doğrudan override yolu.</summary>
    private async Task SaveStockItemsAsync(
        SalesChannelTrN11Product entity,
        List<SalesChannelTrN11ProductAttributeDto> attributesInput,
        List<SalesChannelTrN11ProductStockItemGraphDto> stockItemsInput)
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
        await _channelRecipeLineRepository.DeleteAsync(r => r.SalesChannelTrN11ProductId == entity.Id, autoSave: true);
        await _stockItemRepository.DeleteAsync(v => v.SalesChannelTrN11ProductId == entity.Id, autoSave: true);
        var channelAttributeIds = await AsyncExecuter.ToListAsync(
            (await _channelAttributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelTrN11ProductId == entity.Id)
                .Select(a => a.Id));
        if (channelAttributeIds.Count > 0)
        {
            await _channelAttributeValueRepository.DeleteAsync(v => channelAttributeIds.Contains(v.AttributeId), autoSave: true);
            await _channelAttributeRepository.DeleteAsync(a => a.SalesChannelTrN11ProductId == entity.Id, autoSave: true);
        }

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Özellik/değer grafını PERSIST EDER + kartezyen reconcile'ı hemen tetikler — TÜM ürünü kaydetmeden
    /// yalnız bu N11 kaydının kombinasyon setini yeniler. Full Update ile aynı reconcile mekanizmasını kullanır
    /// (<see cref="SaveAttributesAndReconcileAsync"/>).</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrN11ProductDto> RegenerateStockItemsAsync(Guid id, List<SalesChannelTrN11ProductAttributeDto> productAttributes)
    {
        var entity = await GetOwnedAsync(id);
        await SaveAttributesAndReconcileAsync(entity, productAttributes);

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Muadil M4 köprüsü — Top-N BAŞARILI kombinasyonu bu N11 ürününün StockItem'larına dönüştürür.
    /// Zincir (bağlayıcı karar 1): hesap TEK motordan koşulur → saf plan (<see cref="SubstitutionStockItemPlanner"/>)
    /// → "Kombinasyon" ÖZELLİĞİ + kombinasyon-başına DEĞER → MEVCUT kartezyen reconcile yolu
    /// (<see cref="SaveAttributesAndReconcileAsync"/> — paralel kayıt yolu YOK) StockItem'ları üretir/korur/siler
    /// → her kombinasyon satırına REÇETE (metal satırları; fiyat ELLE YAZILMAZ, maliyet zincirinden türer) +
    /// OverrideStock = paket sayısı yazılır. Rank sırası = değer DisplayOrder'ı (ilk sıra = ANA varyant).
    /// Yeniden uygulama = reconcile: imzası korunan satırların id/override/marj'ı yaşar; kullanıcının elle
    /// eklediği DİĞER özellikler/değerlere DOKUNULMAZ (yalnız "Kombinasyon" özelliği yönetilir).</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SubstitutionApplyResultDto> ApplySubstitutionAsync(Guid id, SubstitutionApplyInput input)
    {
        var entity = await GetOwnedAsync(id);

        // Orkestrasyon KANAL-AGNOSTİK gövdede (SubstitutionChannelPlanProvider.ApplyAsync — Trendyol ile TEK
        // akış); bu adaptör yalnız N11 graf tiplerini bağlar: özellik/değer okuma, upsert planı → N11 DTO
        // çevirisi + MEVCUT persist/reconcile yolu (SaveAttributesAndReconcileAsync) ve StockItem
        // paket stoğu + reçete yazımı (ReplaceChannelRecipeLinesAsync).
        // Yan-maliyet planı — Muadil'in yazdığı TAZE reçetelere de kanal giderleri eklenir (klon yoluyla hizalı).
        var sideCostPlan = await BuildSideCostPlanAsync(entity);

        return await _substitutionPlanProvider.ApplyAsync<SalesChannelTrN11ProductStockItem>(
            input,
            loadChannelAttributesAsync: async () =>
                (await LoadChannelAttributeEntitiesAsync(entity.Id))
                    .Select(a => new SubstitutionChannelAttributeRef(a.Id, a.Name, a.DisplayOrder))
                    .ToList(),
            loadCombinationValuesAsync: async attributeId =>
                (await LoadChannelAttributeValueEntitiesAsync(new List<Guid> { attributeId }))
                    .Select(v => (v.Id, v.Value))
                    .ToList(),
            persistAndReconcileAsync: async upsert =>
            {
                var attributeInput = ToCombinationAttributeDto(upsert);
                await SaveAttributesAndReconcileAsync(entity, new List<SalesChannelTrN11ProductAttributeDto> { attributeInput });

                // Upsert sonrası geri yazılmış GERÇEK id'ler — girdi sırası korunur (binding i ↔ ValueIds[i]).
                return (attributeInput.Id, attributeInput.Values.Where(v => !v.IsDeleted).Select(v => v.Id).ToList());
            },
            loadCombinationHeadersAsync: async () => await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrN11ProductId == entity.Id && h.CombinationSignature != null)),
            signatureOf: h => h.CombinationSignature!,
            applyCombinationToHeaderAsync: async (header, packageCount, recipeLines) =>
            {
                header.SetOverrideStock(packageCount);
                await _stockItemRepository.UpdateAsync(header, autoSave: true);
                SideCostRecipeComposer.EnsureLines(
                    recipeLines, sideCostPlan with { VariantOptInEnabled = header.InsuredShippingEnabled });
                await ReplaceChannelRecipeLinesAsync(entity, header.Id, recipeLines);
            });
    }

    /// <summary>Kanal-nötr upsert planı → N11 attribute DTO'su. Silinen değerde yalnız Id + IsDeleted taşınır
    /// (mevcut davranışla birebir — SaveAttributesGraphAsync silme dalı yalnız Id'ye bakar).</summary>
    private static SalesChannelTrN11ProductAttributeDto ToCombinationAttributeDto(SubstitutionCombinationAttributeUpsert upsert)
    {
        return new SalesChannelTrN11ProductAttributeDto
        {
            Id           = upsert.AttributeId,
            Name         = upsert.Name,
            DisplayOrder = upsert.DisplayOrder,
            Values = upsert.Values
                .Select(v => v.IsDeleted
                    ? new SalesChannelTrN11ProductAttributeValueDto { Id = v.Id, IsDeleted = true }
                    : new SalesChannelTrN11ProductAttributeValueDto { Id = v.Id, Value = v.ValueText, DisplayOrder = v.DisplayOrder })
                .ToList(),
        };
    }

    /// <summary>Köprü, kombinasyon StockItem REÇETESİNİN sahibidir: mevcut satırlar silinir + plan satırları yazılır.
    /// Persist mekaniği MEVCUT <see cref="SaveChannelRecipeLinesAsync"/> (paralel kayıt yolu açılmaz).</summary>
    private async Task ReplaceChannelRecipeLinesAsync(
        SalesChannelTrN11Product channelProduct, Guid stockItemId, List<ProductRecipeLineGraphDto> freshLines)
    {
        var existing = await AsyncExecuter.ToListAsync(
            (await _channelRecipeLineRepository.GetQueryableAsync())
                .Where(r => r.SalesChannelTrN11ProductId == channelProduct.Id && r.StockItemId == stockItemId));
        var lines = existing
            .Select(r => new ProductRecipeLineGraphDto { Id = r.Id, IsDeleted = true, ComponentType = r.ComponentType })
            .Concat(freshLines)
            .ToList();
        await SaveChannelRecipeLinesAsync(channelProduct, stockItemId, lines);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrN11ProductDto> PushToN11Async(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        var syncWarnings = new List<string>();

        try
        {
            // Veri kurulumu da try İÇİNDE: geçici-link (dış servis) hataları dahil her başarısızlık
            // MarkSyncFailed'e düşsün — kayıt bayat "Synced" göstermesin (review bulgusu).
            var plan = await BuildProductDataAsync(entity, channel);
            var data = plan.Data;
            var result = await _client.SaveProductAsync(data, channel.AppKey, channel.AppSecret);

            // Push N11'e ULAŞTI → SKU satırları ŞİMDİ kalıcılaşır (kod donması yalnız başarılı push'ta) ve
            // SKU-başına gönderilen adet/fiyat + seçenek snapshot'ı (Faz 2 dirty-tracking + sipariş→varyant
            // eşleme temeli) ile yanıttaki SKU kimlikleri (id/version) kaydedilir.
            entity.ReconcileSkus(plan.Candidates);
            foreach (var item in data.StockItems)
            {
                entity.RecordSkuPush(
                    item.SellerStockCode,
                    item.Quantity,
                    item.OptionPrice,
                    item.Attributes.Select(a => new SalesChannelTrN11ProductCategoryAttribute(a.Name, a.Value)));
            }

            foreach (var sku in result.Skus)
            {
                entity.ApplySkuIdentity(sku.SellerStockCode, sku.N11SkuId, sku.Version);
            }

            // N11 kuralları KENDİ tarafında oynatabilir (2026-07-07 kararı): push sonrası ürün N11'den geri
            // okunur, SpecialInfo HARİÇ alanlar N11 GERÇEĞİYLE eşlenir; kritik fark (kategori) kullanıcıya bildirilir.
            var saleStatus = result.SaleStatus;
            var approvalStatus = result.ApprovalStatus;
            if (result.N11ProductId is { } n11Id)
            {
                try
                {
                    var detail = await _client.GetProductAsync(n11Id, channel.AppKey, channel.AppSecret);
                    ApplyN11Truth(entity, detail, syncWarnings);
                    saleStatus = detail.SaleStatus ?? saleStatus;
                    approvalStatus = detail.ApprovalStatus ?? approvalStatus;
                }
                catch (Exception pullException)
                {
                    // Push BAŞARILI; doğrulama okuması düştü → geri alınamaz, yalnız uyar (eşitleme bir sonraki push'ta).
                    // Kök neden server logunda kalsın — sessiz yutma yasak (CLAUDE.md §2, review bulgusu 2026-07-07).
                    Logger.LogWarning(
                        pullException,
                        "N11 push sonrası doğrulama okuması başarısız (N11ProductId {N11ProductId}, kayıt {Id}).",
                        n11Id,
                        entity.Id);
                    syncWarnings.Add(L["N11Product:PullFailed"]);
                }
            }

            entity.MarkSynced(result.N11ProductId, saleStatus, approvalStatus, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // Hatayı kaydet (kullanıcı görsün) + yeniden fırlat (toast). Gizleme YOK — kayıt + propagate.
            entity.MarkSyncFailed(FriendlyError(ex), Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        dto.SyncWarnings = syncWarnings;
        return dto;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrN11ProductDto> SyncStockAndPriceAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        var syncWarnings = new List<string>();

        if (entity.N11ProductId is not { } n11ProductId)
        {
            // Hiç tam gönderim yapılmamış → UpdateProductBasic'in adresleyeceği N11 ürünü/SKU'su yok.
            throw new BusinessException("TradeXpress:N11:Product:NotPushedYet");
        }

        try
        {
            var product = await GetOwnedProductAsync(entity.ProductId);

            // Aday seti + fiyat/stok zinciri TAM PUSH ile birebir aynı kaynaktan (BuildPushRowsAsync): axis-modu
            // aktifse kombinasyon satırları (ERP-backed + N11-only), değilse legacy ERP varyantları. Aksi halde
            // hafif senkron ERP ham fiyatını gönderip full push'un yazdığı kanal fiyatını EZER + her turda dirty görünürdü.
            var rows = (await BuildPushRowsAsync(entity)).Rows;
            EnsurePushRowsPriced(rows);

            // Önce N11'den oku: eksik SKU id'lerini doldur + version drift'ini gör (UpdateProductBasic version almaz →
            // lost-update'i "oku-karşılaştır-yaz" disipliniyle yönet). Okuma düşerse senkron güvenli şekilde durur.
            var detail = await _client.GetProductBySellerCodeAsync(entity.SellerCode, channel.AppKey, channel.AppSecret);
            foreach (var sku in detail.Skus)
            {
                var localVersion = entity.Skus.FirstOrDefault(s => string.Equals(s.SellerStockCode, sku.SellerStockCode, StringComparison.OrdinalIgnoreCase))?.N11Version;
                if (localVersion is { } lv && sku.Version is { } rv && lv != rv)
                {
                    // Version değişti = N11'de satış/değişiklik oldu; yine yazarız (ERP otorite) ama kullanıcı bilsin.
                    syncWarnings.Add(L["N11Product:VersionDrift", sku.SellerStockCode]);
                }

                entity.ApplySkuIdentity(sku.SellerStockCode, sku.N11SkuId, sku.Version);
            }

            // Değişen adayları (dirty) belirle: SKU satırı olan + N11 SKU id'si bilinen + adet/fiyatı sapmış.
            var stockItems = new List<N11ProductBasicStockItem>();
            var anyDirty = false;
            foreach (var row in rows)
            {
                // Kombinasyon kimliği: ERP-backed satırda ProductVariant.Id, N11-only satırda StockItem.Id (J3).
                var sku = entity.Skus.FirstOrDefault(s => s.ProductVariantId == row.CandidateId);
                if (sku is null || sku.N11SkuId is not { } n11SkuId)
                {
                    // Bu aday hiç push edilmemiş / SKU id'si yok → hafif senkron adresleyemez; tam push gerekir.
                    syncWarnings.Add(L["N11Product:SkuNotPushed", row.Code]);
                    continue;
                }

                var dirty = sku.LastSentQuantity != row.Stock || sku.LastSentOptionPrice != row.Price;
                anyDirty |= dirty;

                // Merge/replace belirsizliğinden (rapor A3) kaçınmak için TÜM bilinen SKU'ları güncel değerleriyle
                // gönderiyoruz — gönderilmeyen SKU'nun N11'de sıfırlanma riski olmasın.
                stockItems.Add(new N11ProductBasicStockItem(
                    sku.SellerStockCode,
                    n11SkuId,
                    row.Stock,
                    row.Price));
            }

            if (stockItems.Count == 0)
            {
                throw new BusinessException("TradeXpress:N11:Product:NoSyncableSku");
            }

            if (!anyDirty)
            {
                // Değişiklik yok → N11'e gereksiz yazma yapma (60 sn kuralına + kotaya saygı).
                syncWarnings.Add(L["N11Product:NoChangesToSync"]);
            }
            else
            {
                var update = new N11ProductBasicUpdate(
                    n11ProductId,
                    entity.SellerCode,
                    rows[0].Price,   // ilk (ana) adayın efektif base fiyatı (override zinciri) — full push ile hizalı
                    product.Description ?? product.Name,
                    stockItems,
                    BuildSellerDiscount(product));
                var result = await _client.UpdateProductBasicAsync(update, channel.AppKey, channel.AppSecret);

                // Başarılı yazım → LastSent* + yanıttaki version güncellenir (dirty-tracking bir sonraki tur için).
                var versionByCode = result.Skus.ToDictionary(s => s.SellerStockCode, s => s.Version, StringComparer.OrdinalIgnoreCase);
                foreach (var item in stockItems)
                {
                    versionByCode.TryGetValue(item.SellerStockCode, out var version);
                    entity.RecordStockPriceSync(item.SellerStockCode, item.Quantity ?? 0, item.OptionPrice, version);
                }

                entity.MarkSynced(n11ProductId, entity.SaleStatus, entity.ApprovalStatus, Clock.Now.ToUniversalTime());
            }

            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // N11'e GİRMİŞ her başarısızlık (client notFound/SaveFailed/SaveRejected + ağ hataları) kayda geçer —
            // Push ile simetrik. Ön-uçuş guard'ları (NotPushedYet) try'dan ÖNCE fırladığından buraya düşmez;
            // NoSyncableSku düşerse de "senkronlanamadı" işareti bilgilendiricidir.
            entity.MarkSyncFailed(FriendlyError(ex), Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        dto.SyncWarnings = syncWarnings;
        return dto;
    }

    /// <summary>Başarısız push/sync hatasını LastError'a DOSTANE yazar: in-process BusinessException'ın ham
    /// mesajı "ABP Exception was thrown" olduğundan, Code'u sunucu-tarafı lokalize eder + {Key} placeholder'larını
    /// exception Data'sıyla doldurur. BusinessException değilse ham mesaj (ağ/altyapı hatası zaten okunabilir).</summary>
    private string FriendlyError(Exception ex)
    {
        if (ex is not BusinessException { Code: { Length: > 0 } code })
        {
            return ex.Message;
        }

        var message = L[code].Value;
        foreach (System.Collections.DictionaryEntry entry in ex.Data)
        {
            message = message.Replace($"{{{entry.Key}}}", entry.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        return message;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<N11PushPreviewDto> GetPushPreviewAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var product = await GetOwnedProductAsync(entity.ProductId);

        // Özellik modu AKTİFSE push aday seti kombinasyon satırlarından gelir (SaveProduct ile AYNI kaynak) —
        // N11-only satırlar da önizlemeye girer (kaynak rozeti IsErpBacked=false; fiyat/stok override zincirinden,
        // çözülemiyorsa boş görünür — önizleme fail-fast ETMEZ, eksiği göstermek bilgilendiricidir).
        List<N11PreviewVariantDto> previewVariants;
        var attributeModeActive = (await LoadChannelAttributeEntitiesAsync(entity.Id)).Count > 0;
        if (attributeModeActive)
        {
            var rows = (await BuildPushRowsAsync(entity)).Rows;
            previewVariants = rows.Select(r => new N11PreviewVariantDto
            {
                Code = r.Code,
                Name = r.DisplayName,
                StockQuantity = r.Stock ?? 0,
                SalePrice = r.Price,
                Options = string.Join("; ", r.Attributes.Select(a => $"{a.Name}: {a.Value}")),
                IsErpBacked = r.IsErpBacked,
            }).ToList();
        }
        else
        {
            // Legacy ERP-doğrudan görünüm — push'la AYNI filtre/sıra (aktif + fiyatlı + IsMain önce), ham ERP değerleri.
            // Satış fiyatı artık ProductVariantDetail'de (agnostik EntityVariant'ın Product uzantısı) → EntityVariantId ile batch yüklenir.
            var activeVariants = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id && v.IsActive));
            var salePrices = await LoadVariantSalePricesAsync(activeVariants.Select(v => v.Id).ToList());
            var variants = activeVariants
                .Where(v => salePrices.GetValueOrDefault(v.Id).SalePrice is not null)
                .OrderByDescending(v => v.IsMain)
                .ToList();

            var options = await LoadVariantOptionsAsync(product.Id, variants.Select(v => v.Id).ToList());
            previewVariants = variants.Select(v => new N11PreviewVariantDto
            {
                Code = v.Code,
                Name = v.Name,
                StockQuantity = v.StockQuantity,
                SalePrice = salePrices.GetValueOrDefault(v.Id).SalePrice,
                Options = options.TryGetValue(v.Id, out var pairs)
                    ? string.Join("; ", pairs.Select(p => $"{p.Name}: {p.Value}"))
                    : string.Empty,
            }).ToList();
        }

        return new N11PushPreviewDto
        {
            Variants = previewVariants,
            Images = await BuildPreviewImagesAsync(product),
        };
    }

    // Push'ta gidecek görseller (VARSAYILAN önce, sonra DisplayOrder — SaveProduct ile aynı sıra). Yüklenmiş
    // (blob) görsel için thumbnail data-URL'i; URL kaynaklı görselde dış link olduğundan önizleme resmi yok.
    private async Task<List<N11PreviewImageDto>> BuildPreviewImagesAsync(Product product)
    {
        var images = new List<N11PreviewImageDto>();
        foreach (var image in product.Images.OrderByDescending(i => i.IsDefault).ThenBy(i => i.DisplayOrder))
        {
            string? previewDataUrl = null;
            if (image.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(image.BlobName))
            {
                var thumbnail = await _imageContainer.GetAllBytesOrNullAsync(ProductImageAppService.ThumbnailNameOf(image.BlobName!));
                if (thumbnail is not null)
                {
                    previewDataUrl = ProductImageAppService.BuildPreviewDataUrl(thumbnail);
                }
            }

            images.Add(new N11PreviewImageDto
            {
                Source = image.SourceType == ProductImageSourceType.Url ? (image.Url ?? string.Empty) : (image.FileName ?? image.BlobName ?? string.Empty),
                IsDefault = image.IsDefault,
                PreviewDataUrl = previewDataUrl,
            });
        }

        return images;
    }

    /// <summary>N11'in döndürdüğü ürün gerçeğini yerel kayda uygular — <b>SpecialInfo HARİÇ</b> (2026-07-07 kararı:
    /// N11 kuralları kendi tarafında oynatır; yerel kayıt yayın kopyasıdır). Yanıtta OLMAYAN alana dokunulmaz
    /// (N11'in desteklemediği alan yerel değeri silmesin). Kategori değişimi KRİTİK → kullanıcı uyarısı.</summary>
    private void ApplyN11Truth(SalesChannelTrN11Product entity, N11ProductDetail detail, List<string> syncWarnings)
    {
        // DIŞ girdi (N11 yanıtı) entity guard'larına TAKILMAMALI: setter ortasında fırlayan istisna entity'yi
        // yarı-mutasyonlu bırakır ve MarkSynced o hâli persist eder → uzunluklar Set'ten ÖNCE kırpılır.
        if (detail.CategoryId is { Length: > 0 and <= N11ProductConsts.ExternalIdMaxLength } categoryId)
        {
            var previousName = entity.CategoryName ?? entity.CategoryExternalId;
            var categoryChanged = !string.Equals(categoryId, entity.CategoryExternalId, StringComparison.Ordinal);

            // Yanıtta ad yoksa: kategori AYNIYSA yerel ad korunur (olmayan alan silinmez); DEĞİŞTİYSE eski ad
            // artık yanlış olduğundan null'lanır (uyarıda yeni kimlik olarak id gösterilir).
            var incomingName = detail.CategoryName?.Truncate(N11ProductConsts.CategoryNameMaxLength)
                ?? (categoryChanged ? null : entity.CategoryName);
            entity.SetCategory(categoryId, incomingName);

            if (categoryChanged)
            {
                // KRİTİK: N11 ürünü farklı kategoriye/gruba taşıdı — güvenli bilgilendirme (eski → yeni),
                // eşitleme GERÇEKTEN uygulandıktan sonra (uyarı verilip uygulanamama çelişkisi olmasın).
                syncWarnings.Add(L["N11Product:CategoryChangedByN11", previousName, incomingName ?? categoryId]);
            }
        }

        if (detail.ShipmentTemplate is { Length: > 0 } shipmentTemplate)
        {
            entity.SetShipmentTemplate(shipmentTemplate.Truncate(N11ProductConsts.ShipmentTemplateNameMaxLength)!);
        }

        if (detail.ProductCondition is 1 or 2)
        {
            entity.SetCondition((N11ProductCondition)detail.ProductCondition.Value);
        }

        if (detail.PreparingDay is >= 1)
        {
            entity.SetPreparingDay(detail.PreparingDay.Value);
        }

        if (detail.MaxPurchaseQuantity is { } maxPurchase)
        {
            // N11 limiti kaldırdıysa (0/-1 dönebilir) yerel bayat limit sonraki push'ta geri yazılmasın → temizle.
            entity.SetMaxPurchaseQuantity(maxPurchase >= 1 ? maxPurchase : null);
        }

        // BOŞ blok "bilgi yok" sayılır (null gibi): push hemen ardından N11 attribute'ları henüz işlememişken
        // boş wrapper dönerse kullanıcının kategori attribute konfigürasyonu topluca silinmesin.
        if (detail.Attributes is { Count: > 0 })
        {
            entity.SetCategoryAttributes(detail.Attributes.Select(a => new SalesChannelTrN11ProductCategoryAttribute(
                a.Name.Truncate(N11ProductConsts.CategoryAttributeNameMaxLength)!,
                a.Value.Truncate(N11ProductConsts.CategoryAttributeValueMaxLength) ?? string.Empty)));
        }

        // Doğrulama okumasındaki SKU kimlikleri (id/version) push yanıtından TAZEDİR → üzerine yaz.
        foreach (var sku in detail.Skus)
        {
            entity.ApplySkuIdentity(sku.SellerStockCode, sku.N11SkuId, sku.Version);
        }
    }

    // ── Push veri kurulumu (ürün grafı → N11ProductData) ────────────────────────────────────────────

    private async Task<N11ProductPushPlan> BuildProductDataAsync(SalesChannelTrN11Product channelProduct, SalesChannelTrN11 channel)
    {
        var product = await GetOwnedProductAsync(channelProduct.ProductId);

        // Görsel sırası: VARSAYILAN önce, sonra DisplayOrder. URL-kaynaklılar doğrudan; yüklenmiş (blob)
        // görseller sağlayıcı yapılandırılmışsa GEÇİCİ dış linke çevrilir (N11 kendi sistemine import eder),
        // yapılandırılmamışsa atlanır (2026-07-07 kullanıcı kararı; anonim endpoint YOK).
        var imageUrls = new List<string>();
        foreach (var image in product.Images.OrderByDescending(i => i.IsDefault).ThenBy(i => i.DisplayOrder))
        {
            if (image.SourceType == ProductImageSourceType.Url && !string.IsNullOrWhiteSpace(image.Url))
            {
                imageUrls.Add(image.Url!);
            }
            else if (image.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(image.BlobName))
            {
                var link = await _publicImageLink.TryCreateTemporaryLinkAsync(image.BlobName!);
                if (link is not null)
                {
                    imageUrls.Add(link);
                }
            }
        }

        if (imageUrls.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:Product:ImagesRequired");
        }

        // Push aday satırları — axis-modu aktifse kombinasyon (StockItem) satırlarından (ERP-backed + N11-only),
        // değilse legacy ERP varyantlarından. Fiyat/stok zinciri satır içinde ÇÖZÜLMÜŞ gelir. En az 1 aday zorunlu.
        var rows = (await BuildPushRowsAsync(channelProduct)).Rows;
        if (rows.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:Product:NoPricedVariant");
        }

        // N11-only satırda ERP fallback YOK (zincir: OverridePrice ?? türetilmiş) — çözülemeyen fiyat/stok N11'e
        // gitmeden fail-fast (sessiz atlama = kapsam düşürme; kullanıcı override girip yeniden dener).
        EnsurePushRowsPriced(rows);

        // Tek para birimi zorunlu (N11 ürün başına tek currencyType). Kanal para birimi seçiliyse O belirler
        // → satırlar farklı birimde olsa da karışıklık yok (MixedCurrency yalnız kanal seçilmemişken denetlenir).
        var currencyUnitIds = rows.Select(r => r.PriceCurrencyUnitId).Where(x => x is not null).Distinct().ToList();
        // Kanal ya da ÜRÜN para birimi belirleyiciyse mixed-currency serbest (o birim tüm SKU'lara uygulanır);
        // yalnız ne kanalda ne üründe birim yokken satırlar farklı birimdeyse belirsizlik → fail-fast.
        if (channelProduct.CurrencyUnitId is null && product.CurrencyUnitId is null && currencyUnitIds.Count > 1)
        {
            throw new BusinessException("TradeXpress:N11:Product:MixedCurrency");
        }

        // Para birimi çözümü: kanal → ürün varsayılanı → satır (varyant/override) birimi (fallback zinciri).
        var currencyType = await ResolveCurrencyTypeAsync(
            channelProduct.CurrencyUnitId ?? product.CurrencyUnitId ?? currencyUnitIds.FirstOrDefault());

        // ── Faz 1: kategori-farkındalıklı validasyon — varyant EKSENLERİNİ kategori belirler (isVariant seti),
        // customValue=false değer listeden birebir, zorunlu eksen her SKU'da dolu; sapma FAIL-FAST.
        var leaf = await GetLeafAttributesCachedAsync(channelProduct.CategoryExternalId, channel);

        // Adaylar push satırlarından: ERP-backed satırda ERP varyant kimliği/kodu/nitelikleri, N11-only satırda
        // StockItem.Id + kombinasyon-türevli kod + kanal Attribute/Value adları — validator'a AYNI biçimde girer.
        var candidates = rows
            .Select(r => new N11SkuPushCandidate(r.CandidateId, r.Code, r.Attributes))
            .ToList();
        var validated = _pushValidator.Validate(leaf, channelProduct.CategoryAttributes, candidates);

        // Reconcile/imza adayları KANONİK değerlerle kurulur (validated) — RecordSkuPush snapshot'ı da kanonik
        // olduğundan, sonraki push'ta imza eşleşmesi ham/kanonik karışımından ETKİLENMEZ (review bulgusu).
        var canonicalCandidates = candidates
            .Select(c => new N11SkuPushCandidate(
                c.VariantId,
                c.VariantCode,
                (validated.VariantOptions.TryGetValue(c.VariantId, out var cp) ? cp : new List<N11ProductAttributePair>())
                    .Select(p => new SalesChannelTrN11ProductCategoryAttribute(p.Name, p.Value))
                    .ToList()))
            .ToList();

        // Stok kodları PLANLANIR (entity mutasyonu YOK): mevcut dondurulmuş satır kodu tercih edilir, yoksa üretilir.
        // Satırlar ancak BAŞARILI push sonrası ReconcileSkus ile kalıcılaşır — başarısız push bayat kod dondurmasın.
        var stockCodePlan = channelProduct.PlanStockCodes(canonicalCandidates);
        EnsureUniqueStockCodes(stockCodePlan);

        // stockItem'lar ADAY-bazlı: fiyat/stok/kimlik push satırından (ERP-backed: Override ?? türetilmiş ?? ERP;
        // N11-only: Override ?? türetilmiş), attribute'ler validasyondan geçen kanonik ad/değerler.
        var rowByCandidateId = rows.ToDictionary(r => r.CandidateId);
        var stockItems = canonicalCandidates.Select(c =>
        {
            var row = rowByCandidateId[c.VariantId];
            return new N11ProductStockItem(
                SellerStockCode: stockCodePlan[c.VariantId],
                Quantity: row.Stock!.Value,                    // EnsurePushRowsPriced garantisi — null olamaz
                OptionPrice: row.Price,
                Attributes: validated.VariantOptions.TryGetValue(c.VariantId, out var pairs) ? pairs : new List<N11ProductAttributePair>(),
                Gtin: row.Gtin,
                Mpn: row.Mpn,
                Oem: row.Oem);
        }).ToList();

        var images = imageUrls
            .Select((url, index) => new N11ProductImage(url, index + 1))
            .ToList();

        // K8-Faz1: kargo şablonu adı canlı-referans/FK-onarım zinciriyle çözülür (loose string tek başına kaynak değil).
        var shipmentTemplateName = await ResolvePushShipmentTemplateNameAsync(channelProduct, product);

        var data = new N11ProductData(
            ProductSellerCode: channelProduct.SellerCode,   // KAYIT-bazlı upsert kimliği — her kayıt N11'de AYRI listeleme
            Title: product.Name,
            Description: channelProduct.Description ?? product.Description ?? product.Name,   // kanal-özel açıklama önce, yoksa ürün
            Domestic: channelProduct.Domestic,
            CategoryId: channelProduct.CategoryExternalId,
            Price: rows[0].Price!.Value,   // ilk (ana) adayın efektif fiyatı = base (override zinciri)
            CurrencyType: currencyType,
            ProductCondition: (byte)channelProduct.Condition,
            PreparingDay: channelProduct.PreparingDay,
            ShipmentTemplate: shipmentTemplateName,
            // K4: listeleme kuralı — kanal override doluysa kanal, değilse ürün varsayılanı (merkezî K10 zinciri).
            MaxPurchaseQuantity: ChannelInheritance.Resolve(channelProduct.MaxPurchaseQuantity, product.MaxPurchaseQuantity),
            Images: images,
            Attributes: validated.ProductAttributes,       // varyant eksenleri FİLTRELİ + kanonik değerler
            StockItems: stockItems,
            // Kanal özel bilgisi boşsa ürün varsayılanı devralınır (her ikisi de key-zorunlu/value-opsiyonel).
            SpecialInfo: (channelProduct.SpecialInfo.Count > 0 ? channelProduct.SpecialInfo.Select(s => (s.Key, s.Value))
                             : product.SpecialInfo.Select(s => (s.Key, s.Value)))
                         .Select(s => new N11ProductSpecialInfo(s.Key, s.Value)).ToList(),
            Discount: BuildDiscount(product),              // ürün-seviyesi indirim (None ise null)
            SellerNote: channelProduct.SellerNote ?? product.SellerNote,   // kanal-özel not → ürün varsayılanı
            // Kanal-özel tarih önce, yoksa ürün tarihi devralınır ("dd/MM/yyyy"; boşsa boş → gönderilmez).
            ProductionDate: FormatN11Date(channelProduct.ProductionDate ?? product.ProductionDate),
            ExpirationDate: FormatN11Date(channelProduct.ExpirationDate ?? product.ExpirationDate),
            // Grup ürün (kanal-özel; N11-only): boşsa push'ta element gönderilmez.
            GroupItemCode: channelProduct.GroupItemCode,
            GroupAttribute: channelProduct.GroupAttribute,
            ItemName: channelProduct.ItemName);

        return new N11ProductPushPlan(data, canonicalCandidates);
    }

    /// <summary>Push planı — N11'e gidecek veri + BAŞARILI push sonrası SKU satırlarını kurmak için kanonik adaylar
    /// (kod donması yalnız başarılı push'ta gerçekleşsin diye ReconcileSkus çağrısı push sonrasına ertelenir).</summary>
    private sealed record N11ProductPushPlan(N11ProductData Data, List<N11SkuPushCandidate> Candidates);

    /// <summary>K8-Faz1: N11'e gidecek kargo şablonu adının okuma zinciri —
    /// (a) kanal-ürünün seçili adı YEREL N11 şablon aynasında hâlâ mevcutsa (canlı referans) AYNEN kullanılır
    ///     (kullanıcının kanal-seviyesi seçimi ezilmez — K10 "kanal-dolu-ise-kanal" deseni);
    /// (b) bayat/boşsa ürün FK zinciri ONARIR: <c>Product.ShipmentTemplateId</c> → K1 köprüsü
    ///     (<c>N11ShipmentTemplate.ShipmentTemplateId == çekirdek</c> + aynı kanal) → <c>TemplateName</c>
    ///     (LogWarning ile görünür onarım — sessiz kalmasın);
    /// (c) o da çözülmezse ham string olduğu gibi gider (mevcut davranış — kırmama garantisi; N11 kendi doğrular).
    /// Canlılık kontrolü YEREL aynaya karşı — push hazırlığına canlı N11 çağrısı EKLENMEZ. Kanal kolonunun id-only'ye
    /// çevrimi (N1) Faz-4 işi; bu zincir o güne kadar okuma tarafını FK-öncelikli tutar.</summary>
    private async Task<string> ResolvePushShipmentTemplateNameAsync(SalesChannelTrN11Product channelProduct, Product product)
    {
        // Ad kimliktir (N11'de ayrı şablon id'si yok) — trim'li karşılaştırma (NormalizeName deseni; ayna adları zaten trim'li).
        var storedName = channelProduct.ShipmentTemplateName?.Trim() ?? string.Empty;

        // (a) Seçili ad yerel aynada CANLI referans mı? (aynı kanal + birebir ad)
        if (storedName.Length > 0)
        {
            var isLive = await AsyncExecuter.AnyAsync(
                (await _n11ShipmentTemplateRepository.GetQueryableAsync())
                    .Where(t => t.SalesChannelId == channelProduct.SalesChannelId && t.TemplateName == storedName));
            if (isLive)
            {
                return storedName;
            }
        }

        // (b) FK onarım zinciri: ürünün çekirdek şablonu → K1 köprüsüyle BU kanala açılmış N11 şablonu.
        if (product.ShipmentTemplateId is { } coreTemplateId)
        {
            var repairedName = await AsyncExecuter.FirstOrDefaultAsync(
                (await _n11ShipmentTemplateRepository.GetQueryableAsync())
                    .Where(t => t.SalesChannelId == channelProduct.SalesChannelId && t.ShipmentTemplateId == coreTemplateId)
                    .OrderBy(t => t.TemplateName)   // birden çok köprü varsa deterministik seçim
                    .Select(t => t.TemplateName));
            if (!string.IsNullOrEmpty(repairedName))
            {
                Logger.LogWarning(
                    "N11 push: bayat kargo şablonu referansı FK'den onarıldı (kanal-ürün {ChannelProductId}: '{StaleName}' → '{RepairedName}').",
                    channelProduct.Id, storedName, repairedName);
                return repairedName;
            }
        }

        // (c) Çözülemedi — ham string mevcut davranışla gider.
        return channelProduct.ShipmentTemplateName;
    }

    // ── Push aday satırları (J3: N11-only kombinasyonlar da push edilir) ─────────────────────────────

    /// <summary>Push aday satırı — SaveProduct stockItem'ının çözülmüş hâli. <see cref="CandidateId"/> = kombinasyon
    /// kimliği (ERP-backed satırda ProductVariant.Id, N11-only satırda StockItem.Id); <see cref="Price"/>/<see cref="Stock"/>
    /// efektif zincir sonucu (null = çözülemedi — push fail-fast eder, önizleme boş gösterir).</summary>
    private sealed record N11PushRow(
        Guid CandidateId,
        string Code,
        string DisplayName,
        bool IsErpBacked,
        List<SalesChannelTrN11ProductCategoryAttribute> Attributes,
        decimal? Price,
        int? Stock,
        Guid? PriceCurrencyUnitId,
        string? Gtin,
        string? Mpn,
        string? Oem);

    /// <summary>Aday satır seti + hangi moddan üretildiği (axis-modu aktif mi).</summary>
    private sealed record N11PushRowSet(bool AttributeModeActive, List<N11PushRow> Rows);

    /// <summary>Push/senkron/önizleme aday satırlarını kurar (ORTAK kaynak — üçü de aynı seti görsün):
    /// <b>axis-modu AKTİFKEN</b> (en az 1 persist edilmiş kanal özelliği) kombinasyon seti StockItem satırlarından —
    /// ERP-backed satır (ProductVariantId dolu) legacy davranışla (aktif + fiyatlı ERP varyant kimliği/kodu/nitelikleri,
    /// zincir Override ?? türetilmiş ?? ERP), N11-only satır StockItem.Id kimliği + kombinasyon-türevli kod + kanal
    /// Attribute/Value adları (zincir Override ?? türetilmiş; ERP fallback YOK) ile. <b>Axis-modu PASİFKEN</b> legacy
    /// ERP-doğrudan aday seti AYNEN (regresyon sıfır).</summary>
    private async Task<N11PushRowSet> BuildPushRowsAsync(SalesChannelTrN11Product channelProduct)
    {
        // Aktif + fiyatlı ERP varyantları (IsMain önce) — legacy aday seti + axis-modda ERP-backed satır kaynağı.
        // Satış fiyatı ProductVariantDetail'de (agnostik EntityVariant Product uzantısı) → fiyatlı filtresi detail üzerinden.
        var activeVariants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == channelProduct.ProductId && v.IsActive));
        var salePrices = await LoadVariantSalePricesAsync(activeVariants.Select(v => v.Id).ToList());
        var variants = activeVariants
            .Where(v => salePrices.GetValueOrDefault(v.Id).SalePrice is not null)
            .OrderByDescending(v => v.IsMain)
            .ToList();

        var attributeEntities = await LoadChannelAttributeEntitiesAsync(channelProduct.Id);
        if (attributeEntities.Count == 0)
        {
            // Legacy ERP-doğrudan yol (özellik modu hiç aktive edilmemiş) — J3 öncesi davranış AYNEN.
            var pushPricing = await ResolveVariantPushPricingAsync(channelProduct, variants);
            var variantOptions = await LoadVariantOptionsAsync(channelProduct.ProductId, variants.Select(v => v.Id).ToList());
            return new N11PushRowSet(
                false,
                variants.Select(v => BuildErpRow(v, pushPricing[v.Id], variantOptions)).ToList());
        }

        // Axis-modu: kombinasyon seti = imzalı StockItem satırları (SSOT kanal; reconcile üretti).
        var channelAttributeValues = await LoadChannelAttributeValueEntitiesAsync(attributeEntities.Select(a => a.Id).ToList());
        var attributeById = ToAttributeWithValues(attributeEntities, channelAttributeValues).ToDictionary(a => a.AttributeId);
        var headers = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrN11ProductId == channelProduct.Id && h.CombinationSignature != null)
                .OrderBy(h => h.CreationTime));

        // ERP-backed satırlar: varyantı hâlâ aktif + fiyatlı olanlar (legacy eleme semantiği). Aynı varyanta bağlı
        // mükerrer başlık (teorik) tek satıra iner — aday sözlükleri VariantId ile kurulur, çakışma fail üretmesin.
        var variantById = variants.ToDictionary(v => v.Id);
        var erpVariants = headers
            .Where(h => h.ProductVariantId is { } vid && variantById.ContainsKey(vid))
            .Select(h => variantById[h.ProductVariantId!.Value])
            .DistinctBy(v => v.Id)
            .OrderByDescending(v => v.IsMain)
            .ToList();
        var erpPricing = await ResolveVariantPushPricingAsync(channelProduct, erpVariants);
        var erpOptions = await LoadVariantOptionsAsync(channelProduct.ProductId, erpVariants.Select(v => v.Id).ToList());

        var rows = erpVariants.Select(v => BuildErpRow(v, erpPricing[v.Id], erpOptions)).ToList();

        // N11-only satırlar: kimlik StockItem.Id; nitelikler imzadan kanal Attribute.Name/AttributeValue.Value'a çözülür.
        var n11OnlyHeaders = headers.Where(h => h.ProductVariantId is null).ToList();
        var n11OnlyPricing = await ResolveN11OnlyPushPricingAsync(channelProduct, n11OnlyHeaders);
        foreach (var header in n11OnlyHeaders)
        {
            var pairs = ResolveCombinationPairs(header.CombinationSignature!, attributeById);
            var pricing = n11OnlyPricing[header.Id];
            rows.Add(new N11PushRow(
                CandidateId: header.Id,
                Code: BuildCombinationCode(pairs, channelProduct.SequenceNo),
                DisplayName: BuildLabel(pairs),
                IsErpBacked: false,
                Attributes: pairs
                    .Select(p => new SalesChannelTrN11ProductCategoryAttribute(p.Name, p.Value ?? string.Empty))
                    .ToList(),
                Price: pricing.Price,
                Stock: pricing.Stock,
                PriceCurrencyUnitId: header.OverridePriceCurrencyUnitId,
                Gtin: null,
                Mpn: null,
                Oem: null));
        }

        return new N11PushRowSet(true, rows);
    }

    /// <summary>ERP-backed push satırı — legacy davranış: kimlik/kod/ad/ticari kimlikler ERP varyantından,
    /// fiyat/stok zinciri Override ?? türetilmiş ?? ERP (<see cref="ResolveVariantPushPricingAsync"/> sonucu).</summary>
    private static N11PushRow BuildErpRow(
        EntityVariant variant, VariantPushPricing pricing, Dictionary<Guid, List<N11ProductAttributePair>> variantOptions)
    {
        return new N11PushRow(
            CandidateId: variant.Id,
            Code: variant.Code,
            DisplayName: variant.Name,
            IsErpBacked: true,
            Attributes: (variantOptions.TryGetValue(variant.Id, out var opts) ? opts : new List<N11ProductAttributePair>())
                .Select(p => new SalesChannelTrN11ProductCategoryAttribute(p.Name, p.Value))
                .ToList(),
            Price: pricing.Price,
            Stock: pricing.Stock,
            PriceCurrencyUnitId: pricing.PriceCurrencyUnitId,   // satış fiyatı birimi artık ProductVariantDetail'den (pricing içinde çözülür)
            Gtin: variant.Gtin,
            Mpn: variant.Mpn,
            Oem: variant.Oem);
    }

    /// <summary>N11-only (ERP karşılıksız) kombinasyon satırlarının push fiyat/stok'u — zincir: OverridePrice ??
    /// türetilmiş (kaydedilmiş reçete NetCost × (1+Margin/100)); stok: OverrideStock (ERP fallback YOK). Çözülemeyen
    /// değer null döner — fail-fast kararı çağıranda (push <see cref="EnsurePushRowsPriced"/>; önizleme boş gösterir).</summary>
    private async Task<IReadOnlyDictionary<Guid, VariantPushPricingNullable>> ResolveN11OnlyPushPricingAsync(
        SalesChannelTrN11Product channelProduct, List<SalesChannelTrN11ProductStockItem> headers)
    {
        var result = new Dictionary<Guid, VariantPushPricingNullable>(headers.Count);
        if (headers.Count == 0)
        {
            return result;
        }

        var headerIds = headers.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrN11ProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lineSets = headers
            .Select(h => savedByHeader.TryGetValue(h.Id, out var lines)
                ? MapSavedRecipeLines(lines)
                : new List<ProductRecipeLineGraphDto>())
            .ToList();
        var costs = await _recipeCostPopulator.PopulateAsync(lineSets);

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            decimal? derived = costs[i].NetCost is { } nc && !costs[i].NetCostMissingRate
                ? DerivedPriceCalculator.Calculate(nc, header.Margin)
                : null;
            result[header.Id] = new VariantPushPricingNullable(header.OverridePrice ?? derived, header.OverrideStock);
        }

        return result;
    }

    /// <summary>N11-only satırın efektif fiyat/stok'u — İKİSİ de nullable (ERP fallback yok; çözüm çağıranda).</summary>
    private sealed record VariantPushPricingNullable(decimal? Price, int? Stock);

    /// <summary>Push'a girecek her satırın efektif fiyat+stok'unun ÇÖZÜLMÜŞ olduğunu doğrular — N11-only satırda
    /// ERP fallback yoktur (zincir: OverridePrice ?? türetilmiş / OverrideStock); çözülemiyorsa N11'e gitmeden fail-fast.</summary>
    private static void EnsurePushRowsPriced(List<N11PushRow> rows)
    {
        var unpriced = rows.FirstOrDefault(r => r.Price is null || r.Stock is null);
        if (unpriced is not null)
        {
            throw new BusinessException("TradeXpress:N11:StockItem:PriceMissingForPush")
                .WithData("Combination", unpriced.DisplayName);
        }
    }

    /// <summary>Planlanan stok kodlarının benzersizliğini doğrular — ERP varyant kodu ile N11-only kombinasyon-türevli
    /// kod teorik olarak çakışabilir (aynı değer adları); N11 aynı sellerStockCode'lu iki SKU'da tanımsız davranır → fail-fast.</summary>
    private static void EnsureUniqueStockCodes(IReadOnlyDictionary<Guid, string> stockCodePlan)
    {
        var duplicate = stockCodePlan.Values
            .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessException("TradeXpress:N11:Product:DuplicateStockCode")
                .WithData("StockCode", duplicate.Key);
        }
    }

    /// <summary>N11-only kombinasyon satırının stok kodu GÖVDESİ — değer adlarından türetilir (ERP
    /// <c>ProductVariantSynchronizer.BuildVariantCode</c> deseni: "SIYAH-42"); "-{SequenceNo}" sonekini entity
    /// <see cref="SalesChannelTrN11Product.BuildStockCode"/> ekler. Sonek payı düşülerek
    /// <see cref="N11ProductConsts.StockCodeMaxLength"/>'e kesilir (deterministik — aynı kombinasyon hep aynı kod).</summary>
    private static string BuildCombinationCode(List<(string Name, string? Value)> pairs, int sequenceNo)
    {
        var joined = string.Join("-", pairs.Select(p => p.Value)).ToUpperInvariant();
        var suffixLength = sequenceNo.ToString(CultureInfo.InvariantCulture).Length + 1;   // "-{SequenceNo}"
        var maxLength = N11ProductConsts.StockCodeMaxLength - suffixLength;
        return joined.Length <= maxLength ? joined : joined[..maxLength];
    }

    /// <summary>Kombinasyon çiftlerinin insan-okunur etiketi ("Renk: Kırmızı; Beden: M").</summary>
    private static string BuildLabel(List<(string Name, string? Value)> pairs)
    {
        return string.Join("; ", pairs.Select(p => $"{p.Name}: {p.Value}"));
    }

    // Ürün-seviyesi indirimi N11 ProductDiscountRequest'e çevirir (SaveProduct; None → null → elementi gitmez).
    // N11 type: Amount="1", Percentage="2" (canlı doğrulanacak). Tarih N11 formatı "dd/MM/yyyy"; yoksa boş.
    private static N11ProductDiscount? BuildDiscount(Product product)
    {
        if (product.DiscountType == ProductDiscountType.None || product.DiscountValue is not { } value)
        {
            return null;
        }

        return new N11ProductDiscount(
            DiscountTypeCode(product.DiscountType),
            value.ToString(CultureInfo.InvariantCulture),
            FormatN11Date(product.DiscountStartDate),
            FormatN11Date(product.DiscountEndDate));
    }

    // UpdateProductBasic indirimi (SellerProductDiscount; ZORUNLU alan). None → Type=0/Value=0 (yine gönderilir,
    // yoksa sabit-0 N11'deki indirimi silerdi). Aynı type/tarih dönüşümü SaveProduct ile paylaşılır (SSOT).
    private static N11SellerDiscount BuildSellerDiscount(Product product)
    {
        if (product.DiscountType == ProductDiscountType.None || product.DiscountValue is not { } value)
        {
            return new N11SellerDiscount(0, 0m, string.Empty, string.Empty);
        }

        return new N11SellerDiscount(
            int.Parse(DiscountTypeCode(product.DiscountType), CultureInfo.InvariantCulture),
            value,
            FormatN11Date(product.DiscountStartDate),
            FormatN11Date(product.DiscountEndDate));
    }

    private static string DiscountTypeCode(ProductDiscountType type)
    {
        return type == ProductDiscountType.Percentage ? "2" : "1";
    }

    private static string FormatN11Date(DateTime? date)
    {
        return date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Yaprak kategori attribute tanımı — REST-primary client'tan, DAĞITIK CACHE'li (6 saat; kategori
    /// tanımları nadiren değişir, her push'ta N11'e gitmeye gerek yok). Alınamazsa push DURUR (fail-fast:
    /// validasyonsuz gönderim N11'de tanımsız davranış üretir; kullanıcı yeniden dener).</summary>
    private async Task<N11LeafAttributes> GetLeafAttributesCachedAsync(string categoryExternalId, SalesChannelTrN11 channel)
    {
        try
        {
            return (await _leafAttributeCache.GetOrAddAsync(
                $"N11LeafAttributes:{categoryExternalId}",
                async () => await _categoryClient.GetLeafAttributesAsync(categoryExternalId, channel.AppKey, channel.AppSecret),
                () => new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
                }))!;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "N11 kategori attribute tanımı alınamadı ({CategoryId}).", categoryExternalId);
            throw new BusinessException("TradeXpress:N11:Product:CategoryAttributesUnavailable")
                .WithData("CategoryId", categoryExternalId);
        }
    }

    /// <summary>Varyant başına option attribute (name/value) — ProductVariantAttributeValue → attribute adı + değer.</summary>
    private async Task<Dictionary<Guid, List<N11ProductAttributePair>>> LoadVariantOptionsAsync(Guid productId, List<Guid> variantIds)
    {
        var result = new Dictionary<Guid, List<N11ProductAttributePair>>();
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
                list = new List<N11ProductAttributePair>();
                result[link.EntityVariantId] = list;
            }

            list.Add(new N11ProductAttributePair(name, value));
        }

        return result;
    }

    /// <summary>CurrencyUnit kodu → N11 currencyType (1=TL varsayılan; USD=2, EUR=3 best-effort — yalnız TL belgeli).</summary>
    private async Task<int> ResolveCurrencyTypeAsync(Guid? currencyUnitId)
    {
        if (currencyUnitId is not { } id)
        {
            return 1;   // TL
        }

        var unit = await _currencyRepository.FindAsync(id);
        return (unit?.Code.Trim().ToUpperInvariant()) switch
        {
            "USD" => 2,
            "EUR" => 3,
            _ => 1,     // TRY/TL + bilinmeyen → TL
        };
    }

    // ── N11 varyant ÖZELLİKLERİ (klon-sonra-ayrış) + kartezyen kombinasyon RECONCILE ─────────────────────
    // ProductAttributes = N11'in KENDİ varyant özellikleri (klon-sonra-ayrış, 2026-07-09 kararı). Tanımlıysa (persist
    // edilmiş en az 1 özellik varsa) kanal-ürünün kombinasyon seti ARTIK bu özelliklerin kartezyen kombinasyonundan
    // üretilir — legacy ERP-doğrudan graf (BuildStockItemGraphAsync/SaveStockItemOverridesAsync) devre dışı kalır.
    // Reconcile anahtarı CombinationSignature ("{AttributeId}={ValueId}|...", AttributeId sıralı) — STABİL ID'lerden
    // kurulur, ERP ProductVariantId yalnız fiyat/stok fallback KAYNAĞI (bir kerelik fırsatçı eşleştirme; reconcile
    // anahtarı DEĞİL). Özellik/değer silinip kombinasyon artık üretilemezse o satır + reçetesi TEMİZLENİR (türetilmiş
    // satır — kaynağı kalkınca o da kalkar; SaveStockItemOverridesAsync'teki "tutarlı ol" temizlik konvansiyonuyla simetrik).

    /// <summary>Bellek-içi özellik + değer görünümü — reconcile matematiği (kartezyen + imza) için.</summary>
    private sealed record AttributeWithValues(Guid AttributeId, string AttributeName, List<(Guid ValueId, string Value)> Values);

    private async Task<List<SalesChannelTrN11ProductAttribute>> LoadChannelAttributeEntitiesAsync(Guid channelProductId)
    {
        return await AsyncExecuter.ToListAsync(
            (await _channelAttributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelTrN11ProductId == channelProductId)
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.CreationTime));
    }

    private async Task<List<SalesChannelTrN11ProductAttributeValue>> LoadChannelAttributeValueEntitiesAsync(List<Guid> channelAttributeIds)
    {
        if (channelAttributeIds.Count == 0)
        {
            return new List<SalesChannelTrN11ProductAttributeValue>();
        }

        return await AsyncExecuter.ToListAsync(
            (await _channelAttributeValueRepository.GetQueryableAsync())
                .Where(v => channelAttributeIds.Contains(v.AttributeId))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.CreationTime));
    }

    private static List<SalesChannelTrN11ProductAttributeDto> BuildAttributesDto(
        List<SalesChannelTrN11ProductAttribute> channelAttributes, List<SalesChannelTrN11ProductAttributeValue> values)
    {
        var valuesByChannelAttribute = values.GroupBy(v => v.AttributeId).ToDictionary(g => g.Key, g => g.ToList());
        return channelAttributes.Select(a => new SalesChannelTrN11ProductAttributeDto
        {
            Id = a.Id,
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByChannelAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<SalesChannelTrN11ProductAttributeValue>())
                .Select(v => new SalesChannelTrN11ProductAttributeValueDto
                {
                    Id = v.Id,
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    private static List<AttributeWithValues> ToAttributeWithValues(
        List<SalesChannelTrN11ProductAttribute> channelAttributes, List<SalesChannelTrN11ProductAttributeValue> values)
    {
        var valuesByChannelAttribute = values.GroupBy(v => v.AttributeId).ToDictionary(g => g.Key, g => g.ToList());
        return channelAttributes.Select(a => new AttributeWithValues(
            a.Id,
            a.Name,
            (valuesByChannelAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<SalesChannelTrN11ProductAttributeValue>())
                .Select(v => (v.Id, v.Value))
                .ToList())).ToList();
    }

    /// <summary>Özellik + değer grafını persist eder (RecipeLines ile AYNI iki-öge diff deseni: silinenler → upsert;
    /// ClientKey→Id input DTO'suna geri yazılır). Boş/null girdi no-op (mevcut özelliklere DOKUNMAZ).</summary>
    private async Task SaveAttributesGraphAsync(SalesChannelTrN11Product channelProduct, List<SalesChannelTrN11ProductAttributeDto>? attributesInput)
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
            SalesChannelTrN11ProductAttribute entity;
            if (channelAttribute.Id == Guid.Empty)
            {
                entity = new SalesChannelTrN11ProductAttribute(channelProduct.CompanyId, channelProduct.Id, channelAttribute.Name, channelAttribute.DisplayOrder);
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
                    var valueEntity = new SalesChannelTrN11ProductAttributeValue(channelProduct.CompanyId, channelAttribute.Id, value.Value, value.DisplayOrder);
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
    /// (<see cref="ProductAttributeConsts.MaxAttributesPerProduct"/> = 5) karşı doğrular — persist BAŞLAMADAN
    /// fail-fast (analiz 1.1 güçlendirmesi, 2026-07-09). Üst-sınır CombinationSignature kolon kapasitesini de
    /// korur (600 karakter ≈ 8 "{AttributeId}={ValueId}" çifti; sabit 8'i AŞMAMALI).</summary>
    private async Task EnsureAttributeCountWithinLimitAsync(Guid channelProductId, List<SalesChannelTrN11ProductAttributeDto> attributesInput)
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
            throw new BusinessException("TradeXpress:N11:Product:TooManyAttributes")
                .WithData("Max", ProductAttributeConsts.MaxAttributesPerProduct);
        }
    }

    /// <summary>Özellik grafını persist eder + persist-sonrası DB durumuna göre kartezyen kombinasyon satırlarını
    /// reconcile eder. Döndürdüğü bool = channelAttribute-modu AKTİF mi (en az 1 persist edilmiş özellik var) — false ise çağıran
    /// legacy ERP-doğrudan yola (<see cref="BuildStockItemGraphAsync"/>/<see cref="SaveStockItemOverridesAsync"/>) düşer.</summary>
    private async Task<bool> SaveAttributesAndReconcileAsync(SalesChannelTrN11Product channelProduct, List<SalesChannelTrN11ProductAttributeDto>? attributesInput)
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
    /// devredildi (kod tabanındaki son yerel kartezyen kopyası 2026-07-09'da silindi). "0 özellik → kombinasyon yok"
    /// yorumu çağıran guard'ıdır (motorun birim elemanına — tek boş kombinasyon — düşülmez).</summary>
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

    /// <summary>Kombinasyon imzası — N11-yerel format ("{AttributeId}={ValueId}|...", AttributeId artan sıralı).
    /// BİLİNÇLİ olarak <see cref="VariantCombinationEngine.BuildKey"/>'e delege EDİLMEZ: format farklı (BuildKey düz
    /// Guid join) ve tüketici-yerel/opak (analiz 1.1) — S1 karakterizasyon testleri bu formatı snapshot'ladı, DEĞİŞTİRME.</summary>
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

    /// <summary>Bir N11 kombinasyonunun (Attribute.Name/AttributeValue.Value seti) ERP varyantlarından TAM örtüşen tekini bulur
    /// (bir kerelik fırsatçı eşleştirme — reconcile anahtarı DEĞİL). Örtüşme YOKSA ya da BİRDEN FAZLA varyant aynı
    /// sete sahipse (belirsiz) null döner — yanlış atamaktansa N11-only kalması güvenli.</summary>
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

    /// <summary>Kartezyen kombinasyon satırlarını (<see cref="SalesChannelTrN11ProductStockItem"/>, CombinationSignature
    /// ile) mevcut özellik/değer setiyle reconcile eder — diff/sıra mekaniği <see cref="VariantSetReconciler"/>'a
    /// devredildi (2026-07-09): artık üretilemeyen kombinasyonlar (satır + reçetesi) removeAsync'te SİLİNİR (orphan
    /// temizliği), eksik kombinasyonlar addAsync'te İNSERT edilir (fırsatçı ERP eşleştirmesiyle — KANAL politikası,
    /// çekirdekte değil). Var olan satırlara (imzası hâlâ üretilebilir) DOKUNULMAZ — kullanıcı override/reçete verisi korunur.</summary>
    private async Task SynchronizeStockItemsAsync(SalesChannelTrN11Product channelProduct, List<AttributeWithValues> channelAttributes)
    {
        var combos = BuildCombinations(channelAttributes);
        var comboBySignature = new Dictionary<string, List<(Guid AttributeId, Guid ValueId)>>(StringComparer.Ordinal);
        foreach (var combo in combos)
        {
            comboBySignature[BuildCombinationSignature(combo)] = combo;
        }

        var existingHeaders = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrN11ProductId == channelProduct.Id && h.CombinationSignature != null));

        // ERP indeksi TEMBEL: ilk eksik kombinasyonda yüklenir — eksik yoksa ERP sorgusu hiç atılmaz (eski davranış).
        Dictionary<Guid, HashSet<(string Name, string Value)>>? erpIndex = null;
        var attributeById = channelAttributes.ToDictionary(a => a.AttributeId);

        await VariantSetReconciler.ReconcileAsync(
            targetKeys: combos.Select(BuildCombinationSignature).ToList(),
            existingItems: existingHeaders,
            keySelector: h => h.CombinationSignature!,
            removeAsync: async orphan =>
            {
                await _channelRecipeLineRepository.DeleteAsync(
                    r => r.SalesChannelTrN11ProductId == channelProduct.Id && r.StockItemId == orphan.Id,
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

                var header = new SalesChannelTrN11ProductStockItem(channelProduct.CompanyId, channelProduct.Id, matchedVariantId);
                header.SetCombinationSignature(signature);
                await _stockItemRepository.InsertAsync(header, autoSave: true);
            });
    }

    private static string BuildCombinationLabel(string signature, Dictionary<Guid, AttributeWithValues> attributeById)
    {
        return BuildLabel(ResolveCombinationPairs(signature, attributeById));
    }

    /// <summary>İmzadaki (AttributeId=ValueId) çiftlerini kanal özellik/değer METİNLERİNE çözer (imza sırası —
    /// AttributeId artan). Sözlükte bulunamayan bayat attribute çifti atlanır (reconcile orphan'ı zaten siler; savunmacı).</summary>
    private static List<(string Name, string? Value)> ResolveCombinationPairs(string signature, Dictionary<Guid, AttributeWithValues> attributeById)
    {
        var pairs = new List<(string Name, string? Value)>();
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
                pairs.Add((channelAttribute.AttributeName, value));
            }
        }

        return pairs;
    }

    /// <summary>Kartezyen kombinasyon satırlarını graf DTO'suna projekte eder (reconcile'ın ÜRETTİĞİ set — reconcile
    /// bu metottan ÖNCE çağrılmış olmalı). ERP-backed (ProductVariantId dolu) satırda da anchor HALA header.Id'dir.</summary>
    private async Task<List<SalesChannelTrN11ProductStockItemGraphDto>> BuildAttributeStockItemsAsync(
        SalesChannelTrN11Product channelProduct, List<AttributeWithValues> channelAttributes)
    {
        var headers = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrN11ProductId == channelProduct.Id && h.CombinationSignature != null)
                .OrderBy(h => h.CreationTime));
        if (headers.Count == 0)
        {
            return new List<SalesChannelTrN11ProductStockItemGraphDto>();
        }

        var headerIds = headers.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrN11ProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
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
        // reçetesiz/yan-maliyetsiz kalmasın — inceleme bulgusu). N11-only satırda (ERP eşleşmesi yok) taban maliyet
        // bilinmez → reçete boş kalır (OverridePrice zaten zorunlu).
        var erpByVariant = erpVariantIds.Count == 0
            ? new Dictionary<Guid, List<ProductVariantRecipeLine>>()
            : (await AsyncExecuter.ToListAsync(
                    (await _erpRecipeLineRepository.GetQueryableAsync())
                        .Where(r => erpVariantIds.Contains(r.ProductVariantId))))
                .GroupBy(r => r.ProductVariantId)
                .ToDictionary(g => g.Key, g => g.ToList());
        var sideCostPlan = await BuildSideCostPlanAsync(channelProduct);

        var attributeById = channelAttributes.ToDictionary(a => a.AttributeId);
        var nodes = new List<SalesChannelTrN11ProductStockItemGraphDto>(headers.Count);
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

            var node = new SalesChannelTrN11ProductStockItemGraphDto
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

        return nodes;
    }

    /// <summary>Kartezyen kombinasyon satırlarının (zaten reconcile ile server-side üretilmiş) düzenlenebilir
    /// alanlarını (OverridePrice/OverrideStock/Margin/RecipeLines) kullanıcı girdisinden persist eder. Client YENİ
    /// satır AÇAMAZ (Id boş düğüm atlanır — reconcile tek üretici); yabancı/bayat Id sessizce atlanır. N11-only
    /// (ProductVariantId null) satırda ERP fallback'i YOKTUR → OverridePrice + OverrideStock ZORUNLU (fail-fast).</summary>
    private async Task SaveAttributeStockItemOverridesAsync(SalesChannelTrN11Product channelProduct, List<SalesChannelTrN11ProductStockItemGraphDto>? variants)
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
            if (header is null || header.SalesChannelTrN11ProductId != channelProduct.Id)
            {
                continue;
            }

            if (header.ProductVariantId is null && (node.OverridePrice is null || node.OverrideStock is null))
            {
                throw new BusinessException("TradeXpress:N11:ProductVariant:OverrideRequiredForN11Only");
            }

            var insuredShippingChanged = header.InsuredShippingEnabled != node.InsuredShippingEnabled;
            header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
            header.SetOverrideStock(node.OverrideStock);
            header.SetMargin(node.Margin);
            header.SetInsuredShippingEnabled(node.InsuredShippingEnabled);
            await _stockItemRepository.UpdateAsync(header, autoSave: true);

            // Sigortalı-gönderim anahtarı bu save'de DEĞİŞTİYSE reçeteye hemen işlenir (yalnız sigorta satırı —
            // kullanıcının sildiği diğer otomatik satırlar geri getirilmez); yoksa türetilmiş fiyat açık
            // "Giderleri Yeniden Uygula"ya kadar bayat kalırdı (inceleme bulgusu).
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

    // ── Kanal-özel varyant override (fiyat/stok/marj + reçete) ──────────────────────────────────────
    // Graf = ERP varyant seti (aktif) ⋈ kaydedilmiş kanal override (LEFT JOIN). Kaydedilmiş reçete varsa ondan,
    // yoksa ERP reçetesi KLONLANIR. NetCost + türetilmiş fiyat CANLI hesaplanır (ProductAppService ile ORTAK motor).

    /// <summary>Bir kanal-ürünün varyant override grafını kurar: aktif ERP varyantları × kaydedilmiş override başlığı
    /// (fiyat/stok/marj) + reçete (kaydedilmişse ondan, yoksa ERP reçetesinden klon). NetCost + türetilmiş fiyat
    /// (NetCost×(1+Margin/100)) canlı hesaplanır. Varyant yoksa boş liste.</summary>
    private async Task<List<SalesChannelTrN11ProductStockItemGraphDto>> BuildStockItemGraphAsync(SalesChannelTrN11Product channelProduct)
    {
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == channelProduct.ProductId && v.IsActive)
                .OrderByDescending(v => v.IsMain).ThenBy(v => v.Code));
        if (variants.Count == 0)
        {
            return new List<SalesChannelTrN11ProductStockItemGraphDto>();
        }

        var variantIds = variants.Select(v => v.Id).ToList();

        // Yalnız ERP-backed başlıklar (ProductVariantId dolu) — N11-only satırlar bu ERP-varyant grafına girmez
        // (kendi grubunda ayrıca listelenir; bkz. BuildAttributeStockItemsAsync).
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrN11ProductId == channelProduct.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        // Reçete satırları artık override BAŞLIĞININ kendi Id'sine bağlı (2026-07-09 kararı — StockItemId),
        // ERP ProductVariantId'ye DEĞİL; bu yüzden önce header.Id'ye, sonra ERP varyantına eşleniyor.
        var headerIds = headers.Values.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrN11ProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ERP reçetesi — yalnız kaydedilmiş kanal reçetesi OLMAYAN varyantlarda klonlanır (LEFT JOIN eksiği ERP'den).
        var erpByVariant = (await AsyncExecuter.ToListAsync(
                (await _erpRecipeLineRepository.GetQueryableAsync())
                    .Where(r => variantIds.Contains(r.ProductVariantId))))
            .GroupBy(r => r.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Yan-maliyet planı (kanal ayarı + N11 kategori komisyonu) — yalnız KLON yoluna uygulanır (kaydedilmiş
        // reçeteye dokunulmaz; silinen otomatik satır kendiliğinden geri gelmesin — açık "yeniden uygula" var).
        var sideCostPlan = await BuildSideCostPlanAsync(channelProduct);

        var nodes = new List<SalesChannelTrN11ProductStockItemGraphDto>(variants.Count);
        foreach (var v in variants)
        {
            var node = new SalesChannelTrN11ProductStockItemGraphDto
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
            // açılışta klon dalı ERP reçetesini + yan-maliyet satırlarını yeniden üretir (tam-boşaltma kararı kalıcı
            // değildir). Kabul edilmiş davranış: boş reçete = "ERP'den yeniden devral" sinyali sayılır.
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

        return nodes;
    }

    /// <summary>Yan-maliyet planını kurar: kanal <c>SideCostSettings</c>'i + EFEKTİF komisyon oranı çözümü — ürünün
    /// N11 kategorisinin <c>N11Category.CommissionRate</c>'i (host-global taksonomi; TSV import doldurur), yoksa
    /// kanal varsayılanı; ÜSTÜNE N11'in tüm kategorilerde ZORUNLU Pazarlama (%1) + Pazaryeri (%0,67) hizmet
    /// bedelleri KDV brütüyle eklenir (SSOT: <see cref="N11CategoryCommissionImporter.ResolveEffectiveCommissionRate"/>).
    /// Sigortalı-gönderim anahtarı varyant-başı olduğundan burada KAPALI döner — çağıran
    /// <c>plan with { VariantOptInEnabled = ... }</c> ile varyanta göre açar.</summary>
    private async Task<SideCostPlan> BuildSideCostPlanAsync(SalesChannelTrN11Product channelProduct)
    {
        var channel = await _channelRepository.FindAsync(channelProduct.SalesChannelId);
        var settings = channel?.SideCosts;

        decimal? categoryRate = null;
        decimal? marketingFeeRate = null;
        decimal? marketplaceFeeRate = null;
        if (!string.IsNullOrEmpty(channelProduct.CategoryExternalId))
        {
            // Host-global taksonomi okuması (N11CategoryAppService ile aynı sınır — db-per-tenant merkeziliği).
            using (CurrentTenant.Change(null))
            {
                var category = await AsyncExecuter.FirstOrDefaultAsync(
                    (await _n11CategoryRepository.GetQueryableAsync())
                        .Where(c => c.ExternalId == channelProduct.CategoryExternalId));
                categoryRate = category?.CommissionRate;
                marketingFeeRate = category?.MarketingFeeRate;
                marketplaceFeeRate = category?.MarketplaceFeeRate;
            }
        }

        // Kanal-fallback oranı = AutoRate işaretli Commission gider satırının Value'su (gider-satırı modeli).
        var commissionRate = N11CategoryCommissionImporter.ResolveEffectiveCommissionRate(
            categoryRate, marketingFeeRate, marketplaceFeeRate, settings?.GetAutoCommissionFallbackRate());
        return SideCostPlan.From(settings, commissionRate, variantOptInEnabled: false);
    }

    /// <summary>Yan-maliyet satırlarını KAYDEDİLMİŞ reçetelerde ayarlardan TAZELER ("yeniden uygula"): işaretli
    /// (otomatik) satırlar düşürülüp yeniden üretilir, kullanıcı satırlarına dokunulmaz. Kaydedilmemiş reçeteler
    /// atlanır (klon yolu zaten ekler). Kanal gider ayarı değişince ya da silinen otomatik satırı geri getirmek
    /// için kullanılır; idempotent.</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrN11ProductDto> ReapplySideCostsAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var plan = await BuildSideCostPlanAsync(entity);

        var headers = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrN11ProductId == entity.Id));
        var headerIds = headers.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrN11ProductId == entity.Id && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var header in headers)
        {
            if (!savedByHeader.TryGetValue(header.Id, out var saved))
            {
                continue;   // kaydedilmiş reçete yok → klon yolu (BuildStockItemGraphAsync) zaten ekler
            }

            var lines = MapSavedRecipeLines(saved);
            if (SideCostRecipeComposer.ReapplyLines(lines, plan with { VariantOptInEnabled = header.InsuredShippingEnabled }))
            {
                await SaveChannelRecipeLinesAsync(entity, header.Id, lines);
            }
        }

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Push için varyant-başı efektif fiyat/stok — zincir: OverridePrice ?? türetilmiş (KAYDEDİLMİŞ reçete
    /// NetCost × (1+Margin/100)) ?? ERP SalePrice; stok: OverrideStock ?? ERP StockQuantity. Push PERSIST edilmiş
    /// gerçeği kullanır (ERP klonu değil) — kaydedilmemiş reçete türetilmiş fiyat üretmez.</summary>
    private async Task<IReadOnlyDictionary<Guid, VariantPushPricing>> ResolveVariantPushPricingAsync(
        SalesChannelTrN11Product channelProduct, List<EntityVariant> variants)
    {
        var variantIds = variants.Select(v => v.Id).ToList();

        // Satış fiyatı/birimi ProductVariantDetail'de (agnostik EntityVariant Product uzantısı) — EntityVariantId ile batch yüklenir.
        var salePrices = await LoadVariantSalePricesAsync(variantIds);

        // Yalnız ERP-backed başlıklar — N11-only satırlar (ProductVariantId null) burada ERP varyantına eşlenemez,
        // kendi push zincirleri ResolveN11OnlyPushPricingAsync'te (Override ?? türetilmiş; ERP fallback YOK).
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrN11ProductId == channelProduct.Id && h.ProductVariantId != null
                        && variantIds.Contains(h.ProductVariantId!.Value))))
            .ToDictionary(h => h.ProductVariantId!.Value);

        // Reçete satırları header'ın KENDİ Id'sine bağlı (2026-07-09 kararı) — ERP ProductVariantId'ye değil.
        var headerIds = headers.Values.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrN11ProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lineSets = variants
            .Select(v => headers.TryGetValue(v.Id, out var h) && savedByHeader.TryGetValue(h.Id, out var l)
                ? MapSavedRecipeLines(l)
                : new List<ProductRecipeLineGraphDto>())
            .ToList();
        var costs = await _recipeCostPopulator.PopulateAsync(lineSets);

        var result = new Dictionary<Guid, VariantPushPricing>(variants.Count);
        for (var i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            headers.TryGetValue(v.Id, out var header);
            var (salePrice, saleCurrencyUnitId) = salePrices.GetValueOrDefault(v.Id);
            decimal? derived = costs[i].NetCost is { } nc && !costs[i].NetCostMissingRate
                ? DerivedPriceCalculator.Calculate(nc, header?.Margin)
                : null;
            var price = header?.OverridePrice ?? derived ?? salePrice;
            var stock = header?.OverrideStock ?? v.StockQuantity;
            result[v.Id] = new VariantPushPricing(price, stock, saleCurrencyUnitId);
        }

        return result;
    }

    /// <summary>Varyant satış-fiyatı + para birimini <see cref="ProductVariantDetail"/>'den (agnostik EntityVariant'ın
    /// Product uzantısı) EntityVariantId ile batch yükler (N+1 yok). Fiyatlanmamış varyantta (detail yok) (null, null).</summary>
    private async Task<Dictionary<Guid, (decimal? SalePrice, Guid? CurrencyUnitId)>> LoadVariantSalePricesAsync(IReadOnlyCollection<Guid> variantIds)
    {
        if (variantIds.Count == 0)
        {
            return new Dictionary<Guid, (decimal?, Guid?)>();
        }

        var details = await AsyncExecuter.ToListAsync(
            (await _variantDetailRepository.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId)));
        return details.ToDictionary(d => d.EntityVariantId, d => (d.SalePrice, d.SalePriceCurrencyUnitId));
    }

    /// <summary>Kanal-özel varyant override grafını persist eder — override sinyali (OverridePrice/OverrideStock/Margin
    /// herhangi biri dolu) olan varyantın başlığı + reçetesi yazılır; TÜMÜ boşsa (saf ERP devralma) kaydedilmiş
    /// override/reçete TEMİZLENİR (ölü satır şişmesini önle — kullanıcı kararı: tutarlı ol). Türetilmiş fiyat/NetCost
    /// hesap alanları PERSIST EDİLMEZ (canlı).</summary>
    private async Task SaveStockItemOverridesAsync(SalesChannelTrN11Product channelProduct, List<SalesChannelTrN11ProductStockItemGraphDto> variants)
    {
        if (variants == null || variants.Count == 0)
        {
            return;
        }

        // Yalnız ERP-backed başlıklar — N11-only satırlar (ProductVariantId null) bu ERP-anchor'lı override yolundan
        // GEÇMEZ, kartezyen motor (SynchronizeStockItemsAsync) tarafından ayrıca üretilir/güncellenir.
        var existingHeaders = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrN11ProductId == channelProduct.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        SideCostPlan? sideCostPlan = null;   // tembel — yalnız sigorta anahtarı değişen satır varsa kurulur

        foreach (var node in variants)
        {
            if (node.ProductVariantId is null || node.ProductVariantId == Guid.Empty)
            {
                continue;   // anchor yok (N11-only ya da bayat düğüm) → atla; kartezyen motor ele alır
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
                        r => r.SalesChannelTrN11ProductId == channelProduct.Id && r.StockItemId == header.Id,
                        autoSave: true);
                    await _stockItemRepository.DeleteAsync(header, autoSave: true);
                }

                continue;
            }

            var insuredShippingChanged = (header?.InsuredShippingEnabled ?? false) != node.InsuredShippingEnabled;
            if (header is null)
            {
                header = new SalesChannelTrN11ProductStockItem(channelProduct.CompanyId, channelProduct.Id, node.ProductVariantId);
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
            // "Giderleri Yeniden Uygula"ya kadar bayat kalırdı (inceleme bulgusu).
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

    /// <summary>Bir override BAŞLIĞININ (ERP-backed veya N11-only fark etmez — <paramref name="stockItemId"/> her
    /// zaman <see cref="SalesChannelTrN11ProductStockItem"/>'ın KENDİ Id'sidir) kanal-özel reçete satırlarını persist
    /// eder (ERP SaveRecipeLinesAsync deseni, iki-geçişli): silinenler → LineOrder 0..n yeniden-numara → referans
    /// doğrulama → skaler insert/update (1. geçiş) → türev SelectedLines kaynak Id CSV çözümü (2. geçiş).
    /// ComponentType set-once (ctor'da).</summary>
    private async Task SaveChannelRecipeLinesAsync(SalesChannelTrN11Product channelProduct, Guid stockItemId, List<ProductRecipeLineGraphDto> lines)
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
        var entityByClientKey = new Dictionary<Guid, SalesChannelTrN11ProductStockItemRecipeLine>();
        foreach (var l in survivors)
        {
            SalesChannelTrN11ProductStockItemRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new SalesChannelTrN11ProductStockItemRecipeLine(
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
    private static void ApplyChannelRecipeLineFields(SalesChannelTrN11ProductStockItemRecipeLine entity, ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.CatalogCommodity)
        {
            entity.SetCatalogCommodity(
                l.CommodityProcessType.GetValueOrDefault(),
                l.CommodityId,
                l.CommodityVariantId,
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
    private static List<ProductRecipeLineGraphDto> MapSavedRecipeLines(List<SalesChannelTrN11ProductStockItemRecipeLine> saved)
    {
        var ordered = saved.OrderBy(r => r.LineOrder).ThenBy(r => r.CreationTime).ToList();
        var dtos = ordered.Select(r => new ProductRecipeLineGraphDto
        {
            Id = r.Id,
            LineOrder = r.LineOrder,
            ComponentType = r.ComponentType,
            CommodityProcessType = r.CommodityProcessType,
            CommodityId = r.CommodityId,
            CommodityVariantId = r.CommodityVariantId,
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
            CommodityVariantId = r.CommodityVariantId,
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

    /// <summary>Push için varyant-başı efektif fiyat (override zinciri sonucu) + stok + satış fiyatı birimi (ProductVariantDetail'den).</summary>
    private sealed record VariantPushPricing(decimal? Price, int Stock, Guid? PriceCurrencyUnitId);

    // ── Uygulama + güvenlik ─────────────────────────────────────────────────────────────────────────

    private void ApplyInput(SalesChannelTrN11Product entity, ISalesChannelTrN11ProductInput input)
    {
        entity.SetCategory(input.CategoryExternalId, input.CategoryName);
        entity.SetCondition(input.Condition);
        entity.SetShipmentTemplate(input.ShipmentTemplateName);
        entity.SetDomestic(input.Domestic);
        entity.SetPreparingDay(input.PreparingDay);
        entity.SetMaxPurchaseQuantity(input.MaxPurchaseQuantity);
        entity.SetCurrencyUnit(input.CurrencyUnitId);
        entity.SetProductionDate(input.ProductionDate);
        entity.SetExpirationDate(input.ExpirationDate);
        entity.SetActive(input.IsActive);
        entity.SetSellerNote(input.SellerNote);
        entity.SetDescription(input.Description);
        entity.SetGroupItemCode(input.GroupItemCode);
        entity.SetGroupAttribute(input.GroupAttribute);
        entity.SetItemName(input.ItemName);
        entity.SetCategoryAttributes(input.CategoryAttributes.Select(a => new SalesChannelTrN11ProductCategoryAttribute(a.Name, a.Value)));
        entity.SetSpecialInfo(input.SpecialInfo.Select(s => new SalesChannelTrN11ProductSpecialInfo(s.Key, s.Value)));
    }

    private async Task<SalesChannelTrN11Product> GetOwnedAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.Id == id && x.CompanyId == companyId));
        if (entity is null)
        {
            throw new BusinessException("TradeXpress:N11:Product:RecordNotFound");
        }

        return entity;
    }

    private async Task<SalesChannelTrN11> GetOwnedChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.Id == salesChannelId && x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:N11:Product:ChannelNotFound");
        }

        return channel;
    }

    private async Task<Product> GetOwnedProductAsync(Guid productId)
    {
        var companyId = EnsureCurrentCompanyId();
        var product = await AsyncExecuter.FirstOrDefaultAsync(
            (await _productRepository.GetQueryableAsync()).Where(x => x.Id == productId && x.CompanyId == companyId));
        if (product is null)
        {
            throw new BusinessException("TradeXpress:N11:Product:ProductNotFound");
        }

        return product;
    }

    private async Task EnsureProductOwnedAsync(Guid productId)
    {
        await GetOwnedProductAsync(productId);
    }

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:N11:Product:CompanyRequired");
        }

        return companyId;
    }
}
