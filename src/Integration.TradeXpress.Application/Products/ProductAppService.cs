using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels.Variants;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Product CRUD — <b>company-owned + per-tenant</b> katalog (AssayOffice company-scope + Account graf-save deseni
/// birleşimi). Kapsam DAİMA çalışılan şirket (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId
/// GÖNDERMEZ). Kimlik (Code uppercase normalize, şirket-scope benzersizlik). Nitelik + varyant sistemi JENERİK
/// agnostik <see cref="IEntityVariantGraphService"/>'e delege edilir ("Product" bağlamı — Kod/Ad OTOMATİK üretilir);
/// Product-ÖZEL satış fiyatı (<see cref="ProductVariantDetail"/>) + reçete satırları uzantı callback'iyle çözülen DB
/// varyantına bağlanır (GoodAppService deseni). Görsel + pazaryeri (N11/Trendyol) kanal grafları burada orkestre edilir.
/// </summary>
[Authorize(TradeXpressPermissions.Products.Default)]
public class ProductAppService : TradeXpressAppService, IProductAppService
{
    private const string ProductEntityName = "Product";

    // Ürün-seviyesi medya (görsel + video kütüphanesi) agnostik EntityMedia anahtarı — Good'un VariantImageEntityName deseni ürün-seviyesinde.
    // Dize MediaEntityNames'te: aynı anahtarı pazaryeri push'u da OKUYOR (kaynak ikiye bölünürse medya sessizce kaybolur).
    private const string ProductMediaEntityName = MediaEntityNames.Product;

    private readonly IRepository<Product, Guid> _repository;
    private readonly IRepository<SubstitutionGroup, Guid> _substitutionGroupRepository; // yalnız OKUMA — FK varlık doğrulaması
    private readonly IRepository<ProductCategory, Guid> _productCategoryRepository;     // yalnız OKUMA — kategori bağı doğrulaması
    private readonly SubstitutionVariantMaterializer _substitutionMaterializer;   // muadil varyantlarını stoktan otomatik üretir
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<ProductSpecification, Guid> _specificationRepository;
    private readonly ProductCategoryTreeManager _productCategoryTreeManager;
    private readonly IRepository<Company, Guid> _companyRepository;   // yalnız OKUMA — menşei/domestic türetmesi
    private readonly IRepository<ProductCategoryChannelAttributeMapping, Guid> _channelAttributeMappingRepository;   // yalnız OKUMA
    private readonly IRepository<ProductCategoryChannelAttributeValueMapping, Guid> _channelValueMappingRepository;  // yalnız OKUMA
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly RecipeCostPopulator _recipeCostPopulator;
    private readonly ICurrentCompany _currentCompany;
    private readonly IBlobContainer<ProductImagesContainer> _imageContainer;
    private readonly ISalesChannelTrN11ProductAppService _channelProductAppService;
    private readonly ISalesChannelTrTrendyolProductAppService _trendyolChannelProductAppService;
    private readonly ISalesChannelEtsyProductAppService _etsyChannelProductAppService;
    private readonly IEntityMediaAppService _entityMedia;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public ProductAppService(
        IRepository<Product, Guid> repository,
        IRepository<SubstitutionGroup, Guid> substitutionGroupRepository,
        IRepository<ProductCategory, Guid> productCategoryRepository,
        SubstitutionVariantMaterializer substitutionMaterializer,
        IEntityVariantGraphService entityVariant,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> variantDetailRepository,
        IRepository<ProductSpecification, Guid> specificationRepository,
        ProductCategoryTreeManager productCategoryTreeManager,
        IRepository<Company, Guid> companyRepository,
        IRepository<ProductCategoryChannelAttributeMapping, Guid> channelAttributeMappingRepository,
        IRepository<ProductCategoryChannelAttributeValueMapping, Guid> channelValueMappingRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        RecipeCostPopulator recipeCostPopulator,
        ICurrentCompany currentCompany,
        IBlobContainer<ProductImagesContainer> imageContainer,
        ISalesChannelTrN11ProductAppService channelProductAppService,
        ISalesChannelTrTrendyolProductAppService trendyolChannelProductAppService,
        ISalesChannelEtsyProductAppService etsyChannelProductAppService,
        IEntityMediaAppService entityMedia)
    {
        _repository = repository;
        _substitutionGroupRepository = substitutionGroupRepository;
        _productCategoryRepository = productCategoryRepository;
        _substitutionMaterializer = substitutionMaterializer;
        _entityVariant = entityVariant;
        _variantRepository = variantRepository;
        _variantDetailRepository = variantDetailRepository;
        _specificationRepository = specificationRepository;
        _productCategoryTreeManager = productCategoryTreeManager;
        _companyRepository = companyRepository;
        _channelAttributeMappingRepository = channelAttributeMappingRepository;
        _channelValueMappingRepository = channelValueMappingRepository;
        _recipeLineRepository = recipeLineRepository;
        _recipeCostPopulator = recipeCostPopulator;
        _currentCompany = currentCompany;
        _imageContainer = imageContainer;
        _channelProductAppService = channelProductAppService;
        _trendyolChannelProductAppService = trendyolChannelProductAppService;
        _etsyChannelProductAppService = etsyChannelProductAppService;
        _entityMedia = entityMedia;
    }

    public virtual async Task<PagedResultDto<ProductListDto>> GetListAsync(ProductListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<ProductListDto>(0, new List<ProductListDto>());

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        var counts = await LoadVariantCountsAsync(items.Select(p => p.Id));
        var previewUrls = await LoadImagePreviewUrlsAsync(items);

        return new PagedResultDto<ProductListDto>(
            totalCount,
            items.Select(p => new ProductListDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                IsActive = p.IsActive,
                VariantCount = counts.GetValueOrDefault(p.Id),
                ImagePreviewUrl = previewUrls.GetValueOrDefault(p.Id),
            }).ToList());
    }

    /// <summary>Grid önizlemesi için ürün başına VARSAYILAN görselin küçük gösterimi. <c>Product.Images</c> owned
    /// koleksiyonu JSON kolonuna map'li (<c>ToJson()</c>) — sahibiyle AYNI satırda gelir, <paramref name="products"/>
    /// zaten materyalize (yukarıdaki <c>ToListAsync</c>'ten); ek DB sorgusu YOK. Url kaynağında direkt bağlantı;
    /// Upload kaynağında THUMBNAIL blobundan data-URL (<see cref="PopulateImagePreviewsAsync"/> ile AYNI desen —
    /// tam çözünürlük gömülmez). Sayfa-başı satır sayısı kadar blob okuması (DxGrid zaten sayfalı, N+1 riski sınırlı).</summary>
    private async Task<Dictionary<Guid, string>> LoadImagePreviewUrlsAsync(List<Product> products)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var p in products)
        {
            var defaultImage = p.Images.FirstOrDefault(i => i.IsDefault)
                ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault();
            if (defaultImage is null)
            {
                continue;
            }

            if (defaultImage.SourceType == ProductImageSourceType.Url && !string.IsNullOrEmpty(defaultImage.Url))
            {
                result[p.Id] = defaultImage.Url;
            }
            else if (defaultImage.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(defaultImage.BlobName))
            {
                var thumbnail = await _imageContainer.GetAllBytesOrNullAsync(
                    ProductImageAppService.ThumbnailNameOf(defaultImage.BlobName));
                if (thumbnail is not null)
                {
                    result[p.Id] = ProductImageAppService.BuildPreviewDataUrl(thumbnail);
                }
            }
        }

        return result;
    }

    public virtual async Task<ProductGetDto> GetAsync(Guid id) => await ToGetDtoAsync(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.Products.Create)]
    public virtual async Task<ProductGetDto> CreateAsync(ProductCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Product:CompanyRequired");

        // Benzersizlik ÖN-kontrolü (Update ile simetrik): aynı şirkette aynı kodlu ürün → dostane hata,
        // ham DB (TenantId, CompanyId, Code) unique çakışması değil. Kendisi yok → excludeId boş.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(Product.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var entity = new Product(companyId, input.Code, input.Name);
        entity.SetDescription(input.Description);
        await ApplyProductCategoryAsync(entity, input.ProductCategoryId);
        entity.SetImages(MapImages(input.Images));
        entity.SetDiscount(input.DiscountType, input.DiscountValue, input.DiscountStartDate, input.DiscountEndDate);
        entity.SetShelfLife(input.ProductionDate, input.ExpirationDate);
        ApplyMarketplaceDefaults(entity, input.OriginCountryId, input.Condition, input.PreparingDay,
            input.MaxPurchaseQuantity, input.SellerNote,
            input.CurrencyUnitId, input.RecipeTemplateId, input.PackageDesi, input.SpecialInfo, input.AddOns);
        // Varyant modu ÖNCE, muadil konfigürasyonu SONRA (mutator mod tutarlılığını modun güncel değerine göre kurar).
        await EnsureSubstitutionGroupExistsAsync(input.VariantMode, input.SubstitutionGroupId);
        entity.SetVariantMode(input.VariantMode);
        entity.SetSubstitutionConfig(input.SubstitutionGroupId, input.SubstitutionTargetQuantity,
            input.SubstitutionToleranceType, input.SubstitutionToleranceValue, input.SubstitutionOverrideVariantIds,
            input.SubstitutionVariantMode);
        entity.SetStockPolicy(input.StockPolicy);   // muadilde no-op: SetSubstitutionConfig Calculated'ı zorladı
        await _repository.InsertAsync(entity, autoSave: true);

        // Varyant sistemi — JENERİK agnostik servise delege ("Product" bağlamı). Çekirdek (nitelik/değer/varyant)
        // serviste; Product-ÖZEL satış fiyatı + reçete uzantısı saveExtension callback'iyle ProductVariantDetail'e bağlanır.
        await SaveSpecificationsAsync(entity, input.Specifications);
        await SaveVariantGraphAsync(entity, input.Attributes, input.Variants);
        // MUADİL: varyantlar O ANKİ stoğa göre OTOMATİK üretilir ("Uygula" yok — 2026-07-25 Hakan kararı;
        // Single: Rank1 → ana reçete, Multi: adaylar ayrı varyant). Graf-save SONRASI koşar ki synchronizer'ın
        // 0-nitelik dalı (tek-ana indirgeme) materyalize varyantları ezmesin (onlar link-less, dal dokunmaz).
        await _substitutionMaterializer.MaterializeAsync(entity);
        await SaveChannelProductsGraphAsync(entity.Id, input.SalesChannelProducts);
        await SaveTrendyolChannelProductsGraphAsync(entity.Id, input.SalesChannelTrendyolProducts);
        await SaveEtsyChannelProductsGraphAsync(entity.Id, input.SalesChannelEtsyProducts);
        // Ürün-seviyesi medya (görsel + video kütüphanesi) link setini persist et (GoodAppService deseni; entity.Id + companyId hazır).
        await _entityMedia.ReplaceForAsync(ProductMediaEntityName, entity.Id, entity.CompanyId, input.Media);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Products.Update)]
    public virtual async Task<ProductGetDto> UpdateAsync(Guid id, ProductUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);
        await ApplyProductCategoryAsync(entity, input.ProductCategoryId);
        var oldImages = entity.Images.ToList();   // yetim blob temizliği için değişim ÖNCESİ resim
        entity.SetImages(MapImages(input.Images));
        entity.SetDiscount(input.DiscountType, input.DiscountValue, input.DiscountStartDate, input.DiscountEndDate);
        entity.SetShelfLife(input.ProductionDate, input.ExpirationDate);
        ApplyMarketplaceDefaults(entity, input.OriginCountryId, input.Condition, input.PreparingDay,
            input.MaxPurchaseQuantity, input.SellerNote,
            input.CurrencyUnitId, input.RecipeTemplateId, input.PackageDesi, input.SpecialInfo, input.AddOns);
        // Varyant modu ÖNCE, muadil konfigürasyonu SONRA (Create ile simetrik; mod dışı alanlar temizlenir).
        await EnsureSubstitutionGroupExistsAsync(input.VariantMode, input.SubstitutionGroupId);
        entity.SetVariantMode(input.VariantMode);
        entity.SetSubstitutionConfig(input.SubstitutionGroupId, input.SubstitutionTargetQuantity,
            input.SubstitutionToleranceType, input.SubstitutionToleranceValue, input.SubstitutionOverrideVariantIds,
            input.SubstitutionVariantMode);
        entity.SetStockPolicy(input.StockPolicy);   // muadilde no-op: SetSubstitutionConfig Calculated'ı zorladı
        await DeleteOrphanImageBlobsAsync(oldImages, entity.Images);
        await _repository.UpdateAsync(entity, autoSave: true);

        // Varyant sistemi — JENERİK agnostik servise delege ("Product" bağlamı). Çekirdek (nitelik/değer/varyant)
        // serviste; Product-ÖZEL satış fiyatı + reçete uzantısı saveExtension callback'iyle ProductVariantDetail'e bağlanır.
        await SaveSpecificationsAsync(entity, input.Specifications);
        await SaveVariantGraphAsync(entity, input.Attributes, input.Variants);
        // MUADİL: varyantlar O ANKİ stoğa göre OTOMATİK üretilir ("Uygula" yok — 2026-07-25 Hakan kararı;
        // Single: Rank1 → ana reçete, Multi: adaylar ayrı varyant). Graf-save SONRASI koşar ki synchronizer'ın
        // 0-nitelik dalı (tek-ana indirgeme) materyalize varyantları ezmesin (onlar link-less, dal dokunmaz).
        await _substitutionMaterializer.MaterializeAsync(entity);
        await SaveChannelProductsGraphAsync(entity.Id, input.SalesChannelProducts);
        await SaveTrendyolChannelProductsGraphAsync(entity.Id, input.SalesChannelTrendyolProducts);
        await SaveEtsyChannelProductsGraphAsync(entity.Id, input.SalesChannelEtsyProducts);
        // Ürün-seviyesi medya (görsel + video kütüphanesi) link setini persist et (GoodAppService deseni).
        await _entityMedia.ReplaceForAsync(ProductMediaEntityName, entity.Id, entity.CompanyId, input.Media);
        return await ToGetDtoAsync(entity);
    }

    /// <summary>N11 satış kanalı ürünleri grafını KANAL AppService'iyle işler (orkestrasyon; SellerCode/Sıra + push
    /// mantığı N11 AppService'te kalır — katman ayrımı). Yeni (Id boş) → Create (ürün Id'siyle); mevcut → Update;
    /// IsDeleted → Delete. Böylece ürün 'Kaydet'inde N11 ürünleri de birlikte kaydedilir (ürün önce kaydedilmiş olur).</summary>
    private async Task SaveChannelProductsGraphAsync(Guid productId, List<SalesChannelTrN11ProductDto>? graph)
    {
        if (graph is null)
        {
            return;
        }

        foreach (var cp in graph)
        {
            if (cp.IsDeleted)
            {
                if (cp.Id != Guid.Empty)
                {
                    await _channelProductAppService.DeleteAsync(cp.Id);
                }

                continue;
            }

            if (cp.Id == Guid.Empty)
            {
                var createInput = ObjectMapper.Map<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductCreateDto>(cp);
                createInput.ProductId = productId;
                createInput.SalesChannelId = cp.SalesChannelId;
                await _channelProductAppService.CreateAsync(createInput);
            }
            else
            {
                var updateInput = ObjectMapper.Map<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductUpdateDto>(cp);
                await _channelProductAppService.UpdateAsync(cp.Id, updateInput);
            }
        }
    }

    /// <summary>Trendyol satış kanalı ürünleri grafını KANAL AppService'iyle işler (N11 <see cref="SaveChannelProductsGraphAsync"/>
    /// birebir karşılığı — çift-kanal ikinci orkestrasyon; N11'e DOKUNULMAZ, additive). Yeni (Id boş) → Create (ürün Id'siyle);
    /// mevcut → Update; IsDeleted → Delete. ProductMainId/Sıra + push mantığı Trendyol AppService'te kalır (katman ayrımı).</summary>
    private async Task SaveTrendyolChannelProductsGraphAsync(Guid productId, List<SalesChannelTrTrendyolProductDto>? graph)
    {
        if (graph is null)
        {
            return;
        }

        foreach (var cp in graph)
        {
            if (cp.IsDeleted)
            {
                if (cp.Id != Guid.Empty)
                {
                    await _trendyolChannelProductAppService.DeleteAsync(cp.Id);
                }

                continue;
            }

            if (cp.Id == Guid.Empty)
            {
                var createInput = ObjectMapper.Map<SalesChannelTrTrendyolProductDto, SalesChannelTrTrendyolProductCreateDto>(cp);
                createInput.ProductId = productId;
                createInput.SalesChannelId = cp.SalesChannelId;
                await _trendyolChannelProductAppService.CreateAsync(createInput);
            }
            else
            {
                var updateInput = ObjectMapper.Map<SalesChannelTrTrendyolProductDto, SalesChannelTrTrendyolProductUpdateDto>(cp);
                await _trendyolChannelProductAppService.UpdateAsync(cp.Id, updateInput);
            }
        }
    }

    /// <summary>Etsy satış kanalı ürünleri grafını KANAL AppService'iyle işler (N11/Trendyol <see cref="SaveChannelProductsGraphAsync"/>
    /// birebir karşılığı — üçüncü kanal orkestrasyonu; N11/Trendyol'a DOKUNULMAZ, additive). Yeni (Id boş) → Create (ürün
    /// Id'siyle); mevcut → Update; IsDeleted → Delete. SellerSkuBase/Sıra mantığı Etsy AppService'te kalır (katman ayrımı).</summary>
    private async Task SaveEtsyChannelProductsGraphAsync(Guid productId, List<SalesChannelEtsyProductDto>? graph)
    {
        if (graph is null)
        {
            return;
        }

        foreach (var cp in graph)
        {
            if (cp.IsDeleted)
            {
                if (cp.Id != Guid.Empty)
                {
                    await _etsyChannelProductAppService.DeleteAsync(cp.Id);
                }

                continue;
            }

            if (cp.Id == Guid.Empty)
            {
                var createInput = ObjectMapper.Map<SalesChannelEtsyProductDto, SalesChannelEtsyProductCreateDto>(cp);
                createInput.ProductId = productId;
                createInput.SalesChannelId = cp.SalesChannelId;
                await _etsyChannelProductAppService.CreateAsync(createInput);
            }
            else
            {
                var updateInput = ObjectMapper.Map<SalesChannelEtsyProductDto, SalesChannelEtsyProductUpdateDto>(cp);
                await _etsyChannelProductAppService.UpdateAsync(cp.Id, updateInput);
            }
        }
    }

    /// <summary>Nitelik grafından varyant ÜRETİMİ — PERSISTSİZ önizleme (DB'ye yazmaz). Çekirdek üretim JENERİK
    /// agnostik serviste (<see cref="IEntityVariantGraphService.GenerateVariants"/>); Product türevine re-project
    /// (satış fiyatı/reçete default — kullanıcı sonra düzenler). Kod/ad + CombinationKey serviste (synchronizer paritesi).</summary>
    public virtual Task<List<ProductVariantGraphDto>> GenerateVariantsAsync(ProductVariantGenerateRequestDto input)
    {
        return Task.FromResult(_entityVariant.GenerateVariants(new EntityVariantGenerateRequestDto
        {
            OwnerName = input.ProductName,
            Attributes = input.Attributes,
        }).Select(CopyCore).ToList());
    }

    /// <summary>Reçete satırlarının CANLI maliyetini PERSISTSİZ hesaplar (tam kayıt gerekmez) — sanal varyant kurup
    /// GetAsync ile AYNI <see cref="PopulateRecipeCostsAsync"/> motorunu çağırır (ülke birimine rebase + calculator).
    /// Satırlar LineOrder sırasına dizilir (calculator ordinal + devreden bu sıraya dayanır). DB'ye YAZMAZ.</summary>
    public virtual async Task<ProductRecipeCostResultDto> CalculateRecipeCostAsync(ProductRecipeCostRequestDto input)
    {
        var ordered = (input?.Lines ?? new List<ProductRecipeLineGraphDto>())
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.LineOrder)
            .ToList();

        var variant = new ProductVariantGraphDto { RecipeLines = ordered };
        await PopulateRecipeCostsAsync(new List<ProductVariantGraphDto> { variant });

        return new ProductRecipeCostResultDto
        {
            NetCost = variant.NetCost,
            NetCostCurrency = variant.NetCostCurrency,
            NetCostMissingRate = variant.NetCostMissingRate,
            Lines = ordered,
        };
    }

    [Authorize(TradeXpressPermissions.Products.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı (Account deseni): ürünü ÖNCE yükle — company query filter yabancı şirketin ürününü
        // gizler → EntityNotFoundException. Doğrulama varyant silmeden ÖNCE olmalı.
        var entity = await _repository.GetAsync(id);
        await DeleteOrphanImageBlobsAsync(entity.Images, newImages: null);   // ürünle birlikte upload blobları da temizlenir
        // Ürün-seviyesi medya linklerini temizle (içerik kütüphanede kalır; yalnız link'ler kaldırılır — GoodAppService deseni).
        await _entityMedia.ReplaceForAsync(ProductMediaEntityName, id, companyId: null, new List<EntityMediaLinkEditDto>());

        // Varyant grafı (nitelik/değer/bağ/varyant) — JENERİK agnostik servise delege ("Product" bağlamı). Varyantlar
        // silinmeden ÖNCE deleteExtension Product-özel uzantıyı (ProductVariantDetail + reçete satırları) temizler (orphan önleme).
        await _entityVariant.DeleteForAsync(
            ProductEntityName, entity.Id,
            deleteExtensionAsync: async ids =>
            {
                await _variantDetailRepository.DeleteAsync(d => ids.Contains(d.EntityVariantId), autoSave: true);
                await _recipeLineRepository.DeleteAsync(r => ids.Contains(r.ProductVariantId), autoSave: true);
            });

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04): normalize et → değiştiyse AYNI ŞİRKET altında
    /// benzersizliği doğrula (kendisi hariç; dostane hata) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(Product entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği ((TenantId, CompanyId, Code) unique index'iyle hizalı).
    /// Create'te <paramref name="excludeId"/>=Guid.Empty, Update'te entity.Id. Dostane BusinessException.</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(p => p.CompanyId == companyId && p.Id != excludeId && p.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Product:CodeAlreadyExists");
        }
    }

    // ── Graf: JENERİK agnostik servise delege (tüm nitelik/değer/varyant mantığı EntityVariantGraphService'te; DRY) ──

    /// <summary>Ürünün varyant grafını saklar: nitelik/değer diff → synchronizer kartezyen → çekirdek varyant
    /// özelleştirmeleri (Kod/Ad OTOMATİK). Product-ÖZEL satış fiyatı + reçete uzantısı saveExtension callback'iyle
    /// çözülen DB varyantına (ProductVariantDetail + reçete satırları) bağlanır. Ürün zaten kaydedilmiş olmalı.
    /// <para><b>Mod kapısı (Dilim-3):</b> SingleVariant/Substitution modunda nitelik grafı SUNUCUDA boşaltılır
    /// (client güven sınırı DEĞİL) — mevcut DB nitelikleri silinmek üzere işaretlenir, synchronizer'ın 0-nitelik
    /// dalı bağlı varyantları silip tek ana varyanta indirir (hazır yol; ana varyantın reçete/fiyat uzantısı
    /// ResolveTargetVariant IsMain eşlemesiyle yaşamaya devam eder).</para></summary>
    /// <summary>
    /// Ürünün GENEL ÖZELLİK değerlerini saklar — kategorinin spesifikasyon nitelikleri için girilen değerler.
    ///
    /// <para><b>Kategori TEK DOĞRU KAYNAK:</b> yalnız ürünün KENDİ kategorisinin (kalıtım dahil) spesifikasyon
    /// niteliklerine ait satırlar kabul edilir. Doğrulanmasaydı istemci keyfi bir nitelik kimliğiyle satır
    /// yazabilir, o satır hiçbir formda görünmeden birikir ve push'ta beklenmedik nitelik olarak giderdi.</para>
    ///
    /// <para>VARYANT ekseni nitelikleri burada DEĞER ALMAZ: onlar kartezyene girer ve değerleri varyantın
    /// kendisinde yaşar. Karıştırılsaydı aynı nitelik hem ürün hem varyant düzeyinde iki farklı değer taşırdı.</para>
    ///
    /// <para>Boş değer = "girilmedi" → satır SİLİNİR (boş satır saklamak, push'a boş nitelik göndermek demekti).
    /// Kategori değişip nitelik artık geçerli değilse o satırlar da temizlenir — bayat değer taşınmaz.</para>
    /// </summary>
    /// <summary>
    /// Ürünün genel özelliklerini bir KANALIN nitelik alanlarına çevirir.
    ///
    /// <para>Zincir: ürünün özellik değeri → kategorinin NİTELİK eşleştirmesi (kanal nitelik adı) → değer için
    /// DEĞER eşleştirmesi (kanal değer adı). Değer eşleştirmesi yoksa ürünün kendi metni gider: pazaryerlerinin
    /// bir kısmı serbest metin kabul eder ve satırı hiç göndermemek, zorunlu niteliği eksik bırakmaktan daha
    /// kötüdür (kullanıcı neyin eksik olduğunu göremezdi).</para>
    ///
    /// <para><b>Nitelik eşleştirmesi OLMAYAN özellik atlanır:</b> kanaldaki hedef alan bilinmeden gönderilen
    /// bir ad, pazaryerinde tanınmaz ve tüm push'u reddettirebilir.</para>
    ///
    /// <para>Ürün HENÜZ KAYDEDİLMEMİŞ olabilir — bu yüzden özellik değerleri istemciden gelir, DB'den değil.</para>
    /// </summary>
    public virtual async Task<List<ProductChannelAttributeDto>> ResolveChannelAttributesAsync(
        ProductChannelAttributeResolveDto input)
    {
        if (input.ProductCategoryId == Guid.Empty || _currentCompany.Id is not { } companyId)
        {
            return new List<ProductChannelAttributeDto>();
        }

        var specifications = (input.Specifications ?? new List<ProductSpecificationDto>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToList();
        if (specifications.Count == 0)
        {
            return new List<ProductChannelAttributeDto>();
        }

        // Nitelik eşleştirmeleri KALITIMLIDIR: eşleştirme ürünün kendi kategorisinde olmayabilir, bir atasında
        // tanımlanmış olabilir (nitelikler de aynı zincirden devralınıyor). Bu yüzden zincirin TAMAMI taranır.
        var chain = await _productCategoryTreeManager.GetPathAsync(companyId, input.ProductCategoryId);
        var chainIds = chain.Select(c => c.Id).ToList();

        var attributeMappings = await AsyncExecuter.ToListAsync(
            (await _channelAttributeMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == companyId
                    && chainIds.Contains(m.ProductCategoryId)
                    && m.Channel == input.Channel));
        if (attributeMappings.Count == 0)
        {
            return new List<ProductChannelAttributeDto>();
        }

        var valueMappings = await AsyncExecuter.ToListAsync(
            (await _channelValueMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == companyId
                    && chainIds.Contains(m.ProductCategoryId)
                    && m.Channel == input.Channel));

        // En DAR tanım kazanır: alt kategori kendi eşleştirmesini yaptıysa atasınınki devreye girmez
        // (kategori eşleştirmesindeki kuralın aynısı). Zincir kökten yaprağa sıralı → sondaki ezer.
        var attributeByCore = new Dictionary<Guid, ProductCategoryChannelAttributeMapping>();
        foreach (var mapping in attributeMappings.OrderBy(m => chainIds.IndexOf(m.ProductCategoryId)))
        {
            attributeByCore[mapping.ProductCategoryAttributeId] = mapping;
        }

        var valueByCore = new Dictionary<Guid, ProductCategoryChannelAttributeValueMapping>();
        foreach (var mapping in valueMappings.OrderBy(m => chainIds.IndexOf(m.ProductCategoryId)))
        {
            valueByCore[mapping.ProductCategoryAttributeValueId] = mapping;
        }

        // Ürün özelliği METİN tutar (serbest girişe izin verilir); kanal DEĞER eşleştirmesi ise değer
        // KİMLİĞİNE asılıdır → metni kategorinin değer tanımlarıyla eşleyip kimliğe çeviririz.
        var effective = await _productCategoryTreeManager.GetEffectiveAttributesAsync(companyId, input.ProductCategoryId);
        var valueIdByAttributeAndText = new Dictionary<(Guid AttributeId, string Text), Guid>();
        foreach (var attribute in effective.Where(a => a.Kind == ProductCategoryAttributeKind.Specification))
        {
            foreach (var value in attribute.Values)
            {
                valueIdByAttributeAndText[(attribute.AttributeId, value.Value.Trim())] = value.ValueId;
            }
        }

        var result = new List<ProductChannelAttributeDto>();
        foreach (var specification in specifications)
        {
            if (!attributeByCore.TryGetValue(specification.ProductCategoryAttributeId, out var attributeMapping))
            {
                continue;
            }

            var text = specification.Value!.Trim();
            var channelValue = text;

            if (valueIdByAttributeAndText.TryGetValue((specification.ProductCategoryAttributeId, text), out var valueId)
                && valueByCore.TryGetValue(valueId, out var valueMapping))
            {
                channelValue = valueMapping.ChannelAttributeValueName ?? valueMapping.ChannelAttributeValueExternalId;
            }

            result.Add(new ProductChannelAttributeDto
            {
                Name = attributeMapping.ChannelAttributeName ?? attributeMapping.ChannelAttributeExternalId,
                Value = channelValue,
            });
        }

        return result;
    }

    private async Task SaveSpecificationsAsync(Product product, List<ProductSpecificationDto> specifications)
    {
        var allowedAttributeIds = await ResolveSpecificationAttributeIdsAsync(product.ProductCategoryId);
        var existing = await AsyncExecuter.ToListAsync(
            (await _specificationRepository.GetQueryableAsync()).Where(x => x.ProductId == product.Id));

        var incoming = (specifications ?? new List<ProductSpecificationDto>())
            .Where(x => allowedAttributeIds.Contains(x.ProductCategoryAttributeId))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.ProductCategoryAttributeId)
            .ToDictionary(g => g.Key, g => g.First().Value!.Trim());

        foreach (var row in existing)
        {
            if (incoming.TryGetValue(row.ProductCategoryAttributeId, out var value))
            {
                row.SetValue(value);
                await _specificationRepository.UpdateAsync(row, autoSave: true);
                incoming.Remove(row.ProductCategoryAttributeId);
            }
            else
            {
                await _specificationRepository.DeleteAsync(row, autoSave: true);
            }
        }

        foreach (var (attributeId, value) in incoming)
        {
            await _specificationRepository.InsertAsync(
                new ProductSpecification(product.CompanyId, product.Id, attributeId, value), autoSave: true);
        }
    }

    /// <summary>Ürünün kategorisinden (kalıtım çözülmüş) SPESİFİKASYON nitelik kimlikleri. Kategori yoksa boş —
    /// o durumda hiçbir özellik satırı kabul edilmez.</summary>
    private async Task<HashSet<Guid>> ResolveSpecificationAttributeIdsAsync(Guid? productCategoryId)
    {
        return (await LoadSpecificationAttributesAsync(productCategoryId))
            .Select(a => a.AttributeId)
            .ToHashSet();
    }

    /// <summary>Kategorinin SPESİFİKASYON nitelikleri (kalıtım çözülmüş). Kategori AppService'i DEĞİL domain
    /// servisi kullanılır: o servis <c>ProductCategories.Default</c> izni ister ve yalnız ürün yetkisi olan bir
    /// kullanıcı kendi ürününü kaydedemez hâle gelirdi.</summary>
    private async Task<List<ProductCategoryEffectiveAttribute>> LoadSpecificationAttributesAsync(Guid? productCategoryId)
    {
        if (productCategoryId is not { } categoryId || categoryId == Guid.Empty
            || _currentCompany.Id is not { } companyId)
        {
            return new List<ProductCategoryEffectiveAttribute>();
        }

        var effective = await _productCategoryTreeManager.GetEffectiveAttributesAsync(companyId, categoryId);
        return effective.Where(a => a.Kind == ProductCategoryAttributeKind.Specification).ToList();
    }

    /// <summary>Ürünün özellik satırlarını FORM için okur — nitelik ADI kategoriden CANLI çözülür (satırda
    /// saklanmaz): kategoride yapılan yeniden adlandırma tüm ürünlere anında yansır. Kategoride artık bulunmayan
    /// (ya da varyant eksenine dönüştürülmüş) nitelik satırları GÖSTERİLMEZ — kaydetmede zaten temizlenirler.</summary>
    private async Task<List<ProductSpecificationDto>> LoadSpecificationDtosAsync(Product product)
    {
        var rows = await AsyncExecuter.ToListAsync(
            (await _specificationRepository.GetQueryableAsync()).Where(x => x.ProductId == product.Id));
        if (rows.Count == 0)
        {
            return new List<ProductSpecificationDto>();
        }

        var nameById = (await LoadSpecificationAttributesAsync(product.ProductCategoryId))
            .ToDictionary(a => a.AttributeId, a => a.Name);

        return rows
            .Where(r => nameById.ContainsKey(r.ProductCategoryAttributeId))
            .Select(r => new ProductSpecificationDto
            {
                Id = r.Id,
                ProductCategoryAttributeId = r.ProductCategoryAttributeId,
                Name = nameById[r.ProductCategoryAttributeId],
                Value = r.Value,
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task SaveVariantGraphAsync(
        Product product, List<EntityAttributeGraphDto> attributes, List<ProductVariantGraphDto> variants)
    {
        var effectiveAttributes = await BuildEffectiveAttributeGraphAsync(product, attributes);
        // MUADİLDE otomatik-kaynaklı reçete satırlarının sahibi SUNUCUDUR (materializer): client formda önizleme
        // için kurduğu kombinasyon (Origin=Substitution) + şablon klonu (Origin=Template) satırlarını da grafla
        // gönderir; Id'siz oldukları için yazılsalar YENİ satır olarak girer, materializer kendininkileri ayrıca
        // yazar ve emtialar reçetede İKİŞER-ÜÇER görünürdü — maliyet katlanıyordu (2026-07-28 Hakan bulgusu).
        // Filtre İNCEDİR: yalnız Id'siz otomatik-kaynaklı satırlar elenir — kullanıcının Manual satırları ve
        // kayıtlı satırların güncelleme/silme işaretleri AYNEN akar ("Reçeteye Uygula" round-trip'i bozulmasın;
        // EfCoreProductVariantModeGateTests bunu korur).
        var substitutionMode = product.VariantMode == ProductVariantMode.Substitution;
        await _entityVariant.SaveGraphAsync(
            ProductEntityName, product.Id, product.CompanyId, product.Name, effectiveAttributes, variants,
            saveExtensionAsync: (dto, variantId) => SaveProductVariantDetailAsync(product.CompanyId, dto, variantId, substitutionMode));
    }

    /// <summary>Mod kapısının nitelik grafı: MultiVariant → client grafı olduğu gibi; SingleVariant/Substitution →
    /// mevcut DB nitelikleri IsDeleted işaretli graf (boş graf YETMEZ — SaveAttributesAsync yalnız işaretlileri
    /// siler; işaretlemeden geçilirse synchronizer DB niteliklerinden kartezyeni yeniden kurardı).</summary>
    private async Task<List<EntityAttributeGraphDto>> BuildEffectiveAttributeGraphAsync(
        Product product, List<EntityAttributeGraphDto> attributes)
    {
        // FromCatalog, MultiVariant'la AYNI üretim mekaniğidir (nitelik×değer kartezyeni) — yalnız
        // niteliklerin KAYNAĞI farklı (şablon katalogu). Bu kapıdan geçmezse nitelikler IsDeleted
        // işaretlenir ve şablondan gelen gruplar kayıtta sessizce silinirdi.
        if (product.VariantMode is ProductVariantMode.MultiVariant or ProductVariantMode.FromCatalog)
        {
            return attributes;
        }

        var existing = await _entityVariant.LoadGraphAsync(ProductEntityName, product.Id);
        return existing.Attributes
            .Where(a => a.Id != Guid.Empty)
            .Select(a => new EntityAttributeGraphDto { Id = a.Id, Name = a.Name, IsDeleted = true })
            .ToList();
    }

    /// <summary>Varyant satış-fiyatı + reçete uzantısı (Product-özel) — çözülen DB varyanta (EntityVariantId) bağlar.
    /// Satış fiyatı <see cref="ProductVariantDetail"/>'e (1:1; yoksa ekle/varsa güncelle); reçete satırları
    /// EntityVariant.Id'ye (<c>ProductVariantRecipeLine.ProductVariantId</c> = jenerik varyant Id). GoodAppService deseni.</summary>
    private async Task SaveProductVariantDetailAsync(
        Guid companyId, EntityVariantGraphDto dto, Guid variantId, bool substitutionMode = false)
    {
        if (dto is not ProductVariantGraphDto g)
        {
            return;
        }

        var detail = await _variantDetailRepository.FirstOrDefaultAsync(x => x.EntityVariantId == variantId)
            ?? new ProductVariantDetail(companyId, variantId);
        detail.SetSalePrice(g.SalePrice, g.SalePriceCurrencyUnitId);
        if (detail.Id == Guid.Empty)
        {
            await _variantDetailRepository.InsertAsync(detail, autoSave: true);
        }
        else
        {
            await _variantDetailRepository.UpdateAsync(detail, autoSave: true);
        }

        var lines = g.RecipeLines;
        if (substitutionMode)
        {
            // Önizleme kopyaları (Id'siz otomatik-kaynaklı) elenir — sahibi materializer; gerekçe SaveVariantGraphAsync'te.
            lines = lines.Where(l => l.Id != Guid.Empty || l.Origin == RecipeLineOrigin.Manual).ToList();
        }

        await SaveRecipeLinesAsync(companyId, variantId, lines);
    }

    // ── reçete grafı (varyant-scope; Id + IsDeleted diff, Account/SubAccount deseni). Bileşen türü set-once
    //    (toolbar tip belirler); LineOrder korunur. Company + varyant Id (jenerik EntityVariant.Id) çağırandan gelir. ──
    private async Task SaveRecipeLinesAsync(Guid companyId, Guid variantId, List<ProductRecipeLineGraphDto> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return;
        }

        foreach (var l in lines.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _recipeLineRepository.DeleteAsync(l.Id, autoSave: true);
        }

        // Kalanları client sırasında (LineOrder) sırala + 0..n-1 YENİDEN NUMARALA → benzersiz/deterministik pozisyon.
        // Türev satırın "yalnız üsttekiler" referans filtresi + calculator ordinal'i bu sıraya dayanır.
        var survivors = lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineOrder).ToList();
        for (var i = 0; i < survivors.Count; i++)
        {
            survivors[i].LineOrder = i;
        }

        RecipeCostPopulator.ValidateDerivedReferences(survivors);

        // 1. geçiş: TÜM satırları insert/update (skaler alanlar; türev SelectedLines kaynakları HARİÇ) →
        // ClientKey→Id (+ ClientKey→entity) sözlükleri (iki-geçişli ClientKey→Id save deseni).
        var idByClientKey = new Dictionary<Guid, Guid>();
        var entityByClientKey = new Dictionary<Guid, ProductVariantRecipeLine>();
        foreach (var l in survivors)
        {
            ProductVariantRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new ProductVariantRecipeLine(companyId, variantId, l.ComponentType, l.LineOrder);
                ApplyRecipeLineFields(entity, l);
                await _recipeLineRepository.InsertAsync(entity, autoSave: true);
                l.Id = entity.Id;
            }
            else
            {
                entity = await _recipeLineRepository.GetAsync(l.Id);
                entity.SetOrder(l.LineOrder);
                ApplyRecipeLineFields(entity, l);
                await _recipeLineRepository.UpdateAsync(entity, autoSave: true);
            }

            idByClientKey[l.ClientKey] = l.Id;
            entityByClientKey[l.ClientKey] = entity;
        }

        // 2. geçiş: türev SelectedLines satırlarının kaynak ClientKey'lerini çözülmüş Id CSV'sine çevir + persist
        // (kaynak Id'ler artık 1. geçişten hazır). AllAbove satırlarının kaynağı yok (SetDerived null'a düşürdü).
        foreach (var l in survivors.Where(x => x.ComponentType == RecipeComponentType.Service
            && x.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines))
        {
            var csv = string.Join('|', l.DerivedSourceKeys.Select(k => idByClientKey[k].ToString()));
            var entity = entityByClientKey[l.ClientKey];
            entity.SetDerivedSources(csv);
            await _recipeLineRepository.UpdateAsync(entity, autoSave: true);
        }
    }

    /// <summary>Graf düğümünün alanlarını reçete satırına uygular — bileşen türüne göre katalog-emtia ya da
    /// hizmet/manuel setter grubu. ComponentType set-once olduğundan burada DEĞİŞTİRİLMEZ (ctor'da atanır).</summary>
    private static void ApplyRecipeLineFields(ProductVariantRecipeLine entity, ProductRecipeLineGraphDto l)
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
            // Hizmet satırı: hizmet referansı (etiket) + türevsel bedel kuralı (taban modu + işlem + operand);
            // SelectedLines kaynakları AYRICA 2. geçişte SetDerivedSources ile (Id'ler o aşamada çözülür).
            entity.SetService(
                l.CommodityId,
                l.DerivedBaseMode.GetValueOrDefault(RecipeDerivedBaseMode.AllAbove),
                l.DerivedOperation.GetValueOrDefault(RecipeDerivedOperation.Percent),
                l.DerivedOperand,
                l.PayUnitId);
        }

        entity.SetDescription(l.Description);
    }

    /// <summary>Pazaryeri-genel varsayılanları üründe ayarlar (Create+Update ortak; entity setterları fail-fast +
    /// normalize eder). SpecialInfo boş key'li satırları eler (SetSpecialInfo).</summary>
    private static void ApplyMarketplaceDefaults(
        Product entity,
        Guid? originCountryId,
        ProductCondition condition,
        int preparingDay,
        int? maxPurchaseQuantity,
        string? sellerNote,
        Guid? currencyUnitId,
        Guid? recipeTemplateId,
        int? packageDesi,
        List<ProductSpecialInfoDto> specialInfo,
        List<ProductAddOnDto> addOns)
    {
        entity.SetOriginCountry(originCountryId);
        entity.SetCondition(condition);
        entity.SetPreparingDay(preparingDay);
        entity.SetMaxPurchaseQuantity(maxPurchaseQuantity);
        entity.SetRecipeTemplate(recipeTemplateId);
        entity.SetPackageDesi(packageDesi);
        entity.SetSellerNote(sellerNote);
        entity.SetCurrencyUnit(currencyUnitId);
        entity.SetSpecialInfo((specialInfo ?? new List<ProductSpecialInfoDto>())
            .Select(s => new ProductSpecialInfo(s.Key, s.Value)));
        entity.SetAddOns((addOns ?? new List<ProductAddOnDto>())
            .Select(a => new ProductAddOn(
                a.AddOnId, a.PriceOverride, a.CurrencyUnitOverrideId, a.IsRequired, a.DisplayOrder, a.Note)));
    }

    /// <summary>Görsel graf düğümlerini owned tiplere çevirir (normalize/kırpma entity SetImages'ta).</summary>
    private static List<ProductImage> MapImages(List<ProductImageGraphDto> images)
    {
        return (images ?? new List<ProductImageGraphDto>())
            .Select(i => new ProductImage(
                i.SourceType, i.Url, i.BlobName, i.FileName, i.DisplayOrder, i.IsDefault, i.VariantId, i.VariantCode))
            .ToList();
    }

    /// <summary>Blob (Upload) görsellerin önizleme data-URL'lerini doldurur — HEP küçük THUMBNAIL blobundan
    /// (tam içerik DTO'ya gömülmez; review'da kanıtlanan 4MB×8 şişmesi + dirty-check maliyeti). Thumbnail
    /// bulunamazsa önizleme boş kalır (fail-open; kayıt görünmeye devam eder).</summary>
    private async Task PopulateImagePreviewsAsync(List<ProductImageGraphDto> images)
    {
        foreach (var image in images.Where(i =>
            i.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(i.BlobName)))
        {
            var thumbnail = await _imageContainer.GetAllBytesOrNullAsync(
                ProductImageAppService.ThumbnailNameOf(image.BlobName!));
            if (thumbnail is not null)
            {
                image.PreviewDataUrl = ProductImageAppService.BuildPreviewDataUrl(thumbnail);
            }
        }
    }

    /// <summary>Artık referans edilmeyen upload bloblarını (ana + thumbnail) siler — görsel silme/değiştirme
    /// update'inde eski blob AppBlobs'ta yetim kalmasın (review bulgusu). Form iptaliyle yetim kalan
    /// (hiç kaydedilmemiş) upload'lar burada YAKALANMAZ — ileride süpürücü işi (bilinçli kabul).</summary>
    private async Task DeleteOrphanImageBlobsAsync(IEnumerable<ProductImage> oldImages, IEnumerable<ProductImage>? newImages)
    {
        var keep = new HashSet<string>(
            (newImages ?? Enumerable.Empty<ProductImage>())
                .Where(i => !string.IsNullOrEmpty(i.BlobName))
                .Select(i => i.BlobName!),
            StringComparer.Ordinal);

        foreach (var image in oldImages.Where(i =>
            i.SourceType == ProductImageSourceType.Upload
            && !string.IsNullOrEmpty(i.BlobName)
            && !keep.Contains(i.BlobName!)))
        {
            await _imageContainer.DeleteAsync(image.BlobName!);
            await _imageContainer.DeleteAsync(ProductImageAppService.ThumbnailNameOf(image.BlobName!));
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, int>> LoadVariantCountsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, int>();

        // Agnostik varyant sayımı — EntityName+EntityId ile daraltılır (tek tablo tüm entity'lere hizmet eder).
        var grouped = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && ids.Contains(v.EntityId))
                .GroupBy(v => v.EntityId)
                .Select(g => new { EntityId = g.Key, Count = g.Count() }));
        return grouped.ToDictionary(x => x.EntityId, x => x.Count);
    }


    /// <summary>Muadil grubu FK'sinin VARLIK doğrulaması (kod-inceleme bulgusu). Aggregate'ler arası referans id-only
    /// olduğundan DB'de FK kısıtı YOKTUR (NavigationConventionTests) → doğrulama olmadan var olmayan/silinmiş bir grup
    /// id'si sessizce persist ediliyor, hata çok sonra BAŞKA ekranda ("Kombinasyon Hesapla" → GroupNotFound) ve boş
    /// override ağacı olarak ortaya çıkıyordu. Kardeş FK (<see cref="ResolveShipmentTemplateNameAsync"/>) zaten
    /// çözerek doğruluyor; burada da kayıt anında fail-fast edilir. Şirket/tenant görünürlüğü global sorgu
    /// filtrelerince zaten sağlanır (yabancı grup zaten bulunamaz).</summary>
    private async Task EnsureSubstitutionGroupExistsAsync(ProductVariantMode variantMode, Guid? substitutionGroupId)
    {
        // Mod Muadil değilse konfigürasyon zaten temizlenir; grup id'si boşsa domain "grup zorunlu" der.
        if (variantMode != ProductVariantMode.Substitution || substitutionGroupId is not { } groupId)
        {
            return;
        }

        if (await _substitutionGroupRepository.FindAsync(groupId) is null)
        {
            throw new BusinessException("TradeXpress:Product:SubstitutionGroupNotFound");
        }
    }

    /// <summary>
    /// Çekirdek kategori bağını atar — kategori ZORUNLUDUR, VAR MI ve AYNI ŞİRKETE Mİ ait doğrulanır. Entity
    /// katalog kaydını göremediğinden bu kontrol burada: doğrulanmasaydı ürün var olmayan (ya da başka şirketin)
    /// bir kategoriye asılı kalır, kanal kategorisi/komisyon çözümü de sessizce boş dönerdi.
    ///
    /// <para><b>Kategori neden zorunlu</b> (2026-07-28 Hakan): kanal kategorisi ve komisyon oranı ürüne
    /// kategorisi üzerinden çözülüyor. Kategorisiz ürün pazaryerine listelenemez ve fiyatı komisyonsuz —
    /// yani eksik — hesaplanır; hata vermediği için bu sessizce yanlış fiyata yol açar.</para>
    ///
    /// <para>Kategorinin KANAL EŞLEŞTİRMESİ ise burada ZORUNLU TUTULMAZ: eşleştirme kategoride yaşar ve
    /// zamanla değişir; kaydetme yoluna kilit koymak, bir eşleştirme silindiğinde o kategorideki tüm ürünleri
    /// düzenlenemez hâle getirirdi (kullanıcı yazım hatasını bile düzeltemezdi). Eşleştirme eksikse ürün formu
    /// UYARIR; gerçek zorunluluk kanala gönderim anında zaten var — kanal ürünü boş kategoriyle kurulamıyor.</para>
    /// </summary>
    private async Task ApplyProductCategoryAsync(Product entity, Guid? productCategoryId)
    {
        if (productCategoryId is not { } categoryId || categoryId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Product:ProductCategoryRequired");
        }

        var category = await _productCategoryRepository.FindAsync(
            x => x.Id == categoryId && x.CompanyId == entity.CompanyId);
        if (category is null)
        {
            throw new BusinessException("TradeXpress:Product:ProductCategoryNotFound");
        }

        entity.SetProductCategory(categoryId);
    }


    private async Task<ProductGetDto> ToGetDtoAsync(Product p)
    {
        // Varyant grafı — JENERİK agnostik servisten (çekirdek: nitelik/değer/varyant, AttributeSummary dolu) +
        // Product-özel satış fiyatı/reçete uzantısı. ProjectVariantsAsync ProductVariantDetail + reçete satırlarını serer
        // + türev kaynak ClientKey çevirisi + CANLI net maliyet hesabını yapar (GoodAppService.ProjectVariantsAsync deseni).
        var graph = await _entityVariant.LoadGraphAsync(ProductEntityName, p.Id);
        var variantDtos = await ProjectVariantsAsync(graph.Variants);
        var specificationDtos = await LoadSpecificationDtosAsync(p);

        var imageDtos = p.Images.Select(i => new ProductImageGraphDto
        {
            SourceType = i.SourceType,
            Url = i.Url,
            BlobName = i.BlobName,
            FileName = i.FileName,
            DisplayOrder = i.DisplayOrder,
            IsDefault = i.IsDefault,
            VariantId = i.VariantId,
            VariantCode = i.VariantCode,
        }).ToList();
        await PopulateImagePreviewsAsync(imageDtos);

        // N11 kanal ürünleri grafı — kanal AppService'inden (canlı kanal filtreli). Yeni üründe boş (Id yok → GetList
        // boş dönmez ama kayıt yoktur). ClientKey kaydedilmiş satırlarda round-trip için yeniden üretilir.
        var channelProducts = await _channelProductAppService.GetListForProductAsync(p.Id);

        // Trendyol kanal ürünleri grafı — N11'den AYRI ikinci liste (çift-kanal; kanal AppService'inden canlı yüklenir).
        var trendyolChannelProducts = await _trendyolChannelProductAppService.GetListForProductAsync(p.Id);

        // Etsy kanal ürünleri grafı — üçüncü kanal listesi (kanal AppService'inden canlı yüklenir; N11/Trendyol'dan AYRI).
        var etsyChannelProducts = await _etsyChannelProductAppService.GetListForProductAsync(p.Id);

        // Ürün-seviyesi medya linkleri (görsel + video kütüphanesi) — agnostik EntityMedia servisinden (GoodAppService deseni).
        var media = await _entityMedia.GetForAsync(ProductMediaEntityName, p.Id);

        return new ProductGetDto
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Description = p.Description,
            ProductCategoryId = p.ProductCategoryId,
            IsActive = p.IsActive,
            Images = imageDtos,
            DiscountType = p.DiscountType,
            DiscountValue = p.DiscountValue,
            DiscountStartDate = p.DiscountStartDate,
            DiscountEndDate = p.DiscountEndDate,
            ProductionDate = p.ProductionDate,
            ExpirationDate = p.ExpirationDate,
            OriginCountryId = p.OriginCountryId,
            IsDomestic = await ResolveIsDomesticAsync(p),
            Condition = p.Condition,
            PreparingDay = p.PreparingDay,
            MaxPurchaseQuantity = p.MaxPurchaseQuantity,
            RecipeTemplateId = p.RecipeTemplateId,
            PackageDesi = p.PackageDesi,
            SellerNote = p.SellerNote,
            CurrencyUnitId = p.CurrencyUnitId,
            Specifications = specificationDtos,
            SpecialInfo = p.SpecialInfo
                .Select(s => new ProductSpecialInfoDto { Key = s.Key, Value = s.Value })
                .ToList(),
            AddOns = p.AddOns
                .Select(a => new ProductAddOnDto
                {
                    AddOnId = a.AddOnId,
                    PriceOverride = a.PriceOverride,
                    CurrencyUnitOverrideId = a.CurrencyUnitOverrideId,
                    IsRequired = a.IsRequired,
                    DisplayOrder = a.DisplayOrder,
                    Note = a.Note,
                })
                .ToList(),
            VariantMode = p.VariantMode,
            SubstitutionGroupId = p.SubstitutionGroupId,
            SubstitutionTargetQuantity = p.SubstitutionTargetQuantity,
            SubstitutionToleranceType = p.SubstitutionToleranceType,
            SubstitutionToleranceValue = p.SubstitutionToleranceValue,
            SubstitutionOverrideVariantIds = p.SubstitutionOverrideVariantIds.ToList(),
            SubstitutionVariantMode = p.SubstitutionVariantMode,
            StockPolicy = p.StockPolicy,
            Media = media,
            SalesChannelProducts = channelProducts,
            SalesChannelTrendyolProducts = trendyolChannelProducts,
            SalesChannelEtsyProducts = etsyChannelProducts,
            Attributes = graph.Attributes,
            Variants = variantDtos,
        };
    }

    /// <summary>Türev SelectedLines satırlarının persist edilmiş kaynak-Id CSV'sini, bu oturumda üretilmiş taze
    /// ClientKey'lere çevirir (UI round-trip + canlı hesap ordinal çözümü için). Kaydetme referans-bütünlüğü
    /// sağladığından Id'ler kardeş satırlara çözülür; çözülemeyen (teorik) parça sessizce atlanır.</summary>
    private static void ResolveDerivedSourceKeys(
        List<ProductVariantGraphDto> variants, List<ProductVariantRecipeLine> entities)
    {
        var sourceCsvById = entities
            .Where(e => e.ComponentType == RecipeComponentType.Service && !string.IsNullOrEmpty(e.DerivedSourceLineIds))
            .ToDictionary(e => e.Id, e => e.DerivedSourceLineIds!);

        foreach (var variant in variants)
        {
            RecipeCostPopulator.ResolveDerivedSourceKeys(variant.RecipeLines, sourceCsvById);
        }
    }

    // ── CANLI reçete maliyeti (design-time; ledger'a YAZMAZ) ─────────────────────────────────────────
    // ORTAK motor RecipeCostPopulator'a delege (ERP + N11 aynı SSOT). Satır-başı alanlar yerinde doldurulur;
    // set-başı net özet varyant DTO'suna yazılır. Değerleme + katalog ÜRÜN başına TEK çekilir (populator içinde).
    private async Task PopulateRecipeCostsAsync(List<ProductVariantGraphDto> variants)
    {
        var costs = await _recipeCostPopulator.PopulateAsync(variants.Select(v => v.RecipeLines).ToList());
        for (var i = 0; i < variants.Count; i++)
        {
            variants[i].NetCost = costs[i].NetCost;
            variants[i].NetCostCurrency = costs[i].NetCostCurrency;
            variants[i].NetCostMissingRate = costs[i].NetCostMissingRate;
        }
    }

    // ── Varyant projeksiyonu (jenerik çekirdek → Product türevi + fiyat/reçete uzantısı; GoodAppService deseni) ──

    /// <summary>Jenerik çekirdek varyantları (base) Product türevine + satış fiyatı/reçete uzantısıyla zenginleştirir:
    /// <see cref="ProductVariantDetail"/> (SalePrice) + reçete satırları (EntityVariant.Id) batch yüklenir; türev
    /// SelectedLines kaynak Id'leri taze ClientKey'lere çevrilir; CANLI net maliyet ÜRÜN başına tek hesaplanır.</summary>
    /// <summary>
    /// "Yerli ürün mü" — menşei ülke ŞİRKETİN ülkesiyle aynı mı. N11'in <c>domestic</c> bayrağı bundan doğar.
    ///
    /// <para>Menşei ya da şirket ülkesi belirtilmemişse <c>null</c>: "bilmiyoruz" ile "yerli değil" farklı
    /// şeylerdir — bilinmiyorken false göndermek ithal ürün beyanı olurdu.</para>
    /// </summary>
    private async Task<bool?> ResolveIsDomesticAsync(Product product)
    {
        if (product.OriginCountryId is not { } originCountryId)
        {
            return null;
        }

        var company = await _companyRepository.FindAsync(product.CompanyId);
        return company?.CountryId is { } companyCountryId ? originCountryId == companyCountryId : null;
    }

    private async Task<List<ProductVariantGraphDto>> ProjectVariantsAsync(List<EntityVariantGraphDto> baseVariants)
    {
        if (baseVariants.Count == 0)
        {
            return new List<ProductVariantGraphDto>();
        }

        var variantIds = baseVariants.Select(v => v.Id).ToList();
        var details = (await AsyncExecuter.ToListAsync(
                (await _variantDetailRepository.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId))))
            .ToDictionary(d => d.EntityVariantId);

        // Reçete satırları — ProductVariantRecipeLine.ProductVariantId artık jenerik EntityVariant.Id taşır (LineOrder sıralı).
        var recipeLines = (await AsyncExecuter.ToListAsync(
                (await _recipeLineRepository.GetQueryableAsync()).Where(r => variantIds.Contains(r.ProductVariantId))))
            .OrderBy(r => r.LineOrder).ThenBy(r => r.CreationTime)
            .ToList();

        var result = new List<ProductVariantGraphDto>();
        foreach (var v in baseVariants)
        {
            var g = CopyCore(v);
            if (details.TryGetValue(v.Id, out var d))
            {
                g.SalePrice = d.SalePrice;
                g.SalePriceCurrencyUnitId = d.SalePriceCurrencyUnitId;
            }

            g.RecipeLines = MapRecipeLines(recipeLines.Where(r => r.ProductVariantId == v.Id));
            result.Add(g);
        }

        // Türev SelectedLines kaynak Id'lerini bu oturumun taze ClientKey'lerine çevir (UI round-trip) — CANLI hesaptan ÖNCE.
        ResolveDerivedSourceKeys(result, recipeLines);

        // CANLI net maliyet — değerleme dict'i ÜRÜN başına BİR KEZ çekilir, tüm varyant/satırlarda yeniden kullanılır.
        await PopulateRecipeCostsAsync(result);
        return result;
    }

    // Jenerik çekirdek alanlarını (EntityVariantGraphDto) Product türevine kopyalar (fiyat/reçete overlay ProjectVariantsAsync'te).
    private static ProductVariantGraphDto CopyCore(EntityVariantGraphDto v)
    {
        return new ProductVariantGraphDto
        {
            Id = v.Id,
            ClientKey = v.ClientKey,
            IsDeleted = v.IsDeleted,
            IsMain = v.IsMain,
            Code = v.Code,
            Name = v.Name,
            Description = v.Description,
            IsActive = v.IsActive,
            Barcode = v.Barcode,
            Gtin = v.Gtin,
            Mpn = v.Mpn,
            Oem = v.Oem,
            StockQuantity = v.StockQuantity,
            AttributeSummary = v.AttributeSummary,
            CombinationKey = v.CombinationKey,
        };
    }

    // Reçete satırları (entity) → graf DTO listesi (LineCost/türev canlı alanları GetAsync projeksiyonunda doldurulur).
    // Param LIST (tek IEntity DEĞİL) — MapSavedRecipeLines deseniyle hizalı; statik entity→DTO konvansiyon ağını tetiklemez.
    private static List<ProductRecipeLineGraphDto> MapRecipeLines(IEnumerable<ProductVariantRecipeLine> lines)
    {
        return lines.Select(r => new ProductRecipeLineGraphDto
        {
            Id = r.Id,
            Origin = r.Origin,
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
        }).ToList();
    }
}
