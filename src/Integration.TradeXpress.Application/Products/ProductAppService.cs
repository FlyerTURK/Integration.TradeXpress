using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels.Variants;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Permissions;
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
/// GÖNDERMEZ). Kimlik (Code uppercase normalize, şirket-scope benzersizlik). Nitelikler + değerleri in-memory
/// grafla yönetilir (add/update/delete; ürün başına en fazla 5 — <see cref="ProductAttributeConsts"/>).
/// Varyantlar ELLE EKLENMEZ/SİLİNMEZ: nitelik×değer kartezyeninden <see cref="ProductVariantSynchronizer"/>
/// ÜRETİR (save sonunda); grafla yalnız mevcut varyant GÜNCELLENİR (Code/Name/Description/IsActive).
/// </summary>
[Authorize(TradeXpressPermissions.Products.Default)]
public class ProductAppService : TradeXpressAppService, IProductAppService
{
    private readonly IRepository<Product, Guid> _repository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _valueRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _linkRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly ProductVariantManager _variantManager;
    private readonly ProductVariantSynchronizer _variantSynchronizer;
    private readonly RecipeCostPopulator _recipeCostPopulator;
    private readonly ICurrentCompany _currentCompany;
    private readonly IBlobContainer<ProductImagesContainer> _imageContainer;
    private readonly ISalesChannelTrN11ProductAppService _channelProductAppService;
    private readonly ISalesChannelTrTrendyolProductAppService _trendyolChannelProductAppService;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public ProductAppService(
        IRepository<Product, Guid> repository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<ProductAttribute, Guid> attributeRepository,
        IRepository<ProductAttributeValue, Guid> valueRepository,
        IRepository<ProductVariantAttributeValue, Guid> linkRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        ProductVariantManager variantManager,
        ProductVariantSynchronizer variantSynchronizer,
        RecipeCostPopulator recipeCostPopulator,
        ICurrentCompany currentCompany,
        IBlobContainer<ProductImagesContainer> imageContainer,
        ISalesChannelTrN11ProductAppService channelProductAppService,
        ISalesChannelTrTrendyolProductAppService trendyolChannelProductAppService)
    {
        _repository = repository;
        _variantRepository = variantRepository;
        _attributeRepository = attributeRepository;
        _valueRepository = valueRepository;
        _linkRepository = linkRepository;
        _recipeLineRepository = recipeLineRepository;
        _variantManager = variantManager;
        _variantSynchronizer = variantSynchronizer;
        _recipeCostPopulator = recipeCostPopulator;
        _currentCompany = currentCompany;
        _imageContainer = imageContainer;
        _channelProductAppService = channelProductAppService;
        _trendyolChannelProductAppService = trendyolChannelProductAppService;
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
        ApplyMarketplaceDefaults(entity, input.Domestic, input.Condition, input.PreparingDay,
            input.ShipmentTemplateName, input.MaxPurchaseQuantity, input.SellerNote, input.CurrencyUnitId,
            input.SpecialInfo);
        await _repository.InsertAsync(entity, autoSave: true);

        var valueIdByClientKey = await SaveAttributesAsync(entity, input.Attributes);
        // DB mutabakatı (kartezyen üret/temizle) + en-az-1 + tekil-main garantisi; SONRA kullanıcının
        // kaydet-öncesi varyant özelleştirmeleri (Id ya da CombinationKey eşlemesiyle) uygulanır.
        await _variantSynchronizer.SynchronizeAsync(entity);
        await ApplyVariantCustomizationsAsync(entity, input.Variants, valueIdByClientKey);
        await SaveChannelProductsGraphAsync(entity.Id, input.SalesChannelProducts);
        await SaveTrendyolChannelProductsGraphAsync(entity.Id, input.SalesChannelTrendyolProducts);
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
        ApplyMarketplaceDefaults(entity, input.Domestic, input.Condition, input.PreparingDay,
            input.ShipmentTemplateName, input.MaxPurchaseQuantity, input.SellerNote, input.CurrencyUnitId,
            input.SpecialInfo);
        await DeleteOrphanImageBlobsAsync(oldImages, entity.Images);
        await _repository.UpdateAsync(entity, autoSave: true);

        var valueIdByClientKey = await SaveAttributesAsync(entity, input.Attributes);
        // DB mutabakatı (kartezyen üret/temizle) + en-az-1 + tekil-main garantisi; SONRA kullanıcının
        // kaydet-öncesi varyant özelleştirmeleri (Id ya da CombinationKey eşlemesiyle) uygulanır.
        await _variantSynchronizer.SynchronizeAsync(entity);
        await ApplyVariantCustomizationsAsync(entity, input.Variants, valueIdByClientKey);
        await SaveChannelProductsGraphAsync(entity.Id, input.SalesChannelProducts);
        await SaveTrendyolChannelProductsGraphAsync(entity.Id, input.SalesChannelTrendyolProducts);
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

    /// <summary>Nitelik grafından varyant ÜRETİMİ — PERSISTSİZ önizleme (DB'ye yazmaz, kayıt gerekmez).
    /// Kartezyen + kod/ad türetme <see cref="ProductVariantSynchronizer"/> ile AYNI (public static helper'lar);
    /// ilk satır IsMain (display), hepsi aktif; <c>CombinationKey</c> = değer ClientKey'lerinin sıralı join'i
    /// (kayıtta özelleştirme eşlemesi için round-trip edilir).</summary>
    public virtual Task<List<ProductVariantGraphDto>> GenerateVariantsAsync(ProductVariantGenerateRequestDto input)
    {
        var result = new List<ProductVariantGraphDto>();
        var axes = BuildGenerationAxes(input.Attributes);
        if (axes.Count == 0)
        {
            return Task.FromResult(result);   // nitelik yok → üretilecek kombinasyon yok (base varyant save'de doğar)
        }

        // DTO kartezyeni — çekirdek motor (synchronizer'la AYNI matematik; eski BuildDtoCartesian duplikasyonu eridi).
        foreach (var combination in VariantCombinationEngine.BuildCartesian<GenerationAxisItem>(axes))
        {
            var valueNames = combination.Select(x => x.NormalizedValue).ToList();
            // Kombinasyon özeti "Nitelik: Değer" çiftleri (attribute DisplayOrder = eksen sırası), ", " join.
            var summary = string.Join(", ", combination.Select(x => $"{x.AttributeName}: {x.NormalizedValue}"));
            result.Add(new ProductVariantGraphDto
            {
                IsMain = result.Count == 0,   // display-only; kalıcı main garantisi manager/synchronizer'da
                Code = ProductVariantSynchronizer.BuildVariantCode(valueNames).ToUpperInvariant(),
                Name = ProductVariantSynchronizer.BuildVariantName(input.ProductName?.Trim() ?? string.Empty, valueNames).Trim(),
                IsActive = true,
                AttributeSummary = summary,
                CombinationKey = BuildCombinationKeyFromClientKeys(combination.Select(x => x.Value.ClientKey)),
            });
        }

        return Task.FromResult(result);
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
        await DeleteRecipeLinesOfProductAsync(entity.Id);
        await DeleteAttributeGraphOfProductAsync(entity.Id);
        await _variantManager.DeleteVariantsOfProductAsync(entity.Id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Ürünün TÜM varyantlarının reçete satırlarını siler — varyantlar silinmeden önce (orphan önleme).</summary>
    private async Task DeleteRecipeLinesOfProductAsync(Guid productId)
    {
        var variantIds = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == productId).Select(v => v.Id));
        if (variantIds.Count == 0)
        {
            return;
        }

        await _recipeLineRepository.DeleteAsync(r => variantIds.Contains(r.ProductVariantId), autoSave: true);
    }

    /// <summary>Ürünün nitelik grafını (bağ + değer + nitelik satırları) siler — ürün silinmeden önce.
    /// Her bağ (link) bu ürünün bir niteliğine işaret ettiğinden attribute-id kümesi tüm bağları kapsar.</summary>
    private async Task DeleteAttributeGraphOfProductAsync(Guid productId)
    {
        var attributeIds = await AsyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == productId).Select(a => a.Id));
        if (attributeIds.Count == 0)
        {
            return;
        }

