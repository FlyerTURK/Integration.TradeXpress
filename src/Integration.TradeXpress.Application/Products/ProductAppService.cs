using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels.Variants;
using Integration.TradeXpress.Shipments;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Permissions;
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
    private const string ProductMediaEntityName = "Product";

    private readonly IRepository<Product, Guid> _repository;
    private readonly IRepository<ShipmentTemplate, Guid> _shipmentTemplateRepository;   // yalnız OKUMA — FK→ad çözümü (K8-Faz1)
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
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
        IRepository<ShipmentTemplate, Guid> shipmentTemplateRepository,
        IEntityVariantGraphService entityVariant,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> variantDetailRepository,
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
        _shipmentTemplateRepository = shipmentTemplateRepository;
        _entityVariant = entityVariant;
        _variantRepository = variantRepository;
        _variantDetailRepository = variantDetailRepository;
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
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

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
        entity.SetImages(MapImages(input.Images));
        entity.SetDiscount(input.DiscountType, input.DiscountValue, input.DiscountStartDate, input.DiscountEndDate);
        entity.SetShelfLife(input.ProductionDate, input.ExpirationDate);
        entity.SetPersonalization(input.IsPersonalizable, input.PersonalizationInstructions,
            input.PersonalizationIsRequired, input.PersonalizationCharCountMax);
        ApplyMarketplaceDefaults(entity, input.Domestic, input.Condition, input.PreparingDay,
            await ResolveShipmentTemplateNameAsync(input.ShipmentTemplateId, input.ShipmentTemplateName),
            input.ShipmentTemplateId, input.MaxPurchaseQuantity, input.SellerNote,
            input.CurrencyUnitId, input.SpecialInfo, input.AddOns);
        await _repository.InsertAsync(entity, autoSave: true);

        // Varyant sistemi — JENERİK agnostik servise delege ("Product" bağlamı). Çekirdek (nitelik/değer/varyant)
        // serviste; Product-ÖZEL satış fiyatı + reçete uzantısı saveExtension callback'iyle ProductVariantDetail'e bağlanır.
        await SaveVariantGraphAsync(entity, input.Attributes, input.Variants);
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
        var oldImages = entity.Images.ToList();   // yetim blob temizliği için değişim ÖNCESİ resim
        entity.SetImages(MapImages(input.Images));
        entity.SetDiscount(input.DiscountType, input.DiscountValue, input.DiscountStartDate, input.DiscountEndDate);
        entity.SetShelfLife(input.ProductionDate, input.ExpirationDate);
        entity.SetPersonalization(input.IsPersonalizable, input.PersonalizationInstructions,
            input.PersonalizationIsRequired, input.PersonalizationCharCountMax);
        ApplyMarketplaceDefaults(entity, input.Domestic, input.Condition, input.PreparingDay,
            await ResolveShipmentTemplateNameAsync(input.ShipmentTemplateId, input.ShipmentTemplateName),
            input.ShipmentTemplateId, input.MaxPurchaseQuantity, input.SellerNote,
            input.CurrencyUnitId, input.SpecialInfo, input.AddOns);
        await DeleteOrphanImageBlobsAsync(oldImages, entity.Images);
        await _repository.UpdateAsync(entity, autoSave: true);

        // Varyant sistemi — JENERİK agnostik servise delege ("Product" bağlamı). Çekirdek (nitelik/değer/varyant)
        // serviste; Product-ÖZEL satış fiyatı + reçete uzantısı saveExtension callback'iyle ProductVariantDetail'e bağlanır.
        await SaveVariantGraphAsync(entity, input.Attributes, input.Variants);
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
    /// çözülen DB varyantına (ProductVariantDetail + reçete satırları) bağlanır. Ürün zaten kaydedilmiş olmalı.</summary>
    private async Task SaveVariantGraphAsync(
        Product product, List<EntityAttributeGraphDto> attributes, List<ProductVariantGraphDto> variants)
    {
        await _entityVariant.SaveGraphAsync(
            ProductEntityName, product.Id, product.CompanyId, product.Name, attributes, variants,
            saveExtensionAsync: (dto, variantId) => SaveProductVariantDetailAsync(product.CompanyId, dto, variantId));
    }

    /// <summary>Varyant satış-fiyatı + reçete uzantısı (Product-özel) — çözülen DB varyanta (EntityVariantId) bağlar.
    /// Satış fiyatı <see cref="ProductVariantDetail"/>'e (1:1; yoksa ekle/varsa güncelle); reçete satırları
    /// EntityVariant.Id'ye (<c>ProductVariantRecipeLine.ProductVariantId</c> = jenerik varyant Id). GoodAppService deseni.</summary>
    private async Task SaveProductVariantDetailAsync(Guid companyId, EntityVariantGraphDto dto, Guid variantId)
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

        await SaveRecipeLinesAsync(companyId, variantId, g.RecipeLines);
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
        bool domestic,
        ProductCondition condition,
        int preparingDay,
        string? shipmentTemplateName,
        Guid? shipmentTemplateId,
        int? maxPurchaseQuantity,
        string? sellerNote,
        Guid? currencyUnitId,
        List<ProductSpecialInfoDto> specialInfo,
        List<ProductAddOnDto> addOns)
    {
        entity.SetDomestic(domestic);
        entity.SetCondition(condition);
        entity.SetPreparingDay(preparingDay);
        entity.SetShipmentTemplate(shipmentTemplateName);
        entity.SetShipmentTemplateId(shipmentTemplateId);
        entity.SetMaxPurchaseQuantity(maxPurchaseQuantity);
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

    /// <summary>K8-Faz1: kargo şablonu adının OKUMA tek kaynağı FK — <paramref name="shipmentTemplateId"/> doluysa
    /// çekirdek <see cref="ShipmentTemplate.Name"/> çözülür; FK boş ya da şablon bulunamıyorsa (silinmiş/bayat)
    /// legacy string'e düşülür (kırmama garantisi). Yazma yolu da aynı çözümü kullanır → legacy kolon FK'den senkron
    /// dolan denormalize snapshot olur (<see cref="ShipmentTemplate.SetCarrier"/> id+ad deseni); kolonun fiziksel
    /// kaldırılması Faz-4 (K8).</summary>
    private async Task<string?> ResolveShipmentTemplateNameAsync(Guid? shipmentTemplateId, string? legacyName)
    {
        if (shipmentTemplateId is not { } id)
        {
            return legacyName;
        }

        var template = await _shipmentTemplateRepository.FindAsync(id);
        return template?.Name ?? legacyName;
    }

    private async Task<ProductGetDto> ToGetDtoAsync(Product p)
    {
        // Varyant grafı — JENERİK agnostik servisten (çekirdek: nitelik/değer/varyant, AttributeSummary dolu) +
        // Product-özel satış fiyatı/reçete uzantısı. ProjectVariantsAsync ProductVariantDetail + reçete satırlarını serer
        // + türev kaynak ClientKey çevirisi + CANLI net maliyet hesabını yapar (GoodAppService.ProjectVariantsAsync deseni).
        var graph = await _entityVariant.LoadGraphAsync(ProductEntityName, p.Id);
        var variantDtos = await ProjectVariantsAsync(graph.Variants);

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
            IsActive = p.IsActive,
            Images = imageDtos,
            DiscountType = p.DiscountType,
            DiscountValue = p.DiscountValue,
            DiscountStartDate = p.DiscountStartDate,
            DiscountEndDate = p.DiscountEndDate,
            ProductionDate = p.ProductionDate,
            ExpirationDate = p.ExpirationDate,
            Domestic = p.Domestic,
            Condition = p.Condition,
            PreparingDay = p.PreparingDay,
            // K8-Faz1: kargo şablonu adının OKUMA tek kaynağı FK — legacy string yalnız fallback.
            ShipmentTemplateName = await ResolveShipmentTemplateNameAsync(p.ShipmentTemplateId, p.ShipmentTemplateName),
            ShipmentTemplateId = p.ShipmentTemplateId,
            MaxPurchaseQuantity = p.MaxPurchaseQuantity,
            SellerNote = p.SellerNote,
            CurrencyUnitId = p.CurrencyUnitId,
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
            IsPersonalizable = p.IsPersonalizable,
            PersonalizationInstructions = p.PersonalizationInstructions,
            PersonalizationIsRequired = p.PersonalizationIsRequired,
            PersonalizationCharCountMax = p.PersonalizationCharCountMax,
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
