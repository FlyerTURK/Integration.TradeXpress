using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol ürün listeleme CRUD + push — <b>company-owned + per-tenant</b>. Yapılandırma (kategori/marka/KDV/kargo/
/// attribute) bizde tutulur; <see cref="PushToTrendyolAsync"/> ürünü + varyantlarını (items) Trendyol'a ASENKRON
/// gönderir (batch id döner), <see cref="RefreshStatusAsync"/> durumu çeker. Push kanalın KENDİ kimliğiyle yapılır.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelTrTrendyolProductAppService : TradeXpressAppService, ISalesChannelTrTrendyolProductAppService
{
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _attributeValueRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _variantAttributeRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductVariant, Guid> _variantOverrideRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductVariantRecipeLine, Guid> _channelRecipeLineRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _erpRecipeLineRepository;
    private readonly RecipeCostPopulator _recipeCostPopulator;
    private readonly ICurrentCompany _currentCompany;
    private readonly ITrendyolProductClient _client;
    private readonly ITrendyolCategoryAppService _categoryAppService;
    private readonly IPublicImageLinkProvider _publicImageLink;

    public SalesChannelTrTrendyolProductAppService(
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<ProductAttribute, Guid> attributeRepository,
        IRepository<ProductAttributeValue, Guid> attributeValueRepository,
        IRepository<ProductVariantAttributeValue, Guid> variantAttributeRepository,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        IRepository<SalesChannelTrTrendyolProductVariant, Guid> variantOverrideRepository,
        IRepository<SalesChannelTrTrendyolProductVariantRecipeLine, Guid> channelRecipeLineRepository,
        IRepository<ProductVariantRecipeLine, Guid> erpRecipeLineRepository,
        RecipeCostPopulator recipeCostPopulator,
        ICurrentCompany currentCompany,
        ITrendyolProductClient client,
        ITrendyolCategoryAppService categoryAppService,
        IPublicImageLinkProvider publicImageLink)
    {
        _repository = repository;
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _attributeRepository = attributeRepository;
        _attributeValueRepository = attributeValueRepository;
        _variantAttributeRepository = variantAttributeRepository;
        _channelRepository = channelRepository;
        _variantOverrideRepository = variantOverrideRepository;
        _channelRecipeLineRepository = channelRecipeLineRepository;
        _erpRecipeLineRepository = erpRecipeLineRepository;
        _recipeCostPopulator = recipeCostPopulator;
        _currentCompany = currentCompany;
        _client = client;
        _categoryAppService = categoryAppService;
        _publicImageLink = publicImageLink;
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
            dto.Variants = await BuildVariantGraphAsync(item);
            dtos.Add(dto);
        }

        return dtos;
    }

    public virtual async Task<SalesChannelTrTrendyolProductDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        dto.Variants = await BuildVariantGraphAsync(entity);
        return dto;
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
        await SaveVariantOverridesAsync(entity, input.Variants);

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        dto.Variants = await BuildVariantGraphAsync(entity);
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

    /// <summary>Trendyol varyant grup anahtarı: "{ÜrünKodu}-{Sıra}" — kayıt-bazlı benzersiz + insan-okunur (frozen).</summary>
    private static string BuildProductMainId(string productCode, int sequenceNo)
    {
        return $"{productCode}-{sequenceNo}";
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> UpdateAsync(Guid id, SalesChannelTrTrendyolProductUpdateDto input)
    {
        var entity = await GetOwnedAsync(id);
        ApplyInput(entity, input);
        await _repository.UpdateAsync(entity, autoSave: true);
        await SaveVariantOverridesAsync(entity, input.Variants);

        var dto = ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
        dto.Variants = await BuildVariantGraphAsync(entity);
        return dto;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        // Kanal-özel varyant override başlıkları + reçete satırları (ayrı tablolar) — kanal-ürünle birlikte temizlenir.
        await _channelRecipeLineRepository.DeleteAsync(r => r.SalesChannelTrTrendyolProductId == entity.Id, autoSave: true);
        await _variantOverrideRepository.DeleteAsync(v => v.SalesChannelTrTrendyolProductId == entity.Id, autoSave: true);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolProductDto> PushToTrendyolAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);

        try
        {
            // Veri kurulumu da try İÇİNDE — geçici-link hataları dahil MarkSyncFailed'e düşsün (N11 ile aynı).
            var data = await BuildProductDataAsync(entity);
            var result = await _client.SubmitProductAsync(data, CredentialsOf(channel));
            entity.MarkSubmitted(result.BatchRequestId, "ProductV2OnBoarding", Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // Hatayı kaydet (kullanıcı görsün) + yeniden fırlat (toast). Gizleme YOK — kayıt + propagate.
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        return ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
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
            entity.MarkStatus(status.Status, status.FailedCount, error, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
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
            Options = optionsByStockCode.TryGetValue(it.StockCode, out var opt) ? opt : string.Empty,
        }).ToList();

        return new TrendyolPushPreviewDto { Product = previewProduct, Items = items, Warnings = warnings };
    }

    /// <summary>Yaprak kategori attribute tanımlarını best-effort çeker (önizleme). Kategori boşsa ya da REST/kimlik
    /// hatası varsa boş liste döner — önizleme KIRILMAZ (ad çözümü id'ye düşer, zorunlu denetimi atlanır).</summary>
    private async Task<List<TrendyolLeafAttributeDto>> TryLoadLeafAttributesAsync(string categoryId)
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
    private static string BuildAttributeSummary(IReadOnlyCollection<SalesChannelTrTrendyolProductAttribute> attributes, List<TrendyolLeafAttributeDto> attrDefs)
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
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == productId));
        if (variants.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var attributeNames = (await AsyncExecuter.ToListAsync(
                (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == productId)))
            .ToDictionary(a => a.Id, a => a.Name);
        if (attributeNames.Count == 0)
        {
            return new Dictionary<string, string>();   // niteliksiz ürün (tek varyant) → eksen yok
        }

        var valueTexts = (await AsyncExecuter.ToListAsync(
                (await _attributeValueRepository.GetQueryableAsync())
                    .Where(v => attributeNames.Keys.Contains(v.ProductAttributeId))))
            .ToDictionary(v => v.Id, v => v.Value);

        var variantIds = variants.Select(v => v.Id).ToList();
        var links = await AsyncExecuter.ToListAsync(
            (await _variantAttributeRepository.GetQueryableAsync())
                .Where(l => variantIds.Contains(l.ProductVariantId)));

        var byVariant = new Dictionary<Guid, List<string>>();
        foreach (var link in links)
        {
            if (!attributeNames.TryGetValue(link.ProductAttributeId, out var name)
                || !valueTexts.TryGetValue(link.ProductAttributeValueId, out var value))
            {
                continue;
            }

            if (!byVariant.TryGetValue(link.ProductVariantId, out var list))
            {
                list = new List<string>();
                byVariant[link.ProductVariantId] = list;
            }

            list.Add($"{name}: {value}");
        }

        return variants
            .Where(v => byVariant.ContainsKey(v.Id))
            .ToDictionary(v => v.Code, v => string.Join("; ", byVariant[v.Id]));
    }

    // ── Push veri kurulumu (ürün grafı → TrendyolProductData) ─────────────────────────────────────────

    /// <summary>Ürün grafından push payload'unu kurar. <paramref name="warnings"/> verilirse (T6 ÖNİZLEME) fail-fast
    /// koşulları exception yerine uyarıya çevrilir + kurulum devam eder (kısmi önizleme); null ise (gerçek push T8)
    /// eskisi gibi BusinessException fırlatır. Her iki modda da Trendyol'a HİÇBİR ŞEY gönderilmez (submit çağıran üstte).</summary>
    private async Task<TrendyolProductData> BuildProductDataAsync(SalesChannelTrTrendyolProduct channelProduct, List<string>? warnings = null)
    {
        var product = await GetOwnedProductAsync(channelProduct.ProductId);

        // Görsel sırası: VARSAYILAN önce, sonra DisplayOrder. URL-kaynaklılar doğrudan; blob görseller
        // sağlayıcı yapılandırılmışsa geçici dış linke çevrilir (N11 ile aynı 2026-07-07 kararı).
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
            // Önizleme (warnings dolu) modunda fırlatma → uyarı; gerçek push'ta (warnings null) fail-fast.
            if (warnings is null)
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:ImagesRequired");
            }

            warnings.Add(L["TradeXpress:Trendyol:Product:ImagesRequired"].Value);
        }

        // Aktif + fiyatlı varyantlar = Trendyol items (barcode başına). En az 1 zorunlu.
        var variants = (await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.ProductId == product.Id && v.IsActive)))
            .Where(v => v.SalePrice is not null)
            .OrderByDescending(v => v.IsMain)
            .ToList();
        if (variants.Count == 0)
        {
            if (warnings is null)
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:NoPricedVariant");
            }

            warnings.Add(L["TradeXpress:Trendyol:Product:NoPricedVariant"].Value);
        }

        // Trendyol yalnız TRY (V2 create'de currencyType yok) → tek para birimi zorunlu; TRY-dışı karışım fail-fast.
        var currencyUnitIds = variants.Select(v => v.SalePriceCurrencyUnitId).Where(x => x is not null).Distinct().ToList();
        if (currencyUnitIds.Count > 1)
        {
            if (warnings is null)
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:MixedCurrency");
            }

            warnings.Add(L["TradeXpress:Trendyol:Product:MixedCurrency"].Value);
        }

        // Kanal-özel fiyat/stok override + türetilmiş fiyat (kaydedilmiş marj + reçete → NetCost×marj) — CANLI hesap.
        // Zincir: OverridePrice ?? türetilmiş ?? ERP SalePrice; stok: OverrideStock ?? ERP StockQuantity.
        // NOT (push T8): Trendyol listPrice/salePrice ayrımı (C.4) henüz yok — ikisi de efektif fiyata eşitlenir (iskelet).
        var pushPricing = await ResolveVariantPushPricingAsync(channelProduct, variants);

        // Barcode DONDURMA planı (mutasyonsuz — push başarısızsa DB'ye bayat barcode donmaz; kalıcılaştırma
        // yalnız başarılı batch sonrası ReconcileSkus ile). Varianter attribute imzası kategori-def bağımlı (T6/T8'de
        // dolar) → skeleton'da boş; barcode eşlemesi VariantId + dondurulmuş-kod aşamalarına dayanır.
        var candidates = variants
            .Select(v => new TrendyolSkuPushCandidate(v.Id, v.Code, Array.Empty<SalesChannelTrTrendyolProductSkuAttribute>()))
            .ToList();
        var plannedBarcodes = channelProduct.PlanBarcodes(candidates);

        var items = variants.Select(v =>
        {
            var pricing = pushPricing[v.Id];   // OverridePrice ?? türetilmiş ?? ERP SalePrice; stok OverrideStock ?? ERP
            return new TrendyolProductItem(
                Barcode: plannedBarcodes[v.Id],
                StockCode: v.Code,
                Quantity: pricing.Stock,
                ListPrice: pricing.Price!.Value,
                SalePrice: pricing.Price!.Value);
        }).ToList();

        return new TrendyolProductData(
            ProductMainId: channelProduct.ProductMainId,
            Title: product.Name,
            Description: channelProduct.Description ?? product.Description ?? product.Name,
            CategoryId: channelProduct.CategoryId,
            BrandId: channelProduct.BrandId,
            VatRate: channelProduct.VatRate,
            DimensionalWeight: channelProduct.DimensionalWeight,
            DeliveryDuration: channelProduct.DeliveryDuration,
            FastDeliveryType: channelProduct.FastDeliveryType,
            ImageUrls: imageUrls,
            Attributes: channelProduct.Attributes
                .Select(a => new TrendyolAttributeValue(a.AttributeId, a.AttributeValueId, a.CustomValue))
                .ToList(),
            Items: items);
    }

    // ── Kanal-özel varyant override (fiyat/stok/marj + reçete) ──────────────────────────────────────
    // Graf = ERP varyant seti (aktif) ⋈ kaydedilmiş kanal override (LEFT JOIN). Kaydedilmiş reçete varsa ondan,
    // yoksa ERP reçetesi KLONLANIR. NetCost + türetilmiş fiyat CANLI hesaplanır (ProductAppService ile ORTAK motor).

    /// <summary>Bir kanal-ürünün varyant override grafını kurar: aktif ERP varyantları × kaydedilmiş override başlığı
    /// (fiyat/stok/marj) + reçete (kaydedilmişse ondan, yoksa ERP reçetesinden klon). NetCost + türetilmiş fiyat
    /// (NetCost×(1+Margin/100)) canlı hesaplanır. Varyant yoksa boş liste.</summary>
    private async Task<List<SalesChannelTrTrendyolProductVariantGraphDto>> BuildVariantGraphAsync(SalesChannelTrTrendyolProduct channelProduct)
    {
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.ProductId == channelProduct.ProductId && v.IsActive)
                .OrderByDescending(v => v.IsMain).ThenBy(v => v.Code));
        if (variants.Count == 0)
        {
            return new List<SalesChannelTrTrendyolProductVariantGraphDto>();
        }

        var variantIds = variants.Select(v => v.Id).ToList();

        var headers = (await AsyncExecuter.ToListAsync(
                (await _variantOverrideRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id)))
            .ToDictionary(h => h.ProductVariantId);

        var savedByVariant = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id)))
            .GroupBy(r => r.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ERP reçetesi — yalnız kaydedilmiş kanal reçetesi OLMAYAN varyantlarda klonlanır (LEFT JOIN eksiği ERP'den).
        var erpByVariant = (await AsyncExecuter.ToListAsync(
                (await _erpRecipeLineRepository.GetQueryableAsync())
                    .Where(r => variantIds.Contains(r.ProductVariantId))))
            .GroupBy(r => r.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var nodes = new List<SalesChannelTrTrendyolProductVariantGraphDto>(variants.Count);
        foreach (var v in variants)
        {
            var node = new SalesChannelTrTrendyolProductVariantGraphDto
            {
                ProductVariantId = v.Id,
                VariantCode = v.Code,
                VariantName = v.Name,
            };

            if (headers.TryGetValue(v.Id, out var header))
            {
                node.OverridePrice = header.OverridePrice;
                node.OverridePriceCurrencyUnitId = header.OverridePriceCurrencyUnitId;
                node.OverrideStock = header.OverrideStock;
                node.Margin = header.Margin;
            }

            node.RecipeLines = savedByVariant.TryGetValue(v.Id, out var saved)
                ? MapSavedRecipeLines(saved)
                : (erpByVariant.TryGetValue(v.Id, out var erp) ? CloneErpRecipeLines(erp) : new List<ProductRecipeLineGraphDto>());

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
                ? nc * (1m + (node.Margin ?? 0m) / 100m)
                : null;
        }

        return nodes;
    }

    /// <summary>Push için varyant-başı efektif fiyat/stok — zincir: OverridePrice ?? türetilmiş (KAYDEDİLMİŞ reçete
    /// NetCost × (1+Margin/100)) ?? ERP SalePrice; stok: OverrideStock ?? ERP StockQuantity. Push PERSIST edilmiş
    /// gerçeği kullanır (ERP klonu değil) — kaydedilmemiş reçete türetilmiş fiyat üretmez.</summary>
    private async Task<IReadOnlyDictionary<Guid, VariantPushPricing>> ResolveVariantPushPricingAsync(
        SalesChannelTrTrendyolProduct channelProduct, List<ProductVariant> variants)
    {
        var variantIds = variants.Select(v => v.Id).ToList();

        var headers = (await AsyncExecuter.ToListAsync(
                (await _variantOverrideRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id && variantIds.Contains(h.ProductVariantId))))
            .ToDictionary(h => h.ProductVariantId);

        var savedByVariant = (await AsyncExecuter.ToListAsync(
                (await _channelRecipeLineRepository.GetQueryableAsync())
                    .Where(r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && variantIds.Contains(r.ProductVariantId))))
            .GroupBy(r => r.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lineSets = variants
            .Select(v => savedByVariant.TryGetValue(v.Id, out var l) ? MapSavedRecipeLines(l) : new List<ProductRecipeLineGraphDto>())
            .ToList();
        var costs = await _recipeCostPopulator.PopulateAsync(lineSets);

        var result = new Dictionary<Guid, VariantPushPricing>(variants.Count);
        for (var i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            headers.TryGetValue(v.Id, out var header);
            decimal? derived = costs[i].NetCost is { } nc && !costs[i].NetCostMissingRate
                ? nc * (1m + (header?.Margin ?? 0m) / 100m)
                : null;
            var price = header?.OverridePrice ?? derived ?? v.SalePrice;
            var stock = header?.OverrideStock ?? v.StockQuantity;
            result[v.Id] = new VariantPushPricing(price, stock);
        }

        return result;
    }

    /// <summary>Kanal-özel varyant override grafını persist eder — override sinyali (OverridePrice/OverrideStock/Margin
    /// herhangi biri dolu) olan varyantın başlığı + reçetesi yazılır; TÜMÜ boşsa (saf ERP devralma) kaydedilmiş
    /// override/reçete TEMİZLENİR (ölü satır şişmesini önle). Türetilmiş fiyat/NetCost hesap alanları PERSIST EDİLMEZ.</summary>
    private async Task SaveVariantOverridesAsync(SalesChannelTrTrendyolProduct channelProduct, List<SalesChannelTrTrendyolProductVariantGraphDto> variants)
    {
        if (variants == null || variants.Count == 0)
        {
            return;
        }

        var existingHeaders = (await AsyncExecuter.ToListAsync(
                (await _variantOverrideRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == channelProduct.Id)))
            .ToDictionary(h => h.ProductVariantId);

        foreach (var node in variants)
        {
            if (node.ProductVariantId == Guid.Empty)
            {
                continue;   // anchor yok → atla (bayat/geçersiz düğüm)
            }

            // Persist sinyali: override alanı VEYA kanal-özel reçete girilmişse korunur (reçete-only + boş marj de
            // emek → silinmesin). Hepsi gerçekten boşsa (saf ERP devralma) kaydedilmiş override/reçete temizlenir.
            var hasRecipe = node.RecipeLines?.Any(l => !l.IsDeleted) == true;
            var hasOverride = node.OverridePrice is not null || node.OverrideStock is not null
                || node.Margin is not null || hasRecipe;
            existingHeaders.TryGetValue(node.ProductVariantId, out var header);

            if (!hasOverride)
            {
                // Saf devralma → kaydedilmiş override başlığı + reçete satırlarını sil (ERP'ye geri dön).
                if (header is not null)
                {
                    await _variantOverrideRepository.DeleteAsync(header, autoSave: true);
                }

                await _channelRecipeLineRepository.DeleteAsync(
                    r => r.SalesChannelTrTrendyolProductId == channelProduct.Id && r.ProductVariantId == node.ProductVariantId,
                    autoSave: true);
                continue;
            }

            if (header is null)
            {
                header = new SalesChannelTrTrendyolProductVariant(channelProduct.CompanyId, channelProduct.Id, node.ProductVariantId);
                header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
                header.SetOverrideStock(node.OverrideStock);
                header.SetMargin(node.Margin);
                await _variantOverrideRepository.InsertAsync(header, autoSave: true);
            }
            else
            {
                header.SetOverridePrice(node.OverridePrice, node.OverridePriceCurrencyUnitId);
                header.SetOverrideStock(node.OverrideStock);
                header.SetMargin(node.Margin);
                await _variantOverrideRepository.UpdateAsync(header, autoSave: true);
            }

            await SaveChannelRecipeLinesAsync(channelProduct, node.ProductVariantId, node.RecipeLines);
        }
    }

    /// <summary>Bir varyantın kanal-özel reçete satırlarını persist eder (ERP SaveRecipeLinesAsync deseni, iki-geçişli):
    /// silinenler → LineOrder 0..n yeniden-numara → referans doğrulama → skaler insert/update (1. geçiş) → türev
    /// SelectedLines kaynak Id CSV çözümü (2. geçiş). ComponentType set-once (ctor'da).</summary>
    private async Task SaveChannelRecipeLinesAsync(SalesChannelTrTrendyolProduct channelProduct, Guid variantId, List<ProductRecipeLineGraphDto> lines)
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
        var entityByClientKey = new Dictionary<Guid, SalesChannelTrTrendyolProductVariantRecipeLine>();
        foreach (var l in survivors)
        {
            SalesChannelTrTrendyolProductVariantRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new SalesChannelTrTrendyolProductVariantRecipeLine(
                    channelProduct.CompanyId, channelProduct.Id, variantId, l.ComponentType, l.LineOrder);
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
    private static void ApplyChannelRecipeLineFields(SalesChannelTrTrendyolProductVariantRecipeLine entity, ProductRecipeLineGraphDto l)
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
    }

    /// <summary>Kaydedilmiş kanal reçete satırlarını graf DTO'suna projekte eder (Id KORUNUR — mevcut satır) +
    /// türev SelectedLines kaynaklarını taze ClientKey'lere çözer (ORTAK resolver).</summary>
    private static List<ProductRecipeLineGraphDto> MapSavedRecipeLines(List<SalesChannelTrTrendyolProductVariantRecipeLine> saved)
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

    /// <summary>Push için varyant-başı efektif fiyat (override zinciri sonucu) + stok.</summary>
    private sealed record VariantPushPricing(decimal? Price, int Stock);

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
        entity.SetActive(input.IsActive);
        entity.SetAttributes(input.Attributes.Select(a => new SalesChannelTrTrendyolProductAttribute(a.AttributeId, a.AttributeValueId, a.CustomValue)));
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
        var companyId = EnsureCurrentCompanyId();
        var product = await AsyncExecuter.FirstOrDefaultAsync(
            (await _productRepository.GetQueryableAsync()).Where(x => x.Id == productId && x.CompanyId == companyId));
        if (product is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ProductNotFound");
        }

        return product;
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
