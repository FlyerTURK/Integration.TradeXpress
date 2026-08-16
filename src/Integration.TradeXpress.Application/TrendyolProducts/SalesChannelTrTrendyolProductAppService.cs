using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Timing;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Diagnostics;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.SalesChannels.Variants;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Trendyol;
using Integration.TradeXpress.TrendyolBrands;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.TrendyolShipments;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Integration.TradeXpress.Channels;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol ürün listeleme CRUD + push — <b>company-owned + per-tenant</b>. Yapılandırma (kategori/marka/KDV/kargo/
/// attribute) bizde tutulur; <see cref="PushToTrendyolAsync"/> ürünü + varyantlarını (items) Trendyol'a ASENKRON
/// gönderir (batch id döner), <see cref="RefreshStatusAsync"/> durumu çeker. Push kanalın KENDİ kimliğiyle yapılır.
/// Varyant yönetimi N11 final deseniyle BİREBİR: kanal-özel özellik/değer grafı (klon-sonra-ayrış) → kartezyen
/// kombinasyon (StockItem) reconcile (CombinationSignature anahtarlı) → override/reçete satırları.
/// Pazaryerinden İÇE AKTARMA (<c>ImportFromMarketplaceAsync</c>) partial dosyada:
/// <c>SalesChannelTrTrendyolProductAppService.Import.cs</c>.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public partial class SalesChannelTrTrendyolProductAppService : TradeXpressAppService, ISalesChannelTrTrendyolProductAppService
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<EntityAttribute, Guid> _attributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _attributeValueRepository;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _variantAttributeRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<TrendyolCargoProvider, Guid> _cargoProviderRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _stockItemRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _channelRecipeLineRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _erpRecipeLineRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductAttribute, Guid> _channelAttributeRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid> _channelAttributeValueRepository;
    private readonly IRepository<TrendyolCategory, Guid> _trendyolCategoryRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly EntityVariantManager _variantManager;
    private readonly RecipeCostPopulator _recipeCostPopulator;
    private readonly SubstitutionChannelPlanProvider _substitutionPlanProvider;
    private readonly ICurrentCompany _currentCompany;
    private readonly ITrendyolProductClient _client;
    private readonly ITrendyolCategoryAppService _categoryAppService;
    private readonly MarketplacePushImageResolver _pushImageResolver;
    private readonly TrendyolProductPushValidator _pushValidator;
    private readonly TemporaryMediaLinkPublisher _temporaryMediaLinkPublisher;
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _pushHistoryRepository;
    private readonly MarketplaceImageDownloader _imageDownloader;
    private readonly TrendyolBrandCacheManager _brandCacheManager;
    private readonly TrendyolCommissionResolver _commissionResolver;
    private readonly SalesChannelTrTrendyolProductRemover _remover;
    private readonly ImportedProductCategoryResolver _categoryResolver;

    /// <summary>Varyant satış hazırlığı kapısı (N11'deki eşi) — İNSAN onayından geçmemiş varyant push adayı OLMAZ.</summary>
    private readonly VariantSaleReadinessResolver _saleReadiness;

    /// <summary>Toplu varsayilan gorsel cozumu (fiyatlandirma tahtasi) — urun basina cagri yapilmaz.</summary>
    private readonly IEntityMediaAppService _entityMedia;

    /// <summary>Kodlu hatayı operatörün okuyacağı metne çevirir (teşhis verisi dahil) — LastError'a ham
    /// <c>ex.Message</c> yazmak guard'ların doldurduğu SKU/fiyat/sınır bilgisini çöpe atardı.</summary>
    private readonly BusinessExceptionDescriber _describer;

    /// <summary>Kanal tahtalarının ORTAK gövdesi — karar sinyali iki kanalda da aynı yerden gelir.</summary>
    private readonly ChannelProductBoardBuilder _boardBuilder;

    /// <summary>Push GEÇMİŞİ yazıcısı — yalnız COMPLETED batch'te çağrılır (delil "kabul edildi" demektir).</summary>
    private readonly TrendyolPushHistoryRecorder _historyRecorder;

    public SalesChannelTrTrendyolProductAppService(
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> variantDetailRepository,
        IRepository<EntityAttribute, Guid> attributeRepository,
        IRepository<EntityAttributeValue, Guid> attributeValueRepository,
        IRepository<EntityVariantAttributeValue, Guid> variantAttributeRepository,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        IRepository<SalesChannelTrTrendyolProductStockItem, Guid> stockItemRepository,
        IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> channelRecipeLineRepository,
        IRepository<ProductVariantRecipeLine, Guid> erpRecipeLineRepository,
        IRepository<SalesChannelTrTrendyolProductAttribute, Guid> channelAttributeRepository,
        IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid> channelAttributeValueRepository,
        IRepository<TrendyolCategory, Guid> trendyolCategoryRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        EntityVariantManager variantManager,
        RecipeCostPopulator recipeCostPopulator,
        SubstitutionChannelPlanProvider substitutionPlanProvider,
        ICurrentCompany currentCompany,
        ITrendyolProductClient client,
        ITrendyolCategoryAppService categoryAppService,
        MarketplacePushImageResolver pushImageResolver,
        MarketplaceImageDownloader imageDownloader,
        TrendyolBrandCacheManager brandCacheManager,
        TrendyolCommissionResolver commissionResolver,
        SalesChannelTrTrendyolProductRemover remover,
        ImportedProductCategoryResolver categoryResolver,
        VariantSaleReadinessResolver saleReadiness,
        IEntityMediaAppService entityMedia,
        BusinessExceptionDescriber describer,
        TrendyolPushHistoryRecorder historyRecorder,
        ChannelProductBoardBuilder boardBuilder,
        TrendyolProductPushValidator pushValidator,
        TemporaryMediaLinkPublisher temporaryMediaLinkPublisher,
        IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> pushHistoryRepository,
        IRepository<TrendyolCargoProvider, Guid> cargoProviderRepository)
    {
        _pushValidator = pushValidator;
        _temporaryMediaLinkPublisher = temporaryMediaLinkPublisher;
        _pushHistoryRepository = pushHistoryRepository;
        _cargoProviderRepository = cargoProviderRepository;
        _saleReadiness = saleReadiness;
        _entityMedia = entityMedia;
        _describer = describer;
        _boardBuilder = boardBuilder;
        _historyRecorder = historyRecorder;
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
        _trendyolCategoryRepository = trendyolCategoryRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _variantManager = variantManager;
        _recipeCostPopulator = recipeCostPopulator;
        _substitutionPlanProvider = substitutionPlanProvider;
        _currentCompany = currentCompany;
        _client = client;
        _categoryAppService = categoryAppService;
        _pushImageResolver = pushImageResolver;
        _imageDownloader = imageDownloader;
        _brandCacheManager = brandCacheManager;
        _commissionResolver = commissionResolver;
        _remover = remover;
        _categoryResolver = categoryResolver;
    }

    public virtual async Task<List<SalesChannelTrTrendyolProductDto>> GetListForProductAsync(Guid productId)
    {
        var companyId = EnsureCurrentCompanyId();

        // Yalnız CANLI kanalların kayıtları — soft-delete edilmiş kanalın yetim kayıtları sızmasın (N11 ile aynı).
        var liveChannelIds = await AsyncExecuter.ToListAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.Id));

        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.ProductId == productId && liveChannelIds.Contains(x.SalesChannelId))
                .OrderBy(x => x.CategoryName));

        var dtos = new List<SalesChannelTrTrendyolProductDto>(items.Count);
        foreach (var item in items)
        {
            var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(item);
            await PopulateStockItemGraphAsync(item, dto);
            dtos.Add(dto);
        }

        return dtos;
    }

    public virtual async Task<List<SalesChannelTrTrendyolProductDto>> GetListForChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId)
                .OrderBy(x => x.CategoryName));
        return items
            .Select(x => ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(x))
            .ToList();
    }

    /// <summary>FİYATLANDIRMA TAHTASI — içe aktarılmış ürünleri fiyat kararı için gereken alanlarla listeler.
    ///
    /// <para><b>Neden ayrı uç:</b> <see cref="GetAsync"/> tam graf kurar (SKU + nitelik + reçete + maliyet).
    /// 103 kayıt için o yolu 103 kez yürümek hem yavaş hem gereksiz — burada TEK sorgu seti kullanılır ve
    /// ürün başına çağrı YAPILMAZ (N+1 yok).</para>
    ///
    /// <para><b>Pazaryeri değerleri ONLARIN gerçeğidir</b> (import anındaki görüntü) ve push zincirini
    /// ETKİLEMEZ — kıyas içindir. Yerel taraftan yalnız KARAR VERDİREN iki sinyal taşınır: reçete kuruldu mu
    /// ve kaç varyant satış hazırlığından geçti. İkisi de 0 ise o ürün bugün satışa çıkamaz.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<List<TrendyolPricingBoardItemDto>> GetPricingBoardAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();

        var channelProducts = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId));

        if (channelProducts.Count == 0)
        {
            return new List<TrendyolPricingBoardItemDto>();
        }

        var productIds = channelProducts.Select(x => x.ProductId).Distinct().ToList();

        // ORTAK GÖVDE: ürün kimliği + görsel + "karar bekliyor mu" sinyali kanal-agnostiktir
        // (ChannelProductBoardBuilder). Buraya kopyalansaydı satılabilirlik kuralı değişince N11 ile
        // Trendyol sessizce ayrışırdı.
        var common = await _boardBuilder.BuildAsync(productIds);

        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && productIds.Contains(v.EntityId) && v.IsActive));

        // Kanal override stoğu; yoksa çekirdek varyant stoğu (import remote değeri oraya tohumlanır).
        var overrideStockByVariant = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(si => si.ProductVariantId != null && si.OverrideStock != null)
                    .Select(si => new { VariantId = si.ProductVariantId!.Value, si.OverrideStock })))
            .GroupBy(x => x.VariantId)
            .ToDictionary(g => g.Key, g => g.First().OverrideStock!.Value);

        var variantsByProduct = variants.GroupBy(v => v.EntityId).ToDictionary(g => g.Key, g => g.ToList());

        // Görseli ortak gövde çözüyor (tek toplu çağrı) — burada ikinci kez sormak aynı sorguyu boşuna koşardı.

        var board = new List<TrendyolPricingBoardItemDto>(channelProducts.Count);

        foreach (var channelProduct in channelProducts)
        {
            var productVariants = variantsByProduct.GetValueOrDefault(channelProduct.ProductId)
                                  ?? new List<EntityVariant>();

            var row = common.GetValueOrDefault(channelProduct.ProductId);

            board.Add(new TrendyolPricingBoardItemDto
            {
                Id = channelProduct.Id,
                ProductId = channelProduct.ProductId,
                ProductCode = row?.ProductCode ?? string.Empty,
                ProductName = row?.ProductName ?? string.Empty,
                ImageUrl = row?.ImageUrl,
                // KANAL-ÖZEL: pazaryeri fiyatı/adedi yalnız Trendyol entity'sinde var (N11 bunları taşımaz).
                RemoteListPrice = channelProduct.ListPrice,
                RemoteOnSale = channelProduct.RemoteOnSale,
                RemoteQuantity = productVariants.Sum(
                    v => overrideStockByVariant.TryGetValue(v.Id, out var over) ? over : v.StockQuantity),
                VariantCount = row?.VariantCount ?? 0,
                HasRecipe = row?.HasRecipe ?? false,
                ReadyVariantCount = row?.ReadyVariantCount ?? 0,
            });
        }

        // Karar bekleyen iş ÖNCE: reçetesiz ürünler başa, sonra satışa çıkamayanlar, sonra ada göre.
        // Tahtanın amacı "ne yapmam gerekiyor"u göstermek — hazır olanlar listenin dibinde durabilir.
        return board
            .OrderBy(x => x.HasRecipe)
            .ThenBy(x => x.ReadyVariantCount > 0)
            .ThenBy(x => x.ProductName)
            .ToList();
    }

    public virtual async Task<SalesChannelTrTrendyolProductDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Okuma tarafı dispatch (N11 ile birebir): özellik modu AKTİFSE (en az 1 persist edilmiş özellik)
    /// kartezyen kombinasyon grafı, DEĞİLSE legacy ERP-doğrudan graf doldurulur. Özellik modu HİÇ aktive edilmemişse
    /// klon-sonra-ayrış TETİKLENİR: ERP ProductAttribute/Value'lardan TASLAK özellik grafı üretilir (Id boş = henüz
    /// persist YOK) — kullanıcı Kaydet'e bastığında SaveAttributesGraphAsync kalıcılaştırır. Salt-okuma DB'ye YAZMAZ.</summary>
    private async Task PopulateStockItemGraphAsync(SalesChannelTrTrendyolProduct entity, SalesChannelTrTrendyolProductDto dto)
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
    private async Task<List<SalesChannelTrTrendyolProductAttributeDto>> BuildDraftAttributesFromErpAsync(Guid productId)
    {
        var attributes = await AsyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.EntityName == ProductEntityName && a.EntityId == productId)
                .OrderBy(a => a.DisplayOrder));
        if (attributes.Count == 0)
        {
            return new List<SalesChannelTrTrendyolProductAttributeDto>();
        }

        var attributeIds = attributes.Select(a => a.Id).ToList();
        var values = await AsyncExecuter.ToListAsync(
            (await _attributeValueRepository.GetQueryableAsync())
                .Where(v => attributeIds.Contains(v.EntityAttributeId))
                .OrderBy(v => v.DisplayOrder));
        var valuesByAttribute = values.GroupBy(v => v.EntityAttributeId).ToDictionary(g => g.Key, g => g.ToList());

        return attributes.Select(a => new SalesChannelTrTrendyolProductAttributeDto
        {
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<EntityAttributeValue>())
                .Select(v => new SalesChannelTrTrendyolProductAttributeValueDto
                {
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> CreateAsync(SalesChannelTrTrendyolProductCreateDto input)
    {
        // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR (N11 ile aynı 2026-07-07 kararı); kanal set-once.
        var channel = await GetOwnedChannelAsync(input.SalesChannelId);
        var product = await GetOwnedProductAsync(input.ProductId);
        var sequenceNo = await NextSequenceNoAsync(channel.Id, product.Id);

        var entity = new SalesChannelTrTrendyolProduct(
            channel.CompanyId,
            channel.Id,
            input.ProductId,
            BuildProductMainId(product.Code, sequenceNo),
            sequenceNo,
            input.CategoryId,
            input.BrandId);
        ApplyInput(entity, input);
        await _repository.InsertAsync(entity, autoSave: true);
        // K3 write-through: kullanıcının canlı aramadan SEÇTİĞİ marka {id, ad, luxury} host-global cache'e düşer
        // (best-effort, idempotent) — picker bir dahaki açılışta cache'ten beslenir.
        await _brandCacheManager.UpsertAsync(entity.BrandId, entity.BrandName, input.BrandIsLuxury);
        await SaveStockItemsAsync(entity, input.ProductAttributes, input.StockItems);

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Kayıt sırası: aynı ürün+kanal içindeki max SequenceNo + 1 — SİLİNMİŞLER DAHİL (soft-delete filtresi
    /// kapalı) ki silinen kaydın Trendyol'da yaşayan listelemesinin barcode/productMainId'si yeniden üretilip EZİLMESİN.</summary>
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

    /// <summary>Trendyol varyant grup anahtarı — kayıt-bazlı benzersiz + insan-okunur (frozen). İLK listeleme ÇIPLAK
    /// ürün kodudur; son ek 2'den başlar (<see cref="ChannelSequenceCode"/> SSOT — "-1" üretilmez). Ürün kodu üst
    /// sınıra yakınsa kod kısmı KIRPILIR (Code 64 + "-{Sıra}" &gt; ProductMainId 64 taşardı — entity guard'ı ham fail
    /// yerine burada dostane onarım; sıra son eki korunur, benzersizlik bozulmaz).</summary>
    private static string BuildProductMainId(string productCode, int sequenceNo)
    {
        if (sequenceNo <= 1)
        {
            return Truncate(productCode, TrendyolProductConsts.ProductMainIdMaxLength);
        }

        var suffix = $"-{sequenceNo}";
        var maxCodeLength = TrendyolProductConsts.ProductMainIdMaxLength - suffix.Length;
        return Truncate(productCode, maxCodeLength) + suffix;   // Truncate: partial'ın Import dilimindeki ortak yardımcı
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> UpdateAsync(Guid id, SalesChannelTrTrendyolProductUpdateDto input)
    {
        var entity = await GetOwnedAsync(id);
        ApplyInput(entity, input);
        await _repository.UpdateAsync(entity, autoSave: true);
        // K3 write-through: seçilen marka {id, ad, luxury} cache'e (best-effort, idempotent — ad/luxury değiştiyse
        // tazelenir; luxury null = picker'a dokunulmadı → cache'teki değer korunur).
        await _brandCacheManager.UpsertAsync(entity.BrandId, entity.BrandName, input.BrandIsLuxury);
        await SaveStockItemsAsync(entity, input.ProductAttributes, input.StockItems);

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Yazma tarafı dispatch (N11 ile birebir): özellik grafını persist eder + persist-sonrası özellik-modu
    /// AKTİFSE kartezyen reconcile + combo-satır override/reçete kaydı; DEĞİLSE legacy ERP-doğrudan override yolu.</summary>
    private async Task SaveStockItemsAsync(
        SalesChannelTrTrendyolProduct entity,
        List<SalesChannelTrTrendyolProductAttributeDto> attributesInput,
        List<SalesChannelTrTrendyolProductStockItemGraphDto> stockItemsInput)
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
        await DeleteChannelProductGraphAsync(entity);
    }

    /// <summary>Kanal ürününü TÜM bağımlılarıyla siler. Graf <see cref="SalesChannelTrTrendyolProductRemover"/>'da
    /// TEK yerde yaşar — kullanıcının silme komutu, şablon ürün silinirken kanal temizliği ve içe aktarımın öksüz
    /// kayıt temizliği aynı grafı tüketir.</summary>
    private Task DeleteChannelProductGraphAsync(SalesChannelTrTrendyolProduct entity)
    {
        return _remover.RemoveGraphAsync(entity);
    }

    /// <summary>Özellik/değer grafını PERSIST EDER + kartezyen reconcile'ı hemen tetikler — TÜM ürünü kaydetmeden
    /// yalnız bu Trendyol kaydının kombinasyon setini yeniler. Full Update ile aynı reconcile mekanizmasını kullanır
    /// (<see cref="SaveAttributesAndReconcileAsync"/>).</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> RegenerateStockItemsAsync(Guid id, List<SalesChannelTrTrendyolProductAttributeDto> productAttributes)
    {
        var entity = await GetOwnedAsync(id);
        await SaveAttributesAndReconcileAsync(entity, productAttributes);

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Muadil M4 köprüsü — Top-N BAŞARILI kombinasyonu bu Trendyol ürününün StockItem'larına dönüştürür.
    /// N11 adaptörüyle AYNI nötr planı (<see cref="SubstitutionStockItemPlanner"/>, <see cref="SubstitutionChannelPlanProvider"/>)
    /// tüketir; uygulama MEVCUT kartezyen reconcile yolundan (<see cref="SaveAttributesAndReconcileAsync"/>) geçer —
    /// paralel kayıt yolu YOK. Reçete → maliyet zinciri → türetilmiş fiyat; OverrideStock = paket sayısı;
    /// Rank sırası = değer DisplayOrder'ı (ilk sıra = ANA varyant). Yalnız "Kombinasyon" özelliği yönetilir.</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SubstitutionApplyResultDto> ApplySubstitutionAsync(Guid id, SubstitutionApplyInput input)
    {
        var entity = await GetOwnedAsync(id);

        // Orkestrasyon KANAL-AGNOSTİK gövdede (SubstitutionChannelPlanProvider.ApplyAsync — N11 ile TEK akış);
        // bu adaptör yalnız Trendyol graf tiplerini bağlar: özellik/değer okuma, upsert planı → Trendyol DTO
        // çevirisi + MEVCUT persist/reconcile yolu (SaveAttributesAndReconcileAsync) ve StockItem
        // paket stoğu + reçete yazımı (ReplaceChannelRecipeLinesAsync).
        // Yan-maliyet planı — Muadil'in yazdığı TAZE reçetelere de kanal giderleri eklenir (klon yoluyla hizalı).
        var sideCostPlan = await BuildSideCostPlanAsync(entity);

        return await _substitutionPlanProvider.ApplyAsync<SalesChannelTrTrendyolProductStockItem>(
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
                await SaveAttributesAndReconcileAsync(entity, new List<SalesChannelTrTrendyolProductAttributeDto> { attributeInput });

                // Upsert sonrası geri yazılmış GERÇEK id'ler — girdi sırası korunur (binding i ↔ ValueIds[i]).
                return (attributeInput.Id, attributeInput.Values.Where(v => !v.IsDeleted).Select(v => v.Id).ToList());
            },
            loadCombinationHeadersAsync: async () => await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == entity.Id && h.CombinationSignature != null)),
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

    /// <summary>Kanal-nötr upsert planı → Trendyol attribute DTO'su. Silinen değerde yalnız Id + IsDeleted taşınır
    /// (mevcut davranışla birebir — SaveAttributesGraphAsync silme dalı yalnız Id'ye bakar).</summary>
    private static SalesChannelTrTrendyolProductAttributeDto ToCombinationAttributeDto(SubstitutionCombinationAttributeUpsert upsert)
    {
        return new SalesChannelTrTrendyolProductAttributeDto
        {
            Id           = upsert.AttributeId,
            Name         = upsert.Name,
            DisplayOrder = upsert.DisplayOrder,
            Values = upsert.Values
                .Select(v => v.IsDeleted
                    ? new SalesChannelTrTrendyolProductAttributeValueDto { Id = v.Id, IsDeleted = true }
                    : new SalesChannelTrTrendyolProductAttributeValueDto { Id = v.Id, Value = v.ValueText, DisplayOrder = v.DisplayOrder })
                .ToList(),
        };
    }

    /// <summary>Köprü, kombinasyon StockItem REÇETESİNİN sahibidir: mevcut satırlar silinir + plan satırları yazılır.
    /// Persist mekaniği MEVCUT <see cref="SaveChannelRecipeLinesAsync"/> (paralel kayıt yolu açılmaz).</summary>
    private async Task ReplaceChannelRecipeLinesAsync(
        SalesChannelTrTrendyolProduct channelProduct, Guid stockItemId, List<ProductRecipeLineGraphDto> freshLines)
    {
        var existing = await AsyncExecuter.ToListAsync(
            (await _channelRecipeLineRepository.GetQueryableAsync())
                .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && r.StockItemId == stockItemId));
        var lines = existing
            .Select(r => new ProductRecipeLineGraphDto { Id = r.Id, IsDeleted = true, ComponentType = r.ComponentType })
            .Concat(freshLines)
            .ToList();
        await SaveChannelRecipeLinesAsync(channelProduct, stockItemId, lines);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> PushToTrendyolAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        var pushNotices = new List<string>();

        try
        {
            // Veri kurulumu da try İÇİNDE — geçici-link hataları dahil MarkSyncFailed'e düşsün (N11 ile aynı).
            var candidates = new List<TrendyolSkuPushCandidate>();
            var data = await BuildProductDataAsync(entity, warnings: null, notices: pushNotices, candidates: candidates);
            var result = await _client.SubmitProductAsync(data, CredentialsOf(channel));

            // SKU DONDURMA (2026-08-08 düzeltmesi): barkodlar ancak gönderim yapıldıktan SONRA kalıcılaşır —
            // push başarısızsa DB'ye bayat barkod donmasın diye plan aşaması mutasyonsuzdu. Bu çağrı eksikti:
            // kendi push'umuzla açılan kayıt SKU satırı almıyor, dolayısıyla hafif senkron o üründe kalıcı olarak
            // "NotPushedYet" veriyordu. Bugüne kadar görünmemesinin tek sebebi canlıdaki 103 kaydın TAMAMININ
            // import kaynaklı olması (SKU'ları UpsertImportedSku'dan geliyor).
            entity.ReconcileSkus(candidates);

            // "Ne gönderdim" — batch COMPLETED olunca LastSent*'e terfi edecek (bkz. FinalizeCompletedBatchAsync).
            // İçerik üçlüsü (başlık/eksen/görsel) da submit anında saklanır: defter satırı finalize'da ancak
            // BURADAN yazılabilir — o anda yeniden hesaplamak "göndermediğini yazma" hatasına girerdi. Görsel
            // kimlikleri GÖVDEYE FİİLEN GİREN setten (data.SentMediaIds) — adayları yeniden çözmek, geçici link
            // alamayıp düşen görseli de "gönderildi" diye yazardı (bağımsız denetim bulgusu, 2026-08-14).
            var pushedMediaIds = string.Join(",", data.SentMediaIds);
            foreach (var item in data.Items)
            {
                entity.RecordPendingSkuPush(
                    item.Barcode, item.Quantity, item.ListPrice, item.SalePrice,
                    title: data.Title,
                    optionsText: item.OptionLabels is { Count: > 0 } labels
                        ? string.Join("; ", labels.Select(o => o.Name + "=" + o.Value))
                        : null,
                    mediaIdsCsv: pushedMediaIds.Length > 0 ? pushedMediaIds : null);
            }

            entity.MarkSubmitted(result.BatchRequestId, "ProductV2OnBoarding", Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // Hatayı kaydet (kullanıcı görsün) + yeniden fırlat (toast). Gizleme YOK — kayıt + propagate.
            entity.MarkSyncFailed(_describer.Describe(ex), Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        dto.SyncWarnings = pushNotices;
        return dto;
    }

    /// <summary>HAFİF fiyat/stok senkronu — ürün içeriğine dokunmadan yalnız adet/fiyat yazar (N11'deki eşi).
    ///
    /// <para><b>Neden var:</b> çapraz-kanal aşırı satış deliğinin kapanışı. N11'den gelen bir sipariş stoğu düşürür;
    /// Trendyol bu yol olmadan bir sonraki TAM push'a kadar bayat adedi göstermeye devam eder.</para>
    ///
    /// <para><b>LastSent* BURADA GÜNCELLENMEZ.</b> Trendyol yazma uçları asenkron: <c>batchRequestId</c> döner,
    /// gerçek yazım ancak batch COMPLETED olunca kesinleşir. Şimdi güncellenseydi bir sonraki tur "değişiklik yok"
    /// der ve hiç yazılmamış fiyat/stok sessizce atlanırdı — dirty-tracking'in en sinsi tuzağı. Güncelleme
    /// batch finalizasyonunun işidir (P5).</para>
    ///
    /// <para><b>HK-3 GEÇİŞ KİPİ (2026-08-08 kararı):</b> hiç doğrulanmış varyantı olmayan ürün senkron kapsamı
    /// DIŞINDA kalır ve "doğrulama bekliyor" uyarısı döner — adet-0 ile topluca kapatılmaz. Bedeli bilinçlidir:
    /// o ürünlerde bayat adet pazaryerinde kalır (bugünkü durumun aynısı). İlk varyant doğrulanır doğrulanmaz
    /// ürün kendiliğinden tam simetriye girer. Gerekçe: Trendyol'a bugüne kadar hiç push yapılmadığı için
    /// "otorite devri"nin koruyacağı bir şey yok, ama adet-0 canlı 103 listelemeyi bugün kapatırdı.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> SyncStockAndPriceAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);

        // Ön koşul: en az bir SKU DONMUŞ olmalı. İçe aktarılmış kayıtlarda bu satırlar import'tan gelir
        // (UpsertImportedSku) → canlıdaki 103 listeleme bu yoldan senkronlanabilir.
        if (entity.Skus.Count == 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:NotPushedYet");
        }

        // ÇİFTE BATCH KORUMASI: önceki fiyat/stok batch'i hâlâ işleniyorsa yeni submit YAPILMAZ. Trendyol aynı
        // gövdeyi 15 dk içinde mükerrer sayıp reddediyor; üstelik iki açık batch'in hangisinin kazandığı belirsiz.
        // Tip AYRIMI YAPILMAZ (2026-08-08 düzeltmesi): entity kayıt başına TEK BatchRequestId yuvası taşıyor.
        // Guard yalnız fiyat/stok batch'ini bekleseydi, devam eden bir CREATE batch'i üzerine senkron submit
        // edilir ve create'in makbuzu KALICI olarak kaybolurdu — o push'un akıbeti bir daha sorgulanamazdı.
        if (entity.Status == "PROCESSING")
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:BatchInProgress")
                .WithData("BatchType", entity.LastBatchRequestType ?? "-");
        }

        var syncWarnings = new List<string>();

        try
        {
            var product = await GetOwnedProductAsync(entity.ProductId);
            var rowSet = await BuildPushRowsAsync(entity, product, syncWarnings);
            var rows = rowSet.Rows;

            EnsurePushRowsPriced(rows);
            EnsurePushRowsWithinPriceBand(entity, rows);

            // HK-3 GEÇİŞ KİPİ: HİÇ aday satır yoksa ürün senkron kapsamı DIŞINDA kalır (adet-0 gönderilmez).
            // Bu kontrol SKU döngüsünden ÖNCEDİR — aşağıdaki adet-0 dalının bu kararı ezmemesi için.
            if (rows.Count == 0)
            {
                syncWarnings.Add(rowSet.PendingVerificationCount > 0
                    ? L["TrendyolProduct:SyncSkippedPendingVerification"]
                    : L["TrendyolProduct:NoSyncableSku"]);

                return await SaveAndMapAsync(entity, syncWarnings);
            }

            var items = new List<TrendyolPriceInventoryItem>();
            var anyDirty = false;
            var closedSkuCount = 0;

            foreach (var sku in entity.Skus)
            {
                // ERP satırı varyant id'siyle, Trendyol-only satırı StockItem id'siyle eşleşir — SKU satırı
                // (J3) ProductVariantId alanında ikisinden uygun olanı taşır.
                var row = rows.FirstOrDefault(r => r.CandidateId == sku.ProductVariantId);

                if (row is null)
                {
                    // ÜRÜN KAPSAMDA AMA BU SKU DEĞİL → ADET 0 (2026-08-08 düzeltmesi).
                    //
                    // Bu satırı SESSİZCE ATLAMAK en sinsi aşırı satış deliğiydi: varyant kapıya takıldığı
                    // (askıya alındı · doğrulaması bayatladı · yeni ve Draft) için aday olmuyor, ama Trendyol'da
                    // SON GÖNDERİLEN adetle CANLI duruyor ve sipariş almaya devam ediyordu. Üstelik bir daha
                    // ASLA tazelenmiyordu — her turda aynı `continue`. Sistem "bu varyant satılmamalı" kararını
                    // kendisi verip pazaryerine hiç bildirmemiş oluyordu.
                    //
                    // Fiyat DOKUNULMAZ (null = "bu alana dokunma"): amaç satışı durdurmak, listelemeyi bozmak
                    // değil — stok/doğrulama geri gelince normal dal gerçek adedi kendiliğinden yazar.
                    // Bu, §6 ① kararının ("0 kurulabilir varyant → adet 0") SKU granülünde uygulanışıdır.
                    anyDirty |= sku.LastSentQuantity != 0;
                    closedSkuCount++;

                    items.Add(new TrendyolPriceInventoryItem(
                        Barcode: sku.Barcode, Quantity: 0, ListPrice: null, SalePrice: null));
                    continue;
                }

                anyDirty |= sku.LastSentQuantity != row.Stock
                    || sku.LastSentListPrice != row.ListPrice
                    || sku.LastSentSalePrice != row.SalePrice;

                items.Add(new TrendyolPriceInventoryItem(
                    Barcode: sku.Barcode,
                    Quantity: row.Stock,
                    ListPrice: row.ListPrice,
                    SalePrice: row.SalePrice));
            }

            if (items.Count == 0)
            {
                syncWarnings.Add(L["TrendyolProduct:NoSyncableSku"]);
                return await SaveAndMapAsync(entity, syncWarnings);
            }

            // Kapatılan SKU'lar SESSİZ DEĞİLDİR — kullanıcı kaç varyantının satıştan çekildiğini görür.
            if (closedSkuCount > 0)
            {
                syncWarnings.Add(L["TrendyolProduct:SkusZeroedNotSellable", closedSkuCount]);
            }

            if (!anyDirty)
            {
                // Değişiklik yok → Trendyol'a gereksiz yazma yapma (kotaya + 15 dk mükerrer kuralına saygı).
                syncWarnings.Add(L["TrendyolProduct:NoChangesToSync"]);
                return await SaveAndMapAsync(entity, syncWarnings);
            }

            var result = await _client.UpdatePriceAndInventoryAsync(items, CredentialsOf(channel));

            // "NE GÖNDERDİM" ŞİMDİ kaydedilir — LastSent* değil, PendingSent* (2026-08-08 kararı "c").
            // Finalizasyon dakikalar sonra çalışıyor ve o an bu değerleri yeniden üretemez: ürün değişmiş
            // olabilir. Burada yazmazsak "gönderileni" tahmin etmek zorunda kalırdık.
            foreach (var item in items)
            {
                entity.RecordPendingSkuPush(item.Barcode, item.Quantity, item.ListPrice, item.SalePrice);
            }

            entity.MarkSubmitted(result.BatchRequestId, PriceInventoryBatchType, Clock.Now.ToUniversalTime());
            return await SaveAndMapAsync(entity, syncWarnings);
        }
        catch (Exception ex)
        {
            entity.MarkSyncFailed(_describer.Describe(ex), Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }
    }

    /// <summary>Fiyat/stok batch'inin tip etiketi — <see cref="SalesChannelTrTrendyolProduct.LastBatchRequestType"/>'a
    /// yazılır ve çifte-batch korumasında okunur. Tek yerde tanımlı: iki yazımın ayrışması korumayı sessizce
    /// devre dışı bırakırdı.</summary>
    private const string PriceInventoryBatchType = "PriceAndInventory";

    private static void EnsurePushRowsPriced(List<TrendyolPushRow> rows)
    {
        var unpriced = rows.FirstOrDefault(r => r.ListPrice is null || r.SalePrice is null || r.Stock is null);
        if (unpriced is not null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:PriceMissingForPush")
                .WithData("StockCode", unpriced.Code);
        }
    }

    /// <summary>COMPLETED batch'i YEREL GERÇEĞE işler — bu dilimin kalbi.
    ///
    /// <para><b>Neden burada, submit anında değil:</b> Trendyol yazma uçları asenkron ve batch REDDEDİLEBİLİR.
    /// Submit anında <c>LastSent*</c> yazsaydık bir sonraki tur "değişiklik yok" der, hiç yazılmamış fiyat/stok
    /// sessizce atlanırdı. Bu yüzden kıyas tabanı ancak "kabul edildi" kanıtlandığında dolar.</para>
    ///
    /// <para><b>FAILED'da <c>LastSent*</c> yazılmaz</b> — reddedilen bir gönderimi kıyas tabanına terfi
    /// ettirmek, hiç ulaşmamış fiyatı "senkron" göstermek olurdu. <b>Geçmişe ise BAŞARISIZ satır yazılır</b>
    /// (2026-08-10): eskiden hiçbir iz kalmıyordu ve "denendi, reddedildi" ile "hiç denenmedi" ayırt
    /// edilemiyordu. Satır <c>Failed</c> damgası + kanalın kendi gerekçesiyle gider; başarılı görünmez.</para>
    ///
    /// <para><b>İdempotent:</b> ikinci çağrıda <c>Status</c> artık PROCESSING olmadığından (ve çağıranlar
    /// yalnız PROCESSING kayıtları seçtiğinden) tekrar yazılmaz. Kısmi başarıda (<c>FailedCount &gt; 0</c>)
    /// de yazılmaz: hangi SKU'nun düştüğü item kırılımından güvenilir biçimde eşlenemiyor, o yüzden
    /// fail-closed davranıp tabanı KİRLETMİYORUZ — bir sonraki senkron her şeyi yeniden gönderir.</para></summary>
    private async Task FinalizeCompletedBatchAsync(
        SalesChannelTrTrendyolProduct entity,
        TrendyolBatchStatus status,
        string? batchType,
        string? batchRequestId)
    {
        // ARA DURUM = HENÜZ SONUÇ YOK — hiçbir şey yapılmaz (bağımsız denetim bulgusu, 2026-08-14): eskiden
        // COMPLETED dışındaki HER kök durum ret sayılıyordu; Trendyol batch'i henüz işlemedeyken (IN_PROGRESS /
        // PROCESSING) sorgulanırsa bekleyen içerik SİLİNİYOR, deftere gerekçesi "ara durum adı" olan sahte bir
        // Failed satırı düşüyor ve batch gerçekten bitince ne LastSent* terfi ediyor ne Succeeded yazılıyordu.
        // Sonuç ancak terminal durumda okunur; sorgu bir sonraki turda yinelenir (worker PROCESSING seçer).
        if (IsNonTerminalBatchStatus(status.Status))
        {
            return;
        }

        var completed = string.Equals(status.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

        // KISMİ BAŞARI da başarısızlık sayılır: hangi SKU'nun düştüğü item kırılımından güvenilir biçimde
        // eşlenemiyor. Tabanı yarım terfi ettirmek, düşen SKU'yu "senkron" göstermek olurdu → fail-closed.
        if (!completed || status.FailedCount > 0)
        {
            // Reddedildi/kısmen düştü → bekleyenler ATILIR, LastSent* DEĞİŞMEZ. Bir sonraki senkron aynı
            // farkı yeniden görür ve yeniden gönderir.
            //
            // GEÇMİŞE BAŞARISIZ SATIR YAZILIR (2026-08-10 Hakan kararı). Eskiden hiçbir şey yazılmıyordu ve
            // bu, "denendi ve reddedildi" ile "hiç denenmedi"yi ayırt edilemez kılıyordu — otonom fiyat/stok
            // güncellemesinde bir fiyatın kanala yansımama sebebi hiçbir yerde kalmıyordu. Satır Failed
            // damgasıyla ve KANALIN KENDİ mesajıyla yazılır; başarılı görünme riski yok.
            //
            // SIRA ÖNEMLİ: bekleyenler temizlenmeden ÖNCE toplanır — temizlik sonrası ne gönderilmeye
            // çalışıldığı bilgisi kaybolur.
            var attempted = CollectPendingEntries(entity);

            if (status.Status is not null)
            {
                entity.ClearPendingSkuPushes();
            }

            await _historyRecorder.RecordAsync(
                entity.CompanyId,
                entity.Id,
                ResolvePushKind(batchType),
                attempted,
                batchRequestId,
                ChannelPushOutcome.Failed,
                DescribeBatchFailure(status));

            return;
        }

        // Terfi ÖNCESİ topla: geçmişe yazılacak olan GÖNDERİLEN değerlerdir (terfi sonrası ikisi de aynı olur
        // ama bekleyeni olmayan SKU'ları ayırt edebilmek için sıra önemli — o SKU'lar bu gönderime dahil değildi).
        var entries = CollectPendingEntries(entity);

        entity.PromotePendingSkuPushes();

        await _historyRecorder.RecordAsync(
            entity.CompanyId,
            entity.Id,
            ResolvePushKind(batchType),
            entries,
            batchRequestId,
            ChannelPushOutcome.Succeeded);
    }

    /// <summary>Bu batch'te GÖNDERİLMEYE ÇALIŞILAN SKU değerleri. Bekleyeni olmayan SKU bu gönderime dahil
    /// değildi → geçmişe girmez. Hem başarı hem ret dalı aynı kaynaktan okur ki iki dal ayrışmasın.</summary>
    private static List<TrendyolPushHistoryEntry> CollectPendingEntries(SalesChannelTrTrendyolProduct entity)
    {
        return entity.Skus
            .Where(s => s.PendingSentQuantity is not null
                        || s.PendingSentListPrice is not null
                        || s.PendingSentSalePrice is not null)
            .Select(s => new TrendyolPushHistoryEntry(
                Barcode: s.Barcode,
                ListPrice: s.PendingSentListPrice,
                SalePrice: s.PendingSentSalePrice,
                Quantity: s.PendingSentQuantity,
                Title: s.PendingSentTitle,
                Options: s.PendingSentOptions,
                MediaIds: ParseMediaIdsCsv(s.PendingSentMediaIds)))
            .ToList();
    }

    /// <summary>Pending'teki virgüllü MediaId listesini (Guid metni; biçim-agnostik parse) çözer — bozuk parça sessizce atlanır
    /// (delilin kalanını düşürmek, tamamını kaybetmek olurdu).</summary>
    private static IReadOnlyList<Guid>? ParseMediaIdsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Guid.TryParse(part, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        return ids.Count > 0 ? ids : null;
    }

    private static TrendyolProductPushKind ResolvePushKind(string? batchType)
    {
        return string.Equals(batchType, PriceInventoryBatchType, StringComparison.Ordinal)
            ? TrendyolProductPushKind.PriceStockSync
            : TrendyolProductPushKind.Create;
    }

    /// <summary>Reddin gerekçesi — KANALIN kendi mesajı esastır. Trendyol mesaj döndürmediğinde (kısmi
    /// başarıda sık) elde kalan tek bilgi durum + kaç kalemin düştüğüdür; onu yazmak, boş bırakıp
    /// "sebep bilinmiyor" izlenimi vermekten iyidir.</summary>
    private static string DescribeBatchFailure(TrendyolBatchStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.FailureReasons))
        {
            return status.FailureReasons!;
        }

        var state = string.IsNullOrWhiteSpace(status.Status) ? "UNKNOWN" : status.Status!;

        return status.FailedCount > 0
            ? $"{state} ({status.FailedCount}/{status.ItemCount} kalem başarısız)"
            : state;
    }

    /// <summary>Trendyol batch kök durumu terminal DEĞİL mi (işleme sürüyor). Boş/null durum da "bilinmiyor" —
    /// sonuç sayılmaz, bir sonraki sorguya bırakılır (fail-closed: bilinmeyeni ret sanmak defteri kirletir).</summary>
    private static bool IsNonTerminalBatchStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        return string.Equals(status, "PROCESSING", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SalesChannelTrTrendyolProductDto> SaveAndMapAsync(
        SalesChannelTrTrendyolProduct entity, List<string> syncWarnings)
    {
        await _repository.UpdateAsync(entity, autoSave: true);
        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        dto.SyncWarnings = syncWarnings;
        return dto;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> RefreshStatusAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        if (string.IsNullOrEmpty(entity.BatchRequestId))
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:NoBatch");
        }

        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);

        try
        {
            var status = await _client.GetBatchStatusAsync(entity.BatchRequestId, CredentialsOf(channel));
            var error = status.FailedCount > 0 ? status.FailureReasons : null;
            var batchType = entity.LastBatchRequestType;
            var batchId = entity.BatchRequestId;

            entity.MarkStatus(status.Status, status.FailedCount, error, Clock.Now.ToUniversalTime());

            // Batch GERÇEĞE dönüştüğü an — dirty-check'in kıyas tabanı ancak burada dolar.
            await FinalizeCompletedBatchAsync(entity, status, batchType, batchId);

            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            entity.MarkSyncFailed(_describer.Describe(ex), Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        return ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<TrendyolPushPreviewDto> GetPushPreviewAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var product = await GetOwnedProductAsync(entity.ProductId);
        var warnings = new List<string>();

        // BuildProductData'yı READ-ONLY çalıştır (SUBMIT YOK) — fail-fast'ler warnings'e düşer, fırlatmaz.
        TrendyolProductData? data;
        try
        {
            data = await BuildProductDataAsync(entity, warnings);
        }
        catch (BusinessException ex)
        {
            // Savunma amaçlı: warnings modunda normalde fırlamaz; yine de önizleme dönsün diye uyarıya çevir.
            data = null;
            warnings.Add(ex.Code is { Length: > 0 } code ? L[code].Value : ex.Message);
        }

        // Kategori attribute tanımları (best-effort; ad + zorunlu çözümü). Alınamazsa id-only + zorunlu denetimi atlanır.
        var attrDefs = await TryLoadLeafAttributesAsync(entity.CategoryId);
        AppendRequiredFieldWarnings(entity, attrDefs, warnings);

        var previewProduct = new TrendyolPreviewProductDto
        {
            ProductMainId = entity.ProductMainId,
            Title = product.Name,
            CategoryId = entity.CategoryId,
            CategoryName = entity.CategoryName,
            BrandId = entity.BrandId,
            BrandName = entity.BrandName,
            VatRate = entity.VatRate,
            DimensionalWeight = entity.DimensionalWeight,
            DeliveryDuration = entity.DeliveryDuration,
            FastDeliveryType = entity.FastDeliveryType,
            HasDescription = !string.IsNullOrWhiteSpace(entity.Description ?? product.Description),
            ImageCount = data?.ImageUrls.Count ?? 0,
            Attributes = BuildAttributeSummary(entity.Attributes, attrDefs),
        };

        // Kalemler (barcode başına) — BuildProductData'nın Items'ı + varyant eksen özeti (StockCode = varyant kodu ile eşle).
        var optionsByStockCode = await LoadVariantOptionSummariesAsync(entity.ProductId);
        var items = (data?.Items ?? new List<TrendyolProductItem>()).Select(it => new TrendyolPreviewItemDto
        {
            Barcode = it.Barcode,
            StockCode = it.StockCode,
            Quantity = it.Quantity,
            ListPrice = it.ListPrice,
            SalePrice = it.SalePrice,
            // ERP satırı: varyant kodu üzerinden özet; Trendyol-only satır: item'ın kendi kombinasyon çiftleri
            // (kod eşleşmez — kombinasyon kodu ERP kodu değildir; boş kalması denetim bulgusuydu).
            Options = optionsByStockCode.TryGetValue(it.StockCode, out var opt)
                ? opt
                : it.OptionLabels is { Count: > 0 } labels
                    ? string.Join("; ", labels.Select(o => $"{o.Name}: {o.Value}"))
                    : string.Empty,
        }).ToList();

        return new TrendyolPushPreviewDto { Product = previewProduct, Items = items, Warnings = warnings };
    }

    /// <summary>Yaprak kategori attribute tanımlarını best-effort çeker (önizleme). Kategori boşsa (opsiyonel alan)
    /// ya da REST/kimlik hatası varsa boş liste döner — önizleme KIRILMAZ (ad çözümü id'ye düşer, zorunlu denetimi atlanır).</summary>
    private async Task<List<TrendyolLeafAttributeDto>> TryLoadLeafAttributesAsync(string? categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return new List<TrendyolLeafAttributeDto>();
        }

        try
        {
            return await _categoryAppService.GetLeafAttributesAsync(categoryId);
        }
        catch (Exception)
        {
            // Önizleme best-effort: tanımlar alınamazsa uyarı zenginleştirmesi düşer, önizleme yine döner.
            return new List<TrendyolLeafAttributeDto>();
        }
    }

    /// <summary>Eksik zorunlu alan uyarıları (T6 read-only; TAM push validator T8 kapsamı): kategori/marka boş +
    /// zorunlu (Required, varyant-ekseni-olmayan) kategori attribute'u eksik.</summary>
    private void AppendRequiredFieldWarnings(SalesChannelTrTrendyolProduct entity, List<TrendyolLeafAttributeDto> attrDefs, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(entity.CategoryId))
        {
            warnings.Add(L["TrendyolProduct:Preview:CategoryRequired"].Value);
        }

        if (string.IsNullOrWhiteSpace(entity.BrandId))
        {
            warnings.Add(L["TrendyolProduct:Preview:BrandRequired"].Value);
        }

        foreach (var def in attrDefs.Where(a => a.Required && !a.Varianter))
        {
            var filled = entity.Attributes.Any(a => a.AttributeId == def.AttributeId
                && (a.AttributeValueId is not null || !string.IsNullOrWhiteSpace(a.CustomValue)));
            if (!filled)
            {
                warnings.Add(L["TrendyolProduct:Preview:MandatoryAttributeMissing", def.Name].Value);
            }
        }
    }

    /// <summary>Ürün-seviyesi attribute özeti ("Renk: Gri; Materyal: Pamuk") — ad/değer kategori tanımından çözülür;
    /// tanım yoksa "#id: değer" (valueId ya da customValue). Boşsa boş metin.</summary>
    private static string BuildAttributeSummary(IReadOnlyCollection<SalesChannelTrTrendyolProductCategoryAttribute> attributes, List<TrendyolLeafAttributeDto> attrDefs)
    {
        if (attributes.Count == 0)
        {
            return string.Empty;
        }

        var defById = attrDefs.ToDictionary(d => d.AttributeId);
        var parts = attributes.Select(a =>
        {
            var hasDef = defById.TryGetValue(a.AttributeId, out var def);
            var name = hasDef ? def!.Name : $"#{a.AttributeId}";
            string value;
            if (a.AttributeValueId is { } valueId)
            {
                value = hasDef
                    ? def!.Values.FirstOrDefault(v => v.ValueId == valueId)?.Value ?? $"#{valueId}"
                    : $"#{valueId}";
            }
            else
            {
                value = a.CustomValue ?? string.Empty;
            }

            return $"{name}: {value}";
        });

        return string.Join("; ", parts);
    }

    /// <summary>Varyant eksen özetleri, STOK KODU bazlı ("Renk: Kırmızı; Beden: M") — push StockCode = varyant kodu
    /// olduğundan önizleme kalemleriyle kod üstünden eşleşir. Niteliksiz ürün → boş sözlük.</summary>
    private async Task<Dictionary<string, string>> LoadVariantOptionSummariesAsync(Guid productId)
    {
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId));
        if (variants.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var options = await LoadVariantOptionsAsync(productId, variants.Select(v => v.Id).ToList());
        return variants
            .Where(v => options.ContainsKey(v.Id))
            .ToDictionary(v => v.Code, v => string.Join("; ", options[v.Id].Select(p => $"{p.Name}: {p.Value}")));
    }

    /// <summary>Varyant başına ERP option çiftleri (name/value) — ProductVariantAttributeValue → attribute adı + değer.
    /// Hem önizleme eksen özetinin hem fırsatçı ERP eşleştirme indeksinin ORTAK kaynağı (N11 LoadVariantOptionsAsync paritesi).</summary>
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

    // ── Push veri kurulumu (ürün grafı → TrendyolProductData) ─────────────────────────────────────────

    /// <summary>Ürün grafından push payload'unu kurar. <paramref name="warnings"/> verilirse (T6 ÖNİZLEME) fail-fast
    /// koşulları exception yerine uyarıya çevrilir + kurulum devam eder (kısmi önizleme); null ise (gerçek push T8)
    /// eskisi gibi BusinessException fırlatır. Her iki modda da Trendyol'a HİÇBİR ŞEY gönderilmez (submit çağıran üstte).</summary>
    // KDV devralma zinciri (merkezî ChannelInheritance deseni): kanal override doluysa o, değilse ÜRÜNÜN oranı.
    // İkisi de boşsa null döner → çağıran fail-fast eder. Sessiz varsayılan BİLEREK YOK: kıymetli maden teslimi
    // KDV %0'dır (istisna faturası), işçilik %20; "hep 20" varsayımı yanlış fatura + satıcıya rücu demektir.
    private static int? ResolveVatRate(SalesChannelTrTrendyolProduct channelProduct, Product product)
    {
        return ChannelInheritance.Resolve(channelProduct.VatRate, product.VatRate);
    }

    /// <summary>Trendyol push aday satırı — ERP varyantı (<c>IsErpBacked</c>) ya da Trendyol-only kombinasyon
    /// (<c>CandidateId</c> = StockItem.Id; N11 J3 deseni — SKU satırı ProductVariantId alanında bu id'yi taşır).
    /// Fiyat/stok zinciri ÇÖZÜLMÜŞ, indirim ve emniyet payı UYGULANMIŞ hâlde taşınır; çözülemeyen değer
    /// <c>null</c> kalır (fail-fast kararı çağıranındır). <c>OptionPairs</c> yalnız Trendyol-only satırda dolu
    /// (kombinasyon imzasından çözülen ad/değer çiftleri; ERP satırının seçenekleri LoadVariantOptionsAsync'ten).</summary>
    private sealed record TrendyolPushRow(
        Guid CandidateId,
        string Code,
        string DisplayName,
        bool IsErpBacked,
        IReadOnlyList<(string Name, string Value)> OptionPairs,
        decimal? ListPrice,
        decimal? SalePrice,
        int? Stock,
        Guid? PriceCurrencyUnitId);

    /// <summary>Aday seti + kapıya takılan varyant SAYISI. Sayı taşınır çünkü "hiç aday yok" ile "hepsi doğrulama
    /// bekliyor" AYRI durumlardır ve hafif senkron ikisine farklı davranır (HK-3 geçiş kipi).</summary>
    private sealed record TrendyolPushRowSet(List<TrendyolPushRow> Rows, int PendingVerificationCount);

    /// <summary>Push · önizleme · hafif senkron için ORTAK aday satır kaynağı (N11 <c>BuildPushRowsAsync</c> portu).
    ///
    /// <para><b>PUSH KAPISI</b> (§6): yalnız İNSAN tarafından doğrulanmış ve doğrulamadan sonra reçetesi değişmemiş
    /// varyant aday olur. Kapı fiyatlamadan ÖNCEDİR — elle girilen <c>OverridePrice</c> bile kararsızlığı örtemez.
    /// Trendyol tarafında bu kapı bugüne kadar HİÇ yoktu; N11'de vardı (asimetri kapatıldı).</para>
    ///
    /// <para><b>Emniyet payı</b> tam da bu tek çıkışta uygulanır — üç çağıran da aynı paylı adedi görsün diye
    /// (N11 ile birebir gerekçe: aksi hâlde dirty-check her turda "değişti" der).</para></summary>
    private async Task<TrendyolPushRowSet> BuildPushRowsAsync(
        SalesChannelTrTrendyolProduct channelProduct, Product product, List<string>? warnings = null,
        List<string>? notices = null)
    {
        var activeVariants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id && v.IsActive));

        // Satış fiyatı/birimi ProductVariantDetail'de (agnostik EntityVariant'ın Product uzantısı).
        var salePrices = await LoadVariantSalePricesAsync(activeVariants.Select(v => v.Id).ToList());
        var priced = activeVariants
            .Where(v => salePrices.GetValueOrDefault(v.Id).SalePrice is not null)
            .ToList();

        var sellable = await _saleReadiness.ResolveSellableAsync(priced.Select(v => v.Id).ToList());
        var variants = priced
            .Where(v => sellable.Contains(v.Id))
            .OrderByDescending(v => v.IsMain)
            .ToList();

        // Zincir: OverridePrice ?? türetilmiş (reçete NetCost × marj) ?? ERP SalePrice; stok OverrideStock ?? ERP.
        var pushPricing = await ResolveVariantPushPricingAsync(channelProduct, variants);

        // ÜRÜN İNDİRİMİ (2026-08-07 Hakan kararı): Trendyol'da ayrı bir indirim ALANI yok — indirim ancak
        // listPrice (üstü çizili) / salePrice (indirimli) ayrımıyla ifade edilir, hesabı BİZ yaparız. Tarih
        // penceresi de burada gözetilir: N11'e tarihleri gönderip yorumu ona bırakıyoruz, burada bırakacak kimse
        // yok → süresi dolmuş kampanya aksi hâlde Trendyol'da sonsuza kadar açık kalırdı. Gün date-only
        // karşılaştırılır (kullanıcının günü) — UTC saat farkı kampanyayı bir gün kaydırmasın.
        // BusinessClock.Today() — Clock.Now.Date DEĞİL. ABP saati UTC'ye normalize ediyor; UTC gününe bakmak
        // kampanyayı kullanıcının gününe göre saatler önce açıp saatler geç kapatırdı (indirim penceresi
        // date-only semantiktir, §6 zaman kuralı). Yorumun "kullanıcının günü" iddiası ancak bu çağrıyla doğru.
        var today = BusinessClock.Today();

        var rows = variants.Select(v =>
        {
            var pricing = pushPricing[v.Id];
            decimal? salePrice = pricing.Price is { } listPrice
                ? ProductDiscountCalculator.ResolveSalePrice(
                    listPrice, product.DiscountType, product.DiscountValue,
                    product.DiscountStartDate, product.DiscountEndDate, today)
                : null;

            return new TrendyolPushRow(
                CandidateId: v.Id,
                Code: v.Code,
                DisplayName: v.Name,
                IsErpBacked: true,
                OptionPairs: Array.Empty<(string Name, string Value)>(),
                ListPrice: pricing.Price,
                SalePrice: salePrice,
                Stock: ChannelPushGuard.ApplySafetyStock(pricing.Stock, channelProduct.SafetyStock),
                PriceCurrencyUnitId: salePrices.GetValueOrDefault(v.Id).CurrencyUnitId);
        }).ToList();

        // TRENDYOL-ONLY kombinasyonlar (T8 — N11 J3 portu): özellik-modu başlıkları içinde ERP karşılığı
        // olmayanlar da satılabilir satırdır; bugüne dek push'a HİÇ girmiyorlardı. Fiyat = Override ??
        // (kanal reçetesi NetCost × marj); stok YALNIZ Override (ERP fallback yok — ERP'de sayacak varyant
        // yok; "sınırsız" saymak aşırı satış kapısı olurdu). Satış-hazırlık kapısı ERP varyantına aittir,
        // bu satırlar ondan geçmez (N11 ile aynı duruş). Emniyet payı ve indirim ERP satırlarıyla AYNI
        // kurallarla uygulanır — kaynak farkı emniyet farkı üretmesin.
        var onlyHeaders = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id
                            && h.CombinationSignature != null
                            && h.ProductVariantId == null));
        if (onlyHeaders.Count > 0)
        {
            var attributeEntities = await LoadChannelAttributeEntitiesAsync(channelProduct.Id);
            var valueEntities = await LoadChannelAttributeValueEntitiesAsync(attributeEntities.Select(a => a.Id).ToList());
            var attributeById = ToAttributeWithValues(attributeEntities, valueEntities).ToDictionary(a => a.AttributeId);
            var onlyPricing = await ResolveTrendyolOnlyPushPricingAsync(channelProduct, onlyHeaders);

            foreach (var header in onlyHeaders.OrderBy(h => h.CreationTime))
            {
                var pairs = ResolveCombinationPairs(header.CombinationSignature!, attributeById);
                var pricing = onlyPricing[header.Id];

                // NE FİYATI NE STOĞU olan kombinasyon SATILABİLİR KALEM DEĞİLDİR — aday olmaz, uyarıyla bildirilir.
                // Kartezyen reconcile bu başlıkları override'sız OTOMATİK açar; hepsini aday sayıp fail-fast'e
                // sokmak, kullanıcı hiçbirine dokunmadan ürünün TÜM ERP senkronunu durdururdu (bu dilim öncesi
                // ERP SKU'ları senkronlanıyordu — regresyon olurdu). Kısmen dolu satır (fiyat var stok yok ya da
                // tersi) belirsizliktir ve fail-fast'e KALIR: yarım kalmış bir niyet sessiz geçilmez.
                if (pricing.Price is null && pricing.Stock is null)
                {
                    var label = string.Join("; ", pairs.Select(p => $"{p.Name}: {p.Value}"));
                    (warnings ?? notices)?.Add(L["TrendyolProduct:TrendyolOnlyCombinationSkipped", label].Value);
                    continue;
                }

                decimal? salePrice = pricing.Price is { } listPrice
                    ? ProductDiscountCalculator.ResolveSalePrice(
                        listPrice, product.DiscountType, product.DiscountValue,
                        product.DiscountStartDate, product.DiscountEndDate, today)
                    : null;

                rows.Add(new TrendyolPushRow(
                    CandidateId: header.Id,
                    Code: BuildCombinationCode(pairs, channelProduct.SequenceNo),
                    DisplayName: string.Join("; ", pairs.Select(p => $"{p.Name}: {p.Value}")),
                    IsErpBacked: false,
                    OptionPairs: pairs,
                    ListPrice: pricing.Price,
                    SalePrice: salePrice,
                    Stock: ChannelPushGuard.ApplySafetyStock(pricing.Stock, channelProduct.SafetyStock),
                    PriceCurrencyUnitId: header.OverridePriceCurrencyUnitId));
            }
        }

        // "ADAY YOK" kontrolü satırlar TAMAMEN kurulduktan SONRA (Trendyol-only dahil — N11 portuyla aynı yer):
        // ERP varyantlarının hepsi kapıya takılmış olsa bile override'lı bir kombinasyon tek başına push'u ayakta
        // tutar. Kontrol önce olsaydı o ürün push'a başlamadan ölürdü (bağımsız denetim bulgusu, 2026-08-14).
        if (rows.Count == 0)
        {
            // TEŞHİS DOĞRU SEBEBİ SÖYLER (2026-08-08 düzeltmesi): doğrulama kapısı yeni bir "aday yok" sebebi
            // ekledi. Fiyatlı varyant VARDI ama hepsi kapıya takıldıysa "fiyatlı varyant yok" demek kullanıcıyı
            // olmayan bir sorunu aramaya yollar — fiyatları kontrol eder, hepsi doğru görünür, tıkanır.
            var code = priced.Count > 0
                ? "TradeXpress:Trendyol:Product:NoVerifiedVariant"
                : "TradeXpress:Trendyol:Product:NoPricedVariant";

            if (warnings is null)
            {
                throw new BusinessException(code);
            }

            warnings.Add(L[code].Value);
        }

        return new TrendyolPushRowSet(rows, priced.Count - variants.Count);
    }

    /// <summary>Trendyol-only satır fiyat/stok çözümü — N11 <c>ResolveN11OnlyPushPricingAsync</c> portu.
    /// Fiyat: <c>OverridePrice ?? (kaydedilmiş kanal reçetesi NetCost × (1+Margin/100))</c>; kursuz birimde
    /// (NetCostMissingRate) türetilmiş fiyat null kalır — uydurma fiyat üretilmez. Stok: YALNIZ OverrideStock.
    /// Çözülemeyen değer null taşınır; kararı çağıran verir (push fail-fast · önizleme boş gösterir).</summary>
    private async Task<Dictionary<Guid, TrendyolOnlyPricing>> ResolveTrendyolOnlyPushPricingAsync(
        SalesChannelTrTrendyolProduct channelProduct, List<SalesChannelTrTrendyolProductStockItem> headers)
    {
        var result = new Dictionary<Guid, TrendyolOnlyPricing>();
        if (headers.Count == 0)
        {
            return result;
        }

        var headerIds = headers.Select(h => h.Id).ToList();
        var linesByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id
                                && headerIds.Contains(r.StockItemId))))
            .GroupBy(r => r.StockItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lineSets = headers
            .Select(h => linesByHeader.TryGetValue(h.Id, out var lines)
                ? MapSavedRecipeLines(lines)
                : new List<ProductRecipeLineGraphDto>())
            .ToList();
        var costs = await _recipeCostPopulator.PopulateAsync(lineSets);

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            decimal? derived = costs[i].NetCost is { } netCost && !costs[i].NetCostMissingRate
                ? DerivedPriceCalculator.Calculate(netCost, header.Margin)
                : null;
            result[header.Id] = new TrendyolOnlyPricing(header.OverridePrice ?? derived, header.OverrideStock);
        }

        return result;
    }

    private sealed record TrendyolOnlyPricing(decimal? Price, int? Stock);

    /// <summary>Kombinasyon imzasını ("{AttrId}={ValueId}|...") ad/değer çiftlerine çözer — N11
    /// <c>ResolveCombinationPairs</c> portu. Bozuk/bayat çift sessizce atlanır (reconcile orphan'ı zaten siler).</summary>
    private static List<(string Name, string Value)> ResolveCombinationPairs(
        string signature, Dictionary<Guid, AttributeWithValues> attributeById)
    {
        var pairs = new List<(string Name, string Value)>();
        foreach (var part in signature.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var segments = part.Split('=');
            if (segments.Length != 2
                || !Guid.TryParse(segments[0], out var attributeId)
                || !Guid.TryParse(segments[1], out var valueId)
                || !attributeById.TryGetValue(attributeId, out var attribute))
            {
                continue;
            }

            pairs.Add((attribute.AttributeName, attribute.Values.FirstOrDefault(v => v.ValueId == valueId).Value ?? string.Empty));
        }

        return pairs;
    }

    /// <summary>Kombinasyon değerlerinden deterministik stok kodu GÖVDESİ — N11 <c>BuildCombinationCode</c>
    /// portu ("SIYAH-42" deseni): yalnız DEĞERLER '-' ile birleşir, UPPER-invariant, "-{SequenceNo}" son eki
    /// payı düşülerek üst sınıra kesilir (son eki entity <c>BuildBarcode</c>/ChannelSequenceCode ekler).</summary>
    private static string BuildCombinationCode(List<(string Name, string Value)> pairs, int sequenceNo)
    {
        var joined = string.Join("-", pairs.Select(p => p.Value)).ToUpperInvariant();
        var suffixLength = sequenceNo.ToString(CultureInfo.InvariantCulture).Length + 1;
        var maxLength = TrendyolProductConsts.StockCodeMaxLength - suffixLength;
        return joined.Length <= maxLength ? joined : joined[..maxLength];
    }

    /// <summary>Push satırlarının fiyatı kanal-ürünün <c>[MinPrice, MaxPrice]</c> bandında mı — N11 ile birebir
    /// kural (gövde <see cref="ChannelPushGuard"/>'da). İhlalde ÜRÜNÜN TÜM push'u düşer; kırpma YOK.
    /// Bant kontrolü <b>satış</b> fiyatına uygulanır: müşterinin ödediği sayı odur.</summary>
    private static void EnsurePushRowsWithinPriceBand(SalesChannelTrTrendyolProduct channelProduct, List<TrendyolPushRow> rows)
    {
        if (channelProduct.MinPrice is null && channelProduct.MaxPrice is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            ChannelPushGuard.EnsureWithinPriceBand(
                "Trendyol", row.Code, row.SalePrice, channelProduct.MinPrice, channelProduct.MaxPrice);
        }
    }

    /// <param name="warnings">DOLU ise ÖNİZLEME kipi: eksik zorunlu alanlar fırlatmak yerine buraya yazılır.</param>
    /// <param name="notices">Kipten BAĞIMSIZ bildirimler (gerçek push'ta da dolar). <paramref name="warnings"/>'ten
    /// ayrı olması şart: onu doldurmak fail-fast'leri uyarıya çevirip push guard'larını devre dışı bırakırdı.</param>
    private async Task<TrendyolProductData> BuildProductDataAsync(
        SalesChannelTrTrendyolProduct channelProduct, List<string>? warnings = null, List<string>? notices = null,
        List<TrendyolSkuPushCandidate>? candidates = null)
    {
        var product = await GetOwnedProductAsync(channelProduct.ProductId);

        // Kategori KAYITTA opsiyonel (gevşek kategori, 2026-07-11) ama Trendyol create şemasında ZORUNLU →
        // kategorisiz listeleme GERÇEK push'ta dostane fail-fast. Önizleme modunda (warnings dolu) fırlatılmaz;
        // uyarı AppendRequiredFieldWarnings'ten zaten gelir (duplike uyarı üretme).
        if (warnings is null && string.IsNullOrWhiteSpace(channelProduct.CategoryId))
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:CategoryRequired");
        }

        // KDV oranı zorunlu: kanalda da üründe de yoksa push YAPILMAZ. Eskiden entity ctor'ı sessizce 20
        // atıyordu ve kullanıcı hiçbir şeye dokunmazsa kıymetli maden %20 ile listeleniyordu — yanlış fatura.
        if (warnings is null && ResolveVatRate(channelProduct, product) is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:VatRateRequired");
        }

        // GÖRSEL VARLIĞI kimlik listesinden denetlenir (ucuz, dış-ağsız): görselsiz ürün gerçek push'ta fail-fast,
        // önizlemede uyarı. Adres ÜRETİMİ (geçici barındırmaya yükleme = dış-ağ yan etkisi) bilinçle EN SONA
        // bırakıldı — kategori doğrulaması ya da fiyat bandı düşerse yüklenen linkler çöpe gitmesin.
        var candidateMediaIds = await _pushImageResolver.ResolveCandidateMediaIdsAsync(product, ProductConsts.MaxImageCount);
        if (candidateMediaIds.Count == 0)
        {
            if (warnings is null)
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:ImagesRequired");
            }

            warnings.Add(L["TradeXpress:Trendyol:Product:ImagesRequired"].Value);
        }

        // Aday satırlar TEK KAYNAKTAN (push · önizleme · hafif senkron üçü de burayı görür). Kaynak ayrışsaydı
        // hafif senkron ham ERP fiyatını gönderip tam push'un yazdığı kanal fiyatını EZER ve her turda "değişti"
        // görünürdü — N11'de aynı gerekçeyle tek kaynağa çekilmişti.
        var rowSet = await BuildPushRowsAsync(channelProduct, product, warnings, notices);

        // Fiyatsız/stoksuz satır (Trendyol-only'de Override girilmemiş olabilir): gerçek push'ta fail-fast,
        // önizlemede satır uyarıyla ELENİR — items kurulumu dolu fiyat varsayar (`!.Value`).
        if (warnings is null)
        {
            EnsurePushRowsPriced(rowSet.Rows);
        }
        else
        {
            foreach (var unpriced in rowSet.Rows.Where(r => r.ListPrice is null || r.SalePrice is null || r.Stock is null))
            {
                warnings.Add(L["TrendyolProduct:Preview:RowUnpriced", unpriced.DisplayName].Value);
            }

            rowSet = rowSet with { Rows = rowSet.Rows.Where(r => r.ListPrice is not null && r.SalePrice is not null && r.Stock is not null).ToList() };
        }

        // Fiyat bandı YALNIZ gerçek push'ta zorlanır (warnings null). Önizlemede fırlatmak, kullanıcıyı
        // sorununu GÖREMEDEN kapıda bırakırdı — önizlemenin işi tam da o sayıyı göstermek.
        if (warnings is null)
        {
            EnsurePushRowsWithinPriceBand(channelProduct, rowSet.Rows);
        }

        // KISMİ ELEME SESSİZ KALMAZ (2026-08-08 düzeltmesi): varyantların bir kısmı doğrulama kapısına
        // takıldıysa push YİNE yapılır (kalanları engellemek meşru işi durdururdu) ama kullanıcı kaç varyantın
        // dışarıda kaldığını GÖRÜR. Önceden bu sayı hesaplanıp atılıyordu: push "başarılı" görünüyor, elenen
        // varyantın listelemesi Trendyol'da bayat adetle canlı kalıyordu.
        if (rowSet.PendingVerificationCount > 0)
        {
            notices?.Add(L["TrendyolProduct:VariantsHeldBackPendingVerification", rowSet.PendingVerificationCount]);
        }

        // Trendyol yalnız TRY (V2 create'de currencyType yok) → tek para birimi zorunlu; TRY-dışı karışım fail-fast.
        var currencyUnitIds = rowSet.Rows.Select(r => r.PriceCurrencyUnitId).Where(x => x is not null).Distinct().ToList();
        if (currencyUnitIds.Count > 1)
        {
            if (warnings is null)
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:MixedCurrency");
            }

            warnings.Add(L["TradeXpress:Trendyol:Product:MixedCurrency"].Value);
        }

        // T6/T8 — GERÇEK push kategori tanımına karşı DOĞRULANIR (N11 paritesi; eskiden tanıma hiç bakılmıyor,
        // red saatler sonra batch'ten dönüyordu). Eksen kaynağı foto-öncelikli: import fotoğrafı (pazaryeri
        // beyanı) varsa o, yoksa ERP varyant seçeneklerinden ad→id türetimi. Tanım alınamazsa push DURUR —
        // doğrulamasız gönderim yok. Önizleme best-effort kalır (AppendRequiredFieldWarnings zorunlu-attribute
        // uyarısını zaten üretir; tanım yokken önizlemeyi kırmak kullanıcıyı sorunu göremeden kapıda bırakırdı).
        TrendyolPushValidationResult? validated = null;
        if (warnings is null)
        {
            var leafDefinitions = await _categoryAppService.GetLeafAttributesAsync(channelProduct.CategoryId!);
            var erpOptions = await LoadVariantOptionsAsync(
                product.Id, rowSet.Rows.Where(r => r.IsErpBacked).Select(r => r.CandidateId).ToList());
            var inputs = rowSet.Rows.Select(r => new TrendyolPushVariantInput(
                r.CandidateId,
                r.Code,
                r.IsErpBacked
                    ? erpOptions.GetValueOrDefault(r.CandidateId) ?? new List<(string Name, string Value)>()
                    : r.OptionPairs,
                PhotoValuesOf(channelProduct, r.CandidateId))).ToList();
            validated = _pushValidator.Validate(leafDefinitions, channelProduct.Attributes, inputs);
        }

        // Barcode DONDURMA planı (mutasyonsuz — push başarısızsa DB'ye bayat barcode donmaz; kalıcılaştırma
        // yalnız başarılı batch sonrası ReconcileSkus ile). Varianter imzası artık DOĞRULAYICIDAN gelir (T6/T8
        // doldu); önizlemede boş kalır — imza yalnız gerçek push'un reconcile'ına lazım.
        // Aday listesi ÇAĞIRANA da verilir: başarılı submit sonrası SKU DONDURMA (ReconcileSkus) bu listeyi ister.
        // Vermeseydik push başarılı olur ama kayıt SKU'suz kalırdı → hafif senkron o üründe kalıcı NotPushedYet.
        var pushCandidates = rowSet.Rows
            .Select(r => new TrendyolSkuPushCandidate(
                r.CandidateId,
                r.Code,
                (IReadOnlyList<SalesChannelTrTrendyolProductSkuAttribute>?)validated?.VariantAxes.GetValueOrDefault(r.CandidateId)?.Signature
                    ?? Array.Empty<SalesChannelTrTrendyolProductSkuAttribute>()))
            .ToList();
        candidates?.AddRange(pushCandidates);
        var plannedBarcodes = channelProduct.PlanBarcodes(pushCandidates);

        // İndirim + emniyet payı satır kaynağında UYGULANDI (BuildPushRowsAsync) — burada yalnız taşınır.
        // Item attribute'ları: gerçek push'ta doğrulayıcı çıktısı (foto ?? türetilmiş, kanonik); önizlemede
        // yalnız foto (tanım yüklenmemiş olabilir — best-effort). OptionLabels = delil defterinin okunur çiftleri.
        var items = rowSet.Rows.Select(r =>
        {
            var axis = validated?.VariantAxes.GetValueOrDefault(r.CandidateId);
            return new TrendyolProductItem(
                Barcode: plannedBarcodes[r.CandidateId],
                StockCode: r.Code,
                Quantity: r.Stock ?? 0,
                ListPrice: r.ListPrice!.Value,
                SalePrice: r.SalePrice!.Value,
                Attributes: axis?.Attributes ?? ResolveItemAxisAttributes(channelProduct, plannedBarcodes[r.CandidateId]),
                // Önizlemede (validated yok) Trendyol-only satırın kombinasyon çiftleri gösterilir — kullanıcı
                // hangi Renk/Beden'in gittiğini görsün (N11 önizlemesi paritesi); ERP satırı özet zincirinden alır.
                OptionLabels: axis?.Options ?? (r.IsErpBacked ? null : r.OptionPairs));
        }).ToList();

        // Görsel ADRESLERİ en son (tüm yerel fail-fast'ler geçildi): gerçek push'ta geçici-link akışı ya da set
        // değişmediyse kanalın kendi CDN'i; önizlemede imzalı DAM linkleri. Fiilen giden kimlikler de döner.
        var images = await ResolvePushImagesAsync(
            channelProduct, product, candidateMediaIds, realPush: warnings is null, notices);

        // Kanalın VARSAYILAN kargo firması (2026-08-10 Hakan kararıyla kanala kondu; sunucu seçer) → gövdede
        // cargoCompanyId. Trendyol'un sayısal firma id'si sağlayıcının ExternalId'sidir.
        var cargoCompanyId = await ResolveDefaultCargoCompanyIdAsync(channelProduct.SalesChannelId);

        return new TrendyolProductData(
            ProductMainId: channelProduct.ProductMainId,
            Title: product.Name,
            Description: channelProduct.Description ?? product.Description ?? product.Name,
            CategoryId: channelProduct.CategoryId ?? string.Empty,   // yalnız ÖNİZLEME modunda boş olabilir (push'ta üstte fail-fast)
            BrandId: channelProduct.BrandId,
            // KDV: kanal override doluysa kanal, değilse ÜRÜNÜN oranı (merkezî devralma zinciri). İkisi de boşsa
            // yukarıda fail-fast atılır — sessiz varsayılan YOK (kıymetli maden %0 ≠ %20 karışmasın).
            VatRate: ResolveVatRate(channelProduct, product),
            DimensionalWeight: channelProduct.DimensionalWeight,
            DeliveryDuration: channelProduct.DeliveryDuration,
            FastDeliveryType: channelProduct.FastDeliveryType,
            ImageUrls: images.Urls,
            // Gerçek push'ta KANONİK liste (varianter tanıma denk gelenler elenmiş — onlar kalemle gider);
            // önizlemede ham kayıt (tanım yüklenmemiş olabilir).
            Attributes: validated?.ProductAttributes
                ?? channelProduct.Attributes
                    .Select(a => new TrendyolAttributeValue(a.AttributeId, a.AttributeValueId, a.CustomValue))
                    .ToList(),
            Items: items,
            SentMediaIds: images.MediaIds,
            CargoCompanyId: cargoCompanyId);
    }

    /// <summary>Kanalın varsayılan kargo firmasının Trendyol sayısal id'si — kanalda seçili değilse ya da
    /// sağlayıcı kaydı silinmişse null (gövdeye yazılmaz; Trendyol satıcı varsayılanına düşer).</summary>
    private async Task<int?> ResolveDefaultCargoCompanyIdAsync(Guid salesChannelId)
    {
        var channel = await _channelRepository.FindAsync(salesChannelId);
        if (channel?.DefaultCargoProviderId is not { } providerId)
        {
            return null;
        }

        var provider = await _cargoProviderRepository.FindAsync(providerId);
        return provider is not null && int.TryParse(provider.ExternalId, out var externalId) ? externalId : null;
    }

    /// <summary>Push görsel çözümü — adresler + FİİLEN giden kimlikler (defter bunu yazar).</summary>
    private sealed record PushImages(List<string> Urls, List<Guid> MediaIds);

    /// <summary>
    /// Push görselleri. Önizleme + yayıncı-kapalı gerçek push: imzalı DAM linkleri (dış ağa çıkılmaz).
    /// Yayıncı AÇIK gerçek push: ① bugünkü görsel seti import DAMGASIYLA (<c>RemoteImageMediaIds</c>) birebir ise
    /// kanalın kendi CDN adresleri gönderilir — kanala aynı görseli yeniden yutturma; kapı DEFTERLE değil damgayla
    /// kurulur (bayat kanal adresi tuzağı entity doc'unda) ② değilse her medya geçici barındırmaya yüklenir;
    /// yüklenemeyen görsel bildirimle atlanır ve KİMLİĞİ giden listeye GİRMEZ (defter "göndermediğini yazmaz").
    /// Aday varken hiçbiri yüklenemediyse özgül hata: <c>ImageTemporaryLinkFailed</c> — "görsel yok" değil,
    /// "barındırıcıya ulaşılamadı" (AV/ağ filtresi engeli bilinen arıza modu; CLAUDE.md §6).
    /// </summary>
    private async Task<PushImages> ResolvePushImagesAsync(
        SalesChannelTrTrendyolProduct channelProduct, Product product, List<Guid> candidateMediaIds,
        bool realPush, List<string>? notices)
    {
        if (!realPush || !_temporaryMediaLinkPublisher.IsEnabled)
        {
            var signed = await _pushImageResolver.ResolveAsync(product, ProductConsts.MaxImageCount);
            var signedIds = await _pushImageResolver.ResolveMediaIdsAsync(product, ProductConsts.MaxImageCount);
            return new PushImages(signed, signedIds);
        }

        if (candidateMediaIds.Count > 0
            && channelProduct.RemoteImageUrls.Count > 0
            && channelProduct.RemoteImageMediaIds.SequenceEqual(candidateMediaIds))
        {
            return new PushImages(channelProduct.RemoteImageUrls.ToList(), candidateMediaIds);
        }

        var urls = new List<string>();
        var sentIds = new List<Guid>();
        foreach (var mediaId in candidateMediaIds)
        {
            if (await _temporaryMediaLinkPublisher.PublishAsync(mediaId) is { } url)
            {
                urls.Add(url);
                sentIds.Add(mediaId);
            }
            else
            {
                notices?.Add(L["TrendyolProduct:ImageTemporaryLinkFailed"].Value);
            }
        }

        if (candidateMediaIds.Count > 0 && urls.Count == 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ImageTemporaryLinkFailed");
        }

        return new PushImages(urls, sentIds);
    }

    /// <summary>Varyantın import FOTOĞRAFI (pazaryerinin bildirdiği eksen değerleri) — doğrulayıcının
    /// foto-öncelik girdisi; fotoğrafsız varyantta boş liste.</summary>
    private static IReadOnlyList<SalesChannelTrTrendyolProductSkuRemoteAxisValue> PhotoValuesOf(
        SalesChannelTrTrendyolProduct channelProduct, Guid variantId)
    {
        return channelProduct.Skus.FirstOrDefault(s => s.ProductVariantId == variantId)?.RemoteVariantAttributes
            ?? (IReadOnlyList<SalesChannelTrTrendyolProductSkuRemoteAxisValue>)Array.Empty<SalesChannelTrTrendyolProductSkuRemoteAxisValue>();
    }

    /// <summary>Kalemin item-düzeyi attribute'ları — ÖNİZLEME geri düşüşü (gerçek push'ta kaynak doğrulayıcı
    /// çıktısıdır: foto ?? kategori tanımından türetilmiş; T6/T8 2026-08-14'te kuruldu). Burada yalnız import
    /// fotoğrafı okunur (<c>SalesChannelTrTrendyolProductSku.RemoteVariantAttributes</c>: pazaryerinin kendi beyanı);
    /// fotoğrafı olmayan kalemde <c>null</c> → önizleme yalnız ürün-seviyesi nitelikleri gösterir.</summary>
    private static IReadOnlyList<TrendyolAttributeValue>? ResolveItemAxisAttributes(
        SalesChannelTrTrendyolProduct channelProduct, string barcode)
    {
        var sku = channelProduct.Skus.FirstOrDefault(s => s.Barcode == barcode);
        if (sku is null || sku.RemoteVariantAttributes.Count == 0)
        {
            return null;
        }

        return sku.RemoteVariantAttributes
            .Select(a => new TrendyolAttributeValue(
                a.AttributeId,
                a.AttributeValueId,
                a.AttributeValueId is null ? a.ValueText : null))
            .ToList();
    }

    // ── Trendyol varyant ÖZELLİKLERİ (klon-sonra-ayrış) + kartezyen kombinasyon RECONCILE ─────────────────────
    // ProductAttributes = Trendyol'un KENDİ varyant özellikleri (N11 deseninin portu). Tanımlıysa (persist edilmiş en
    // az 1 özellik varsa) kanal-ürünün kombinasyon seti ARTIK bu özelliklerin kartezyen kombinasyonundan üretilir —
    // legacy ERP-doğrudan graf (BuildStockItemGraphAsync/SaveStockItemOverridesAsync) devre dışı kalır.
    // Reconcile anahtarı CombinationSignature ("{AttributeId}={ValueId}|...", AttributeId sıralı) — STABİL ID'lerden
    // kurulur, ERP ProductVariantId yalnız fiyat/stok fallback KAYNAĞI (bir kerelik fırsatçı eşleştirme; reconcile
    // anahtarı DEĞİL). Özellik/değer silinip kombinasyon artık üretilemezse o satır + reçetesi TEMİZLENİR.

    /// <summary>Bellek-içi özellik + değer görünümü — reconcile matematiği (kartezyen + imza) için.</summary>
    private sealed record AttributeWithValues(Guid AttributeId, string AttributeName, List<(Guid ValueId, string Value)> Values);

    private async Task<List<SalesChannelTrTrendyolProductAttribute>> LoadChannelAttributeEntitiesAsync(Guid channelProductId)
    {
        return await AsyncExecuter.ToListAsync(
            (await _channelAttributeRepository.GetQueryableAsync())
                .Where(a => a.SalesChannelTrTrendyolProductId == channelProductId)
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.CreationTime));
    }

    private async Task<List<SalesChannelTrTrendyolProductAttributeValue>> LoadChannelAttributeValueEntitiesAsync(List<Guid> channelAttributeIds)
    {
        if (channelAttributeIds.Count == 0)
        {
            return new List<SalesChannelTrTrendyolProductAttributeValue>();
        }

        return await AsyncExecuter.ToListAsync(
            (await _channelAttributeValueRepository.GetQueryableAsync())
                .Where(v => channelAttributeIds.Contains(v.AttributeId))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.CreationTime));
    }

    private static List<SalesChannelTrTrendyolProductAttributeDto> BuildAttributesDto(
        List<SalesChannelTrTrendyolProductAttribute> channelAttributes, List<SalesChannelTrTrendyolProductAttributeValue> values)
    {
        var valuesByChannelAttribute = values.GroupBy(v => v.AttributeId).ToDictionary(g => g.Key, g => g.ToList());
        return channelAttributes.Select(a => new SalesChannelTrTrendyolProductAttributeDto
        {
            Id = a.Id,
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByChannelAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<SalesChannelTrTrendyolProductAttributeValue>())
                .Select(v => new SalesChannelTrTrendyolProductAttributeValueDto
                {
                    Id = v.Id,
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    private static List<AttributeWithValues> ToAttributeWithValues(
        List<SalesChannelTrTrendyolProductAttribute> channelAttributes, List<SalesChannelTrTrendyolProductAttributeValue> values)
    {
        var valuesByChannelAttribute = values.GroupBy(v => v.AttributeId).ToDictionary(g => g.Key, g => g.ToList());
        return channelAttributes.Select(a => new AttributeWithValues(
            a.Id,
            a.Name,
            (valuesByChannelAttribute.TryGetValue(a.Id, out var vs) ? vs : new List<SalesChannelTrTrendyolProductAttributeValue>())
                .Select(v => (v.Id, v.Value))
                .ToList())).ToList();
    }

    /// <summary>Özellik + değer grafını persist eder (RecipeLines ile AYNI iki-öge diff deseni: silinenler → upsert;
    /// ClientKey→Id input DTO'suna geri yazılır). Boş/null girdi no-op (mevcut özelliklere DOKUNMAZ).</summary>
    private async Task SaveAttributesGraphAsync(SalesChannelTrTrendyolProduct channelProduct, List<SalesChannelTrTrendyolProductAttributeDto>? attributesInput)
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
            SalesChannelTrTrendyolProductAttribute entity;
            if (channelAttribute.Id == Guid.Empty)
            {
                entity = new SalesChannelTrTrendyolProductAttribute(channelProduct.CompanyId, channelProduct.Id, channelAttribute.Name, channelAttribute.DisplayOrder);
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
                    var valueEntity = new SalesChannelTrTrendyolProductAttributeValue(channelProduct.CompanyId, channelAttribute.Id, value.Value, value.DisplayOrder);
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
    /// fail-fast (N11 S4 guard'ının portu). Üst-sınır CombinationSignature kolon kapasitesini de korur
    /// (600 karakter ≈ 8 "{AttributeId}={ValueId}" çifti; sabit 8'i AŞMAMALI).</summary>
    private async Task EnsureAttributeCountWithinLimitAsync(Guid channelProductId, List<SalesChannelTrTrendyolProductAttributeDto> attributesInput)
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
            throw new BusinessException("TradeXpress:Trendyol:Product:TooManyAttributes")
                .WithData("Max", ProductAttributeConsts.MaxAttributesPerProduct);
        }
    }

    /// <summary>Özellik grafını persist eder + persist-sonrası DB durumuna göre kartezyen kombinasyon satırlarını
    /// reconcile eder. Döndürdüğü bool = channelAttribute-modu AKTİF mi (en az 1 persist edilmiş özellik var) — false
    /// ise çağıran legacy ERP-doğrudan yola (<see cref="BuildStockItemGraphAsync"/>/<see cref="SaveStockItemOverridesAsync"/>) düşer.</summary>
    private async Task<bool> SaveAttributesAndReconcileAsync(SalesChannelTrTrendyolProduct channelProduct, List<SalesChannelTrTrendyolProductAttributeDto>? attributesInput)
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
    /// devredilmiştir (N11 S4 bağlama şekliyle BİREBİR). "0 özellik → kombinasyon yok" yorumu çağıran guard'ıdır
    /// (motorun birim elemanına — tek boş kombinasyon — düşülmez).</summary>
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

    /// <summary>Kombinasyon imzası — N11 ile AYNI format ("{AttributeId}={ValueId}|...", AttributeId artan sıralı).
    /// BİLİNÇLİ olarak <see cref="VariantCombinationEngine.BuildKey"/>'e delege EDİLMEZ: format farklı (BuildKey düz
    /// Guid join) ve tüketici-yerel/opak — S1 karakterizasyon testleri bu formatı snapshot'ladı, DEĞİŞTİRME.</summary>
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

    /// <summary>Bir Trendyol kombinasyonunun (Attribute.Name/AttributeValue.Value seti) ERP varyantlarından TAM örtüşen
    /// tekini bulur (bir kerelik fırsatçı eşleştirme — reconcile anahtarı DEĞİL). Örtüşme YOKSA ya da BİRDEN FAZLA
    /// varyant aynı sete sahipse (belirsiz) null döner — yanlış atamaktansa Trendyol-only kalması güvenli.</summary>
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

    /// <summary>Kartezyen kombinasyon satırlarını (<see cref="SalesChannelTrTrendyolProductStockItem"/>, CombinationSignature
    /// ile) mevcut özellik/değer setiyle reconcile eder — diff/sıra mekaniği <see cref="VariantSetReconciler"/>'da
    /// (N11 S4 bağlama şekli BİREBİR): artık üretilemeyen kombinasyonlar (satır + reçetesi) removeAsync'te SİLİNİR
    /// (orphan temizliği), eksik kombinasyonlar addAsync'te İNSERT edilir (fırsatçı ERP eşleştirmesiyle — KANAL
    /// politikası, çekirdekte değil). Var olan satırlara (imzası hâlâ üretilebilir) DOKUNULMAZ — kullanıcı
    /// override/reçete verisi korunur.</summary>
    private async Task SynchronizeStockItemsAsync(SalesChannelTrTrendyolProduct channelProduct, List<AttributeWithValues> channelAttributes)
    {
        var combos = BuildCombinations(channelAttributes);
        var comboBySignature = new Dictionary<string, List<(Guid AttributeId, Guid ValueId)>>(StringComparer.Ordinal);
        foreach (var combo in combos)
        {
            comboBySignature[BuildCombinationSignature(combo)] = combo;
        }

        var existingHeaders = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id && h.CombinationSignature != null));

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
                    r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && r.StockItemId == orphan.Id,
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

                // Aynı ERP varyantına bağlı İMZASIZ (legacy/import kökenli) başlık varsa YENİ satır AÇILMAZ — filtered
                // unique index (TenantId, kanal ürünü, ProductVariantId) ikinci satırı DB'de reddederdi. Mevcut satır
                // imzaya TERFİ eder: kullanıcı override/reçetesi korunur, attribute-modu UI'ında görünmezken görünür olur.
                if (matchedVariantId is not null)
                {
                    var legacyHeader = await AsyncExecuter.FirstOrDefaultAsync(
                        (await _stockItemRepository.GetQueryableAsync())
                            .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id
                                && h.ProductVariantId == matchedVariantId
                                && h.CombinationSignature == null));
                    if (legacyHeader is not null)
                    {
                        legacyHeader.SetCombinationSignature(signature);
                        await _stockItemRepository.UpdateAsync(legacyHeader, autoSave: true);
                        return;
                    }
                }

                var header = new SalesChannelTrTrendyolProductStockItem(channelProduct.CompanyId, channelProduct.Id, matchedVariantId);
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
    private async Task<List<SalesChannelTrTrendyolProductStockItemGraphDto>> BuildAttributeStockItemsAsync(
        SalesChannelTrTrendyolProduct channelProduct, List<AttributeWithValues> channelAttributes)
    {
        var headers = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id && h.CombinationSignature != null)
                .OrderBy(h => h.CreationTime));
        if (headers.Count == 0)
        {
            return new List<SalesChannelTrTrendyolProductStockItemGraphDto>();
        }

        var headerIds = headers.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
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
        // reçetesiz/yan-maliyetsiz kalmasın — inceleme bulgusu; N11 simetriği). Trendyol-only satırda (ERP eşleşmesi
        // yok) taban maliyet bilinmez → reçete boş kalır (OverridePrice zaten zorunlu).
        var erpByVariant = erpVariantIds.Count == 0
            ? new Dictionary<Guid, List<ProductVariantRecipeLine>>()
            : (await AsyncExecuter.ToListAsync(
                    (await _erpRecipeLineRepository.GetQueryableAsync())
                        .Where(r => erpVariantIds.Contains(r.ProductVariantId))))
                .GroupBy(r => r.ProductVariantId)
                .ToDictionary(g => g.Key, g => g.ToList());
        var sideCostPlan = await BuildSideCostPlanAsync(channelProduct);

        var attributeById = channelAttributes.ToDictionary(a => a.AttributeId);
        var nodes = new List<SalesChannelTrTrendyolProductStockItemGraphDto>(headers.Count);
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

            var node = new SalesChannelTrTrendyolProductStockItemGraphDto
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
    /// satır AÇAMAZ (Id boş düğüm atlanır — reconcile tek üretici); yabancı/bayat Id sessizce atlanır. Trendyol-only
    /// (ProductVariantId null) satırda ERP fallback'i YOKTUR → OverridePrice + OverrideStock ZORUNLU (fail-fast).</summary>
    private async Task SaveAttributeStockItemOverridesAsync(SalesChannelTrTrendyolProduct channelProduct, List<SalesChannelTrTrendyolProductStockItemGraphDto>? variants)
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
            if (header is null || header.SalesChannelTrTrendyolProductId != channelProduct.Id)
            {
                continue;
            }

            if (header.ProductVariantId is null && (node.OverridePrice is null || node.OverrideStock is null))
            {
                throw new BusinessException("TradeXpress:Trendyol:ProductVariant:OverrideRequiredForTrendyolOnly");
            }

            var insuredShippingChanged = header.InsuredShippingEnabled != node.InsuredShippingEnabled;
            header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
            header.SetOverrideStock(node.OverrideStock);
            header.SetMargin(node.Margin);
            header.SetInsuredShippingEnabled(node.InsuredShippingEnabled);
            await _stockItemRepository.UpdateAsync(header, autoSave: true);

            // Sigortalı-gönderim anahtarı bu save'de DEĞİŞTİYSE reçeteye hemen işlenir (yalnız sigorta satırı —
            // kullanıcının sildiği diğer otomatik satırlar geri getirilmez); yoksa türetilmiş fiyat açık
            // "Giderleri Yeniden Uygula"ya kadar bayat kalırdı (inceleme bulgusu; N11 simetriği).
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
    private async Task<List<SalesChannelTrTrendyolProductStockItemGraphDto>> BuildStockItemGraphAsync(SalesChannelTrTrendyolProduct channelProduct)
    {
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == channelProduct.ProductId && v.IsActive)
                .OrderByDescending(v => v.IsMain).ThenBy(v => v.Code));
        if (variants.Count == 0)
        {
            return new List<SalesChannelTrTrendyolProductStockItemGraphDto>();
        }

        var variantIds = variants.Select(v => v.Id).ToList();

        // Yalnız ERP-backed başlıklar (ProductVariantId dolu) — Trendyol-only satırlar bu ERP-varyant grafına girmez
        // (kendi grubunda listelenir; bkz. BuildAttributeStockItemsAsync).
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        // Reçete satırları override BAŞLIĞININ kendi Id'sine bağlı (StockItemId) — önce header.Id'ye, sonra ERP varyantına eşlenir.
        var headerIds = headers.Values.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
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

        var nodes = new List<SalesChannelTrTrendyolProductStockItemGraphDto>(variants.Count);
        foreach (var v in variants)
        {
            var node = new SalesChannelTrTrendyolProductStockItemGraphDto
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

        await PopulateNodeCostsAsync(nodes);
        return nodes;
    }

    /// <summary>Düğümlerin CANLI net maliyet + türetilmiş fiyatını doldurur (kartezyen ve legacy graf ORTAK sonu).</summary>
    private async Task PopulateNodeCostsAsync(List<SalesChannelTrTrendyolProductStockItemGraphDto> nodes)
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

    /// <summary>Yan-maliyet planını kurar: kanal gider satırları + ürünün KATEGORİSİNDEN kalıtımla çözülen komisyon
    /// oranı (<see cref="TrendyolCommissionResolver"/> — yaprak → üst → kök; hiçbir seviyede tanımlı değilse
    /// yer tutucu). Varyant opt-in anahtarı varyant-başı olduğundan burada KAPALI döner — çağıran
    /// <c>plan with { VariantOptInEnabled = ... }</c> ile varyanta göre açar.
    ///
    /// <para><b>Kategorisiz kayıtta</b> (CategoryId null — import'ta eşleşmemiş olabilir) çözücü yine yer tutucuya
    /// düşer: komisyon HİÇ hesaplanmaması, yaklaşık hesaplanmasından kötüdür (fiyat ~%20 ucuz çıkardı).</para></summary>
    private async Task<SideCostPlan> BuildSideCostPlanAsync(SalesChannelTrTrendyolProduct channelProduct)
    {
        var channel = await _channelRepository.FindAsync(channelProduct.SalesChannelId);
        var commissionRate = await _commissionResolver.ResolveAsync(channelProduct.CategoryId);
        return SideCostPlan.From(channel?.SideCosts, commissionRate, variantOptInEnabled: false);
    }

    /// <summary>Yan-maliyet satırlarını KAYDEDİLMİŞ reçetelerde ayarlardan TAZELER ("yeniden uygula"): işaretli
    /// (otomatik) satırlar düşürülüp yeniden üretilir, kullanıcı satırlarına dokunulmaz. Kaydedilmemiş reçeteler
    /// atlanır (klon yolu zaten ekler). Kanal gider ayarı değişince ya da silinen otomatik satırı geri getirmek
    /// için kullanılır; idempotent (N11 simetriği).</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> ReapplySideCostsAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var plan = await BuildSideCostPlanAsync(entity);

        var headers = await AsyncExecuter.ToListAsync(
            (await _stockItemRepository.GetQueryableAsync())
                .Where(h => h.SalesChannelTrTrendyolProductId == entity.Id));
        var headerIds = headers.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == entity.Id && headerIds.Contains(r.StockItemId))))
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

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        await PopulateStockItemGraphAsync(entity, dto);
        return dto;
    }

    /// <summary>Push için varyant-başı efektif fiyat/stok — zincir: OverridePrice ?? türetilmiş (KAYDEDİLMİŞ reçete
    /// NetCost × (1+Margin/100)) ?? ERP SalePrice; stok: OverrideStock ?? ERP StockQuantity. Push PERSIST edilmiş
    /// gerçeği kullanır (ERP klonu değil) — kaydedilmemiş reçete türetilmiş fiyat üretmez.</summary>
    private async Task<IReadOnlyDictionary<Guid, VariantPushPricing>> ResolveVariantPushPricingAsync(
        SalesChannelTrTrendyolProduct channelProduct, List<EntityVariant> variants)
    {
        var variantIds = variants.Select(v => v.Id).ToList();

        // Satış fiyatı/birimi ProductVariantDetail'de (agnostik EntityVariant Product uzantısı) — EntityVariantId ile batch yüklenir.
        var salePrices = await LoadVariantSalePricesAsync(variantIds);

        // Yalnız ERP-backed başlıklar — Trendyol-only satırlar (ProductVariantId null) burada ERP varyantına
        // eşlenemez; kendi push zincirleri push fazında (T8) ele alınır (N11 J3 simetriği).
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id && h.ProductVariantId != null
                        && variantIds.Contains(h.ProductVariantId!.Value))))
            .ToDictionary(h => h.ProductVariantId!.Value);

        // Reçete satırları header'ın KENDİ Id'sine bağlı (StockItemId) — ERP ProductVariantId'ye değil.
        var headerIds = headers.Values.Select(h => h.Id).ToList();
        var savedByHeader = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && headerIds.Contains(r.StockItemId))))
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

    /// <summary>Kanal-özel varyant override grafını persist eder (LEGACY ERP-doğrudan yol) — override sinyali
    /// (OverridePrice/OverrideStock/Margin herhangi biri dolu) olan varyantın başlığı + reçetesi yazılır; TÜMÜ boşsa
    /// (saf ERP devralma) kaydedilmiş override/reçete TEMİZLENİR (ölü satır şişmesini önle). Türetilmiş fiyat/NetCost
    /// hesap alanları PERSIST EDİLMEZ (canlı).</summary>
    private async Task SaveStockItemOverridesAsync(SalesChannelTrTrendyolProduct channelProduct, List<SalesChannelTrTrendyolProductStockItemGraphDto> variants)
    {
        if (variants == null || variants.Count == 0)
        {
            return;
        }

        // Yalnız ERP-backed başlıklar — Trendyol-only satırlar (ProductVariantId null) bu ERP-anchor'lı override
        // yolundan GEÇMEZ, kartezyen motor (SynchronizeStockItemsAsync) tarafından ayrıca üretilir/güncellenir.
        var existingHeaders = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        SideCostPlan? sideCostPlan = null;   // tembel — yalnız sigorta anahtarı değişen satır varsa kurulur

        foreach (var node in variants)
        {
            if (node.ProductVariantId is null || node.ProductVariantId == Guid.Empty)
            {
                continue;   // anchor yok (Trendyol-only ya da bayat düğüm) → atla; kartezyen motor ele alır
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
                        r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && r.StockItemId == header.Id,
                        autoSave: true);
                    await _stockItemRepository.DeleteAsync(header, autoSave: true);
                }

                continue;
            }

            var insuredShippingChanged = (header?.InsuredShippingEnabled ?? false) != node.InsuredShippingEnabled;
            if (header is null)
            {
                header = new SalesChannelTrTrendyolProductStockItem(channelProduct.CompanyId, channelProduct.Id, node.ProductVariantId);
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
            // "Giderleri Yeniden Uygula"ya kadar bayat kalırdı (inceleme bulgusu; N11 simetriği).
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

    /// <summary>Bir override BAŞLIĞININ (ERP-backed veya Trendyol-only fark etmez — <paramref name="stockItemId"/> her
    /// zaman <see cref="SalesChannelTrTrendyolProductStockItem"/>'ın KENDİ Id'sidir) kanal-özel reçete satırlarını
    /// persist eder (ERP SaveRecipeLinesAsync deseni, iki-geçişli): silinenler → LineOrder 0..n yeniden-numara →
    /// referans doğrulama → skaler insert/update (1. geçiş) → türev SelectedLines kaynak Id CSV çözümü (2. geçiş).
    /// ComponentType set-once (ctor'da).</summary>
    private async Task SaveChannelRecipeLinesAsync(SalesChannelTrTrendyolProduct channelProduct, Guid stockItemId, List<ProductRecipeLineGraphDto> lines)
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
        var entityByClientKey = new Dictionary<Guid, SalesChannelTrTrendyolProductStockItemRecipeLine>();
        foreach (var l in survivors)
        {
            SalesChannelTrTrendyolProductStockItemRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new SalesChannelTrTrendyolProductStockItemRecipeLine(
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
    private static void ApplyChannelRecipeLineFields(SalesChannelTrTrendyolProductStockItemRecipeLine entity, ProductRecipeLineGraphDto l)
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
    private static List<ProductRecipeLineGraphDto> MapSavedRecipeLines(List<SalesChannelTrTrendyolProductStockItemRecipeLine> saved)
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

    private static TrendyolCredentials CredentialsOf(SalesChannelTrTrendyol channel)
    {
        return new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
    }

    private void ApplyInput(SalesChannelTrTrendyolProduct entity, ISalesChannelTrTrendyolProductInput input)
    {
        entity.SetCategory(input.CategoryId, input.CategoryName);
        entity.SetBrand(input.BrandId, input.BrandName);
        entity.SetVatRate(input.VatRate);
        entity.SetCargoCompany(input.CargoCompanyId);
        entity.SetDimensionalWeight(input.DimensionalWeight);
        entity.SetDescription(input.Description);
        entity.SetDeliveryOption(input.DeliveryDuration, input.FastDeliveryType);
        entity.SetSafetyStock(input.SafetyStock);
        entity.SetPriceBand(input.MinPrice, input.MaxPrice);
        entity.SetActive(input.IsActive);
        entity.SetAttributes(input.Attributes.Select(a => new SalesChannelTrTrendyolProductCategoryAttribute(a.AttributeId, a.AttributeValueId, a.CustomValue)));
    }

    private async Task<SalesChannelTrTrendyolProduct> GetOwnedAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.Id == id && x.CompanyId == companyId));
        if (entity is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:RecordNotFound");
        }

        return entity;
    }

    private async Task<SalesChannelTrTrendyol> GetOwnedChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.Id == salesChannelId && x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ChannelNotFound");
        }

        return channel;
    }

    private async Task<Product> GetOwnedProductAsync(Guid productId)
    {
        var product = await FindOwnedProductAsync(productId);
        if (product is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ProductNotFound");
        }

        return product;
    }

    /// <summary>Şablon ürünü bulur, YOKSA null döner (fırlatmaz). Ürün SİLİNMİŞ olabilir: kanal kaydı ürüne
    /// yalnız Guid ile bağlıdır (aggregate'ler arası id-only konvansiyonu) → referans bütünlüğünü DB zorlamaz ve
    /// ürün silinince kanal kaydı ölü bir id taşımaya devam eder. Bu ihtimali GÖRMEK isteyen çağıranlar (içe
    /// aktarım) bunu kullanır; ihtimalin hata olduğu çağıranlar <see cref="GetOwnedProductAsync"/> kullanır.</summary>
    private async Task<Product?> FindOwnedProductAsync(Guid productId)
    {
        var companyId = EnsureCurrentCompanyId();
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _productRepository.GetQueryableAsync()).Where(x => x.Id == productId && x.CompanyId == companyId));
    }

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:CompanyRequired");
        }

        return companyId;
    }
}