        await _linkRepository.DeleteAsync(l => attributeIds.Contains(l.ProductAttributeId), autoSave: true);
        await _valueRepository.DeleteAsync(v => attributeIds.Contains(v.ProductAttributeId), autoSave: true);
        await _attributeRepository.DeleteAsync(a => a.ProductId == productId, autoSave: true);
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

    // ── varyant grafı: YALNIZ ÖZELLEŞTİRME (attribute-driven kural) — varyantlar elle EKLENMEZ/SİLİNMEZ
    //    (synchronizer üretir/temizler; IsDeleted YOKSAYILIR). Senkron SONRASI çalışır: satır Id ile (mevcut)
    //    ya da CombinationKey ile (üretim önizlemesinden gelen, henüz Id'siz) DB varyantına eşlenir; kullanıcının
    //    kaydet-öncesi Code/Name/Description/IsActive dokunuşları (ör. pasife çekme) KAYBOLMAZ. ──
    private async Task ApplyVariantCustomizationsAsync(
        Product product,
        List<ProductVariantGraphDto> variants,
        Dictionary<Guid, Guid> valueIdByClientKey)
    {
        if (variants == null || variants.Count == 0) return;

        var dbVariants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == product.Id));
        var variantIds = dbVariants.Select(v => v.Id).ToList();
        var links = variantIds.Count == 0
            ? new List<ProductVariantAttributeValue>()
            : await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));

        // Kombinasyon imzası (sıralı valueId join) → DB varyantı — synchronizer ile AYNI anahtar (DRY).
        var byCombination = dbVariants.ToDictionary(
            v => ProductVariantSynchronizer.BuildKey(
                links.Where(l => l.ProductVariantId == v.Id).Select(l => l.ProductAttributeValueId)),
            v => v);

        foreach (var v in variants)
        {
            var target = ResolveTargetVariant(v);
            if (target == null)
            {
                continue;   // senkronun sildiği / eşleşmeyen (bayat önizleme) satır → yoksay
            }

            await ApplyVariantFieldsAsync(product, target, v);
            await SaveRecipeLinesAsync(target, v.RecipeLines);
        }

        // Satırın hedef DB varyantı: (a) Id doluysa Id ile; (b) Id boşsa CombinationKey'in değer
        // ClientKey'leri persist eşlemesinden ValueId'lere çevrilir → aynı kombinasyonlu varyant.
        ProductVariant? ResolveTargetVariant(ProductVariantGraphDto dto)
        {
            if (dto.Id != Guid.Empty)
            {
                return dbVariants.FirstOrDefault(x => x.Id == dto.Id);
            }

            // Yeni ürünün seed'lenmiş base main'i (Id yok, IsMain, kombinasyon yok) → server'ın yarattığı DB main'e
            // eşle → Yeni'de girilen reçete/özelleştirme ana varyanta yazılır (ANAVARYANT set = server ile aynı).
            if (dto.IsMain && string.IsNullOrEmpty(dto.CombinationKey))
            {
                return dbVariants.FirstOrDefault(x => x.IsMain);
            }

            if (string.IsNullOrEmpty(dto.CombinationKey))
            {
                return null;
            }

            var valueIds = new List<Guid>();
            foreach (var part in dto.CombinationKey.Split('|'))
            {
                if (!Guid.TryParse(part, out var clientKey) || !valueIdByClientKey.TryGetValue(clientKey, out var valueId))
                {
                    return null;   // değer bu kayıtta persist edilmedi (silinmiş/bayat) → eşleşme yok
                }

                valueIds.Add(valueId);
            }

            return byCombination.GetValueOrDefault(ProductVariantSynchronizer.BuildKey(valueIds));
        }
    }

    /// <summary>Kullanıcı özelleştirmelerini varyanta uygular — ürün-scope kod benzersizliği korunur;
    /// IsMain'e DOKUNULMAZ (display-only; değişmez manager'da).</summary>
    private async Task ApplyVariantFieldsAsync(Product product, ProductVariant variant, ProductVariantGraphDto v)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            v.Code, nameof(ProductVariant.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        if (!string.Equals(normalizedCode, variant.Code, StringComparison.Ordinal))
        {
            await EnsureVariantCodeUniqueAsync(product.Id, normalizedCode, variant.Id);
            variant.SetCode(normalizedCode);
        }

        variant.SetName(v.Name);
        variant.SetDescription(v.Description);
        variant.SetActive(v.IsActive);
        variant.SetSalePrice(v.SalePrice, v.SalePriceCurrencyUnitId);
        variant.SetStock(v.StockQuantity);
        variant.SetTradeIdentifiers(v.Barcode, v.Gtin, v.Mpn, v.Oem);
        await _variantRepository.UpdateAsync(variant, autoSave: true);
    }

    /// <summary>Aynı ÜRÜN altında varyant Code benzersizliği. Dostane BusinessException — ham DB çakışmasını önler.</summary>
    private async Task EnsureVariantCodeUniqueAsync(Guid productId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(x => x.ProductId == productId && x.Id != excludeId && x.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:ProductVariant:CodeAlreadyExists");
        }
    }

    // ── reçete grafı (varyant-scope; Id + IsDeleted diff, Account/SubAccount deseni). Bileşen türü set-once
    //    (toolbar tip belirler); LineOrder korunur. Company varyanttan denormalize. ──
    private async Task SaveRecipeLinesAsync(ProductVariant variant, List<ProductRecipeLineGraphDto> lines)
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
        // ClientKey→Id (+ ClientKey→entity) sözlükleri (SaveAttributesAsync valueIdByClientKey deseni).
        var idByClientKey = new Dictionary<Guid, Guid>();
        var entityByClientKey = new Dictionary<Guid, ProductVariantRecipeLine>();
        foreach (var l in survivors)
        {
            ProductVariantRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new ProductVariantRecipeLine(variant.CompanyId, variant.Id, l.ComponentType, l.LineOrder);
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

    // ── nitelik grafı diff (Id + IsDeleted) — değerler nitelik başına iç graf. DÖNÜŞ: değer ClientKey →
    //    persist-edilen ValueId eşlemesi (CombinationKey'li varyant özelleştirmelerinin çözümü için). ──
    private async Task<Dictionary<Guid, Guid>> SaveAttributesAsync(Product product, List<ProductAttributeGraphDto> attributes)
    {
        var valueIdByClientKey = new Dictionary<Guid, Guid>();
        if (attributes == null) return valueIdByClientKey;

        // Önce silinenler: nitelik + TÜM değer satırları. Bağ (link) satırlarına dokunulmaz —
        // sonda çalışan synchronizer kalkan kombinasyonların varyant+bağlarını zaten temizler.
        foreach (var a in attributes.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _valueRepository.DeleteAsync(v => v.ProductAttributeId == a.Id, autoSave: true);
            await _attributeRepository.DeleteAsync(a.Id, autoSave: true);
        }

        // Graf ürünün TAM nitelik resmi (GetAsync hepsini döner) → max-5 + ad benzersizliği girdi üzerinde.
        var survivors = attributes.Where(x => !x.IsDeleted).ToList();
        if (survivors.Count > ProductAttributeConsts.MaxAttributesPerProduct)
        {
            throw new BusinessException("TradeXpress:Product:TooManyAttributes");
        }

        EnsureAttributeNamesUnique(survivors);
        EnsureEveryAttributeHasValue(survivors);

        foreach (var a in survivors)
        {
            if (a.Id == Guid.Empty)
            {
                var attribute = new ProductAttribute(product.CompanyId, product.Id, a.Name, a.DisplayOrder);
                await _attributeRepository.InsertAsync(attribute, autoSave: true);
                a.Id = attribute.Id;   // değer grafı yeni niteliğe bağlanabilsin
            }
            else
            {
                var attribute = await _attributeRepository.GetAsync(a.Id);
                attribute.SetName(a.Name);
                attribute.SetDisplayOrder(a.DisplayOrder);
                await _attributeRepository.UpdateAsync(attribute, autoSave: true);
            }

            await SaveAttributeValuesAsync(product, a, valueIdByClientKey);
        }

        return valueIdByClientKey;
    }

    /// <summary>Her (silinmemiş) nitelik en az bir (silinmemiş) değer içermeli — değersiz nitelik kaydedilemez;
    /// üretim (GenerateVariants) tarafıyla AYNI kural. Synchronizer'daki değersiz-eksen dalı savunma olarak kalır.</summary>
    private static void EnsureEveryAttributeHasValue(List<ProductAttributeGraphDto> survivors)
    {
        var hasEmptyAttribute = survivors.Any(a => a.Values == null || a.Values.All(v => v.IsDeleted));
        if (hasEmptyAttribute)
        {
            throw new BusinessException("TradeXpress:ProductAttribute:ValueRequired");
        }
    }

    /// <summary>Aynı üründe aynı adlı iki nitelik olamaz — normalize (TitleCase) adlar üzerinden dostane hata.</summary>
    private static void EnsureAttributeNamesUnique(List<ProductAttributeGraphDto> survivors)
    {
        var names = survivors.Select(a => StringFieldGuard.NormalizeName(
            a.Name, nameof(ProductAttribute.Name), EntityFieldConsts.NameMinLength, ProductAttributeConsts.NameMaxLength));
        if (HasDuplicate(names))
        {
            throw new BusinessException("TradeXpress:ProductAttribute:NameAlreadyExists");
        }
    }

    // ── değer grafı diff — nitelik başına (parent attribute Id'si SaveAttributesAsync'te garanti dolu).
    //    Persist edilen her değer için ClientKey→ValueId eşlemesi doldurulur (CombinationKey çözümü). ──
    private async Task SaveAttributeValuesAsync(
        Product product,
        ProductAttributeGraphDto attribute,
        Dictionary<Guid, Guid> valueIdByClientKey)
    {
        if (attribute.Values == null) return;

        foreach (var v in attribute.Values.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _valueRepository.DeleteAsync(v.Id, autoSave: true);
        }

        var survivors = attribute.Values.Where(x => !x.IsDeleted).ToList();
        EnsureAttributeValuesUnique(survivors);

        foreach (var v in survivors)
        {
            if (v.Id == Guid.Empty)
            {
                var value = new ProductAttributeValue(product.CompanyId, attribute.Id, v.Value, v.DisplayOrder);
                await _valueRepository.InsertAsync(value, autoSave: true);
                v.Id = value.Id;
            }
            else
            {
                var value = await _valueRepository.GetAsync(v.Id);
                value.SetValue(v.Value);
                value.SetDisplayOrder(v.DisplayOrder);
                await _valueRepository.UpdateAsync(value, autoSave: true);
            }

            valueIdByClientKey[v.ClientKey] = v.Id;
        }
    }

    /// <summary>Aynı nitelikte aynı değer iki kez olamaz — normalize değerler üzerinden dostane hata.</summary>
    private static void EnsureAttributeValuesUnique(List<ProductAttributeValueGraphDto> survivors)
    {
        var values = survivors.Select(v => StringFieldGuard.NormalizeName(
            v.Value, nameof(ProductAttributeValue.Value), EntityFieldConsts.NameMinLength, ProductAttributeConsts.ValueMaxLength));
        if (HasDuplicate(values))
        {
            throw new BusinessException("TradeXpress:ProductAttributeValue:ValueAlreadyExists");
        }
    }

    private static bool HasDuplicate(IEnumerable<string> normalized)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return normalized.Any(n => !seen.Add(n));
    }

    // ── persistsiz üretim (GenerateVariants) yardımcıları — sıralama/türetme synchronizer paritesinde ──

    /// <summary>Üretim eksenleri: silinmemiş nitelikler (DisplayOrder→Name) × silinmemiş, NORMALİZE değerler
    /// (DisplayOrder→Value) — synchronizer'ın entity sıralamasıyla AYNI. Her öğe niteliğin NORMALİZE adını da
    /// taşır (kombinasyon özeti "Nitelik: Değer" için). Değersiz nitelik → dostane hata
    /// (kayıt tarafındaki <see cref="EnsureEveryAttributeHasValue"/> ile aynı kural).</summary>
    private static List<List<GenerationAxisItem>> BuildGenerationAxes(List<ProductAttributeGraphDto> attributes)
    {
        var survivors = (attributes ?? new List<ProductAttributeGraphDto>())
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();

        var axes = new List<List<GenerationAxisItem>>();
        foreach (var attribute in survivors)
        {
            var attributeName = StringFieldGuard.NormalizeName(
                attribute.Name, nameof(ProductAttribute.Name), EntityFieldConsts.NameMinLength, ProductAttributeConsts.NameMaxLength);

            var values = (attribute.Values ?? new List<ProductAttributeValueGraphDto>())
                .Where(v => !v.IsDeleted)
                .Select(v => new GenerationAxisItem(
                    v,
                    StringFieldGuard.NormalizeName(
                        v.Value, nameof(ProductAttributeValue.Value), EntityFieldConsts.NameMinLength, ProductAttributeConsts.ValueMaxLength),
                    attributeName))
                .OrderBy(x => x.Value.DisplayOrder).ThenBy(x => x.NormalizedValue)
                .ToList();

            if (values.Count == 0)
            {
                throw new BusinessException("TradeXpress:ProductAttribute:ValueRequired");
            }

            axes.Add(values);
        }

        return axes;
    }

    /// <summary>Üretim ekseninin bir öğesi — değer DTO'su + normalize değer + normalize nitelik adı
    /// (kombinasyon özeti "Nitelik: Değer" için gerekli).</summary>
    private sealed record GenerationAxisItem(ProductAttributeValueGraphDto Value, string NormalizedValue, string AttributeName);

    /// <summary>Kombinasyonun istemci-taraflı kimliği — değer ClientKey'lerinin SIRALI "|" join'i
    /// (synchronizer BuildKey ile aynı biçim; sunucu üretir, client round-trip eder).</summary>
    private static string BuildCombinationKeyFromClientKeys(IEnumerable<Guid> clientKeys)
    {
        return string.Join("|", clientKeys.OrderBy(k => k));
    }

    /// <summary>Pazaryeri-genel varsayılanları üründe ayarlar (Create+Update ortak; entity setterları fail-fast +
    /// normalize eder). SpecialInfo boş key'li satırları eler (SetSpecialInfo).</summary>
    private static void ApplyMarketplaceDefaults(
        Product entity,
        bool domestic,
        ProductCondition condition,
        int preparingDay,
        string? shipmentTemplateName,
        int? maxPurchaseQuantity,
        string? sellerNote,
        Guid? currencyUnitId,
        List<ProductSpecialInfoDto> specialInfo)
    {
        entity.SetDomestic(domestic);
        entity.SetCondition(condition);
        entity.SetPreparingDay(preparingDay);
        entity.SetShipmentTemplate(shipmentTemplateName);
        entity.SetMaxPurchaseQuantity(maxPurchaseQuantity);
        entity.SetSellerNote(sellerNote);
        entity.SetCurrencyUnit(currencyUnitId);
        entity.SetSpecialInfo((specialInfo ?? new List<ProductSpecialInfoDto>())
            .Select(s => new ProductSpecialInfo(s.Key, s.Value)));
    }

    /// <summary>Görsel graf düğümlerini owned tiplere çevirir (normalize/kırpma entity SetImages'ta).</summary>
    private static List<ProductImage> MapImages(List<ProductImageGraphDto> images)
    {
        return (images ?? new List<ProductImageGraphDto>())
            .Select(i => new ProductImage(i.SourceType, i.Url, i.BlobName, i.FileName, i.DisplayOrder, i.IsDefault))
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

        var grouped = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => ids.Contains(v.ProductId))
                .GroupBy(v => v.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() }));
        return grouped.ToDictionary(x => x.ProductId, x => x.Count);
    }

    private async Task<ProductGetDto> ToGetDtoAsync(Product p)
    {
        // Company filtresi AÇIK kalır (mevcut desen): tüm alt kayıtlar üründen denormalize aynı şirkette,
        // çalışılan şirket de ürünü görünür kılan şirket → ek Disable gerekmez (varyant sorgusuyla simetrik).
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == p.Id).OrderBy(v => v.Code));

        var attributes = (await AsyncExecuter.ToListAsync(
                (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == p.Id)))
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();

        var attributeIds = attributes.Select(a => a.Id).ToList();
        var values = attributeIds.Count == 0
            ? new List<ProductAttributeValue>()
            : (await AsyncExecuter.ToListAsync(
                    (await _valueRepository.GetQueryableAsync()).Where(v => attributeIds.Contains(v.ProductAttributeId))))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value)
                .ToList();

        var variantIds = variants.Select(v => v.Id).ToList();
        var links = variantIds.Count == 0
            ? new List<ProductVariantAttributeValue>()
            : await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));

        // Reçete satırları (tüm varyantlar) — LineOrder sırasıyla.
        var recipeLines = variantIds.Count == 0
            ? new List<ProductVariantRecipeLine>()
            : (await AsyncExecuter.ToListAsync(
                    (await _recipeLineRepository.GetQueryableAsync()).Where(r => variantIds.Contains(r.ProductVariantId))))
                .OrderBy(r => r.LineOrder).ThenBy(r => r.CreationTime)
                .ToList();

        var variantDtos = variants.Select(v => new ProductVariantGraphDto
        {
            Id = v.Id,
            IsMain = v.IsMain,
            Code = v.Code,
            Name = v.Name,
            Description = v.Description,
            IsActive = v.IsActive,
            SalePrice = v.SalePrice,
            SalePriceCurrencyUnitId = v.SalePriceCurrencyUnitId,
            StockQuantity = v.StockQuantity,
            Barcode = v.Barcode,
            Gtin = v.Gtin,
            Mpn = v.Mpn,
            Oem = v.Oem,
            AttributeSummary = BuildAttributeSummary(v.Id, attributes, values, links),
            RecipeLines = recipeLines
                .Where(r => r.ProductVariantId == v.Id)
                .Select(r => new ProductRecipeLineGraphDto
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
                }).ToList(),
        }).ToList();

        // Türev SelectedLines kaynak Id'lerini bu oturumun taze ClientKey'lerine çevir (UI round-trip) — CANLI hesaptan ÖNCE.
        ResolveDerivedSourceKeys(variantDtos, recipeLines);

        // CANLI net maliyet — değerleme dict'i ÜRÜN başına BİR KEZ çekilir, tüm varyant/satırlarda yeniden kullanılır.
        await PopulateRecipeCostsAsync(variantDtos);

        var imageDtos = p.Images.Select(i => new ProductImageGraphDto
        {
            SourceType = i.SourceType,
            Url = i.Url,
            BlobName = i.BlobName,
            FileName = i.FileName,
            DisplayOrder = i.DisplayOrder,
            IsDefault = i.IsDefault,
        }).ToList();
        await PopulateImagePreviewsAsync(imageDtos);

        // N11 kanal ürünleri grafı — kanal AppService'inden (canlı kanal filtreli). Yeni üründe boş (Id yok → GetList
        // boş dönmez ama kayıt yoktur). ClientKey kaydedilmiş satırlarda round-trip için yeniden üretilir.
        var channelProducts = await _channelProductAppService.GetListForProductAsync(p.Id);

        // Trendyol kanal ürünleri grafı — N11'den AYRI ikinci liste (çift-kanal; kanal AppService'inden canlı yüklenir).
        var trendyolChannelProducts = await _trendyolChannelProductAppService.GetListForProductAsync(p.Id);

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
            ShipmentTemplateName = p.ShipmentTemplateName,
            MaxPurchaseQuantity = p.MaxPurchaseQuantity,
            SellerNote = p.SellerNote,
            CurrencyUnitId = p.CurrencyUnitId,
            SpecialInfo = p.SpecialInfo
                .Select(s => new ProductSpecialInfoDto { Key = s.Key, Value = s.Value })
                .ToList(),
            SalesChannelProducts = channelProducts,
            SalesChannelTrendyolProducts = trendyolChannelProducts,
            Attributes = attributes.Select(a => new ProductAttributeGraphDto
            {
                Id = a.Id,
                Name = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = values.Where(v => v.ProductAttributeId == a.Id)
                    .Select(v => new ProductAttributeValueGraphDto
                    {
                        Id = v.Id,
                        Value = v.Value,
                        DisplayOrder = v.DisplayOrder,
                    }).ToList(),
            }).ToList(),
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

    /// <summary>Varyantın kombinasyon özeti — bağlı "Nitelik: Değer" çiftleri, attribute DisplayOrder
    /// (synchronizer ile aynı: DisplayOrder→Name) sırasıyla ", " join (ör. "Renk: Kırmızı, Beden: M"). Salt görüntü.</summary>
    private static string BuildAttributeSummary(
        Guid variantId,
        List<ProductAttribute> attributes,
        List<ProductAttributeValue> values,
        List<ProductVariantAttributeValue> links)
    {
        var valueById = values.ToDictionary(v => v.Id);
        var attributeById = attributes.ToDictionary(a => a.Id);
        var attributeOrder = attributes
            .Select((a, index) => (a.Id, Index: index))
            .ToDictionary(x => x.Id, x => x.Index);

        var parts = links
            .Where(l => l.ProductVariantId == variantId
                && valueById.ContainsKey(l.ProductAttributeValueId)
                && attributeById.ContainsKey(l.ProductAttributeId))
            .OrderBy(l => attributeOrder.GetValueOrDefault(l.ProductAttributeId, int.MaxValue))
            .Select(l => $"{attributeById[l.ProductAttributeId].Name}: {valueById[l.ProductAttributeValueId].Value}");

        return string.Join(", ", parts);
    }
}
