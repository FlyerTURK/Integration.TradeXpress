using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
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
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ITrendyolProductClient _client;
    private readonly IPublicImageLinkProvider _publicImageLink;

    public SalesChannelTrTrendyolProductAppService(
        IRepository<SalesChannelTrTrendyolProduct, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        ICurrentCompany currentCompany,
        ITrendyolProductClient client,
        IPublicImageLinkProvider publicImageLink)
    {
        _repository = repository;
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _channelRepository = channelRepository;
        _currentCompany = currentCompany;
        _client = client;
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
        return items.Select(x => ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(x)).ToList();
    }

    public virtual async Task<SalesChannelTrTrendyolProductDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        return ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
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

        return ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
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

        return ObjectMapper.Map<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
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

    // ── Push veri kurulumu (ürün grafı → TrendyolProductData) ─────────────────────────────────────────

    private async Task<TrendyolProductData> BuildProductDataAsync(SalesChannelTrTrendyolProduct channelProduct)
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
            throw new BusinessException("TradeXpress:Trendyol:Product:ImagesRequired");
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
            throw new BusinessException("TradeXpress:Trendyol:Product:NoPricedVariant");
        }

        // Trendyol yalnız TRY (V2 create'de currencyType yok) → tek para birimi zorunlu; TRY-dışı karışım fail-fast.
        var currencyUnitIds = variants.Select(v => v.SalePriceCurrencyUnitId).Where(x => x is not null).Distinct().ToList();
        if (currencyUnitIds.Count > 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:MixedCurrency");
        }

        // Barcode DONDURMA planı (mutasyonsuz — push başarısızsa DB'ye bayat barcode donmaz; kalıcılaştırma
        // yalnız başarılı batch sonrası ReconcileSkus ile). Varianter attribute imzası kategori-def bağımlı (T6/T8'de
        // dolar) → skeleton'da boş; barcode eşlemesi VariantId + dondurulmuş-kod aşamalarına dayanır.
        var candidates = variants
            .Select(v => new TrendyolSkuPushCandidate(v.Id, v.Code, Array.Empty<SalesChannelTrTrendyolProductSkuAttribute>()))
            .ToList();
        var plannedBarcodes = channelProduct.PlanBarcodes(candidates);

        var items = variants.Select(v => new TrendyolProductItem(
            Barcode: plannedBarcodes[v.Id],
            StockCode: v.Code,
            Quantity: v.StockQuantity,
            ListPrice: v.SalePrice!.Value,
            SalePrice: v.SalePrice!.Value)).ToList();

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
