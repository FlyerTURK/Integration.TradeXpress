using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 ürün listeleme CRUD + push — <b>company-owned + per-tenant</b>. Listeleme yapılandırması (kategori/attribute/
/// kargo şablonu/condition/özel bilgi) bizde tutulur; <see cref="ListToN11Async"/> ürünü + varyantlarını (stockItems)
/// + fiyat/stok/görselleriyle N11'e SaveProduct ile gönderir (kanalın KENDİ kimliğiyle) ve durumu işaretler.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class N11ProductListingAppService : TradeXpressAppService, IN11ProductListingAppService
{
    private readonly IRepository<N11ProductListing, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _attributeValueRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _variantAttributeRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IN11ProductClient _client;

    public N11ProductListingAppService(
        IRepository<N11ProductListing, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<ProductAttribute, Guid> attributeRepository,
        IRepository<ProductAttributeValue, Guid> attributeValueRepository,
        IRepository<ProductVariantAttributeValue, Guid> variantAttributeRepository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IRepository<CurrencyUnit, Guid> currencyRepository,
        ICurrentCompany currentCompany,
        IN11ProductClient client)
    {
        _repository = repository;
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _attributeRepository = attributeRepository;
        _attributeValueRepository = attributeValueRepository;
        _variantAttributeRepository = variantAttributeRepository;
        _channelRepository = channelRepository;
        _currencyRepository = currencyRepository;
        _currentCompany = currentCompany;
        _client = client;
    }

    public virtual async Task<N11ProductListingDto?> GetForProductAsync(Guid productId, Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.ProductId == productId && x.SalesChannelId == salesChannelId));
        return entity is null ? null : ObjectMapper.Map<N11ProductListing, N11ProductListingDto>(entity);
    }

    public virtual async Task<N11ProductListingDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        return ObjectMapper.Map<N11ProductListing, N11ProductListingDto>(entity);
    }

    public virtual async Task<List<N11ProductListingDto>> GetListForChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId)
                .OrderBy(x => x.CategoryName));
        return items.Select(x => ObjectMapper.Map<N11ProductListing, N11ProductListingDto>(x)).ToList();
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<N11ProductListingDto> CreateAsync(N11ProductListingCreateDto input)
    {
        var channel = await GetOwnedChannelAsync(input.SalesChannelId);
        await EnsureProductOwnedAsync(input.ProductId);
        await EnsureNotListedAsync(channel.Id, input.ProductId);

        var entity = new N11ProductListing(
            channel.CompanyId,
            channel.Id,
            input.ProductId,
            input.CategoryExternalId,
            input.ShipmentTemplateName,
            input.Condition);
        ApplyInput(entity, input);
        await _repository.InsertAsync(entity, autoSave: true);

        return ObjectMapper.Map<N11ProductListing, N11ProductListingDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<N11ProductListingDto> UpdateAsync(Guid id, N11ProductListingUpdateDto input)
    {
        var entity = await GetOwnedAsync(id);
        ApplyInput(entity, input);
        await _repository.UpdateAsync(entity, autoSave: true);

        return ObjectMapper.Map<N11ProductListing, N11ProductListingDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<N11ProductListingDto> ListToN11Async(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        var data = await BuildProductDataAsync(entity);

        try
        {
            var result = await _client.SaveProductAsync(data, channel.AppKey, channel.AppSecret);
            entity.MarkSynced(result.N11ProductId, result.SaleStatus, result.ApprovalStatus, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // Hatayı kaydet (kullanıcı görsün) + yeniden fırlat (toast). Gizleme YOK — kayıt + propagate.
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        return ObjectMapper.Map<N11ProductListing, N11ProductListingDto>(entity);
    }

    // ── Push veri kurulumu (ürün grafı → N11ProductData) ────────────────────────────────────────────

    private async Task<N11ProductData> BuildProductDataAsync(N11ProductListing listing)
    {
        var product = await GetOwnedProductAsync(listing.ProductId);

        if (product.ImageUrls.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:Product:ImagesRequired");
        }

        // Aktif + fiyatlı varyantlar = N11 stockItems. En az 1 zorunlu.
        var variants = (await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.ProductId == product.Id && v.IsActive)))
            .Where(v => v.SalePrice is not null)
            .OrderByDescending(v => v.IsMain)
            .ToList();
        if (variants.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:Product:NoPricedVariant");
        }

        // Tek para birimi zorunlu (N11 ürün başına tek currencyType).
        var currencyUnitIds = variants.Select(v => v.SalePriceCurrencyUnitId).Where(x => x is not null).Distinct().ToList();
        if (currencyUnitIds.Count > 1)
        {
            throw new BusinessException("TradeXpress:N11:Product:MixedCurrency");
        }

        var currencyType = await ResolveCurrencyTypeAsync(currencyUnitIds.FirstOrDefault());
        var variantOptions = await LoadVariantOptionsAsync(product.Id, variants.Select(v => v.Id).ToList());

        var stockItems = variants.Select(v => new N11ProductStockItem(
            SellerStockCode: v.Code,
            Quantity: v.StockQuantity,
            OptionPrice: v.SalePrice,
            Attributes: variantOptions.TryGetValue(v.Id, out var opts) ? opts : new List<N11ProductAttributePair>(),
            Gtin: null,
            Mpn: null)).ToList();

        var images = product.ImageUrls
            .Select((url, index) => new N11ProductImage(url, index + 1))
            .ToList();

        return new N11ProductData(
            ProductSellerCode: product.Code,
            Title: product.Name,
            Description: product.Description ?? product.Name,
            Domestic: listing.Domestic,
            CategoryId: listing.CategoryExternalId,
            Price: variants[0].SalePrice!.Value,          // ana/ilk fiyatlı varyant = base fiyat
            CurrencyType: currencyType,
            ProductCondition: (byte)listing.Condition,
            PreparingDay: listing.PreparingDay,
            ShipmentTemplate: listing.ShipmentTemplateName,
            MaxPurchaseQuantity: listing.MaxPurchaseQuantity,
            Images: images,
            Attributes: listing.Attributes.Select(a => new N11ProductAttributePair(a.Name, a.Value)).ToList(),
            StockItems: stockItems,
            SpecialInfo: listing.SpecialInfo.Select(s => new N11ProductSpecialInfo(s.Key, s.Value)).ToList());
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
                (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == productId)))
            .ToDictionary(a => a.Id, a => a.Name);
        if (attributeNames.Count == 0)
        {
            return result;   // niteliksiz ürün (tek varyant) → option attribute yok
        }

        var valueTexts = (await AsyncExecuter.ToListAsync(
                (await _attributeValueRepository.GetQueryableAsync())
                    .Where(v => attributeNames.Keys.Contains(v.ProductAttributeId))))
            .ToDictionary(v => v.Id, v => v.Value);

        var links = await AsyncExecuter.ToListAsync(
            (await _variantAttributeRepository.GetQueryableAsync())
                .Where(l => variantIds.Contains(l.ProductVariantId)));

        foreach (var link in links)
        {
            if (!attributeNames.TryGetValue(link.ProductAttributeId, out var name) ||
                !valueTexts.TryGetValue(link.ProductAttributeValueId, out var value))
            {
                continue;
            }

            if (!result.TryGetValue(link.ProductVariantId, out var list))
            {
                list = new List<N11ProductAttributePair>();
                result[link.ProductVariantId] = list;
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

    // ── Uygulama + güvenlik ─────────────────────────────────────────────────────────────────────────

    private void ApplyInput(N11ProductListing entity, IN11ProductListingInput input)
    {
        entity.SetCategory(input.CategoryExternalId, input.CategoryName);
        entity.SetCondition(input.Condition);
        entity.SetShipmentTemplate(input.ShipmentTemplateName);
        entity.SetDomestic(input.Domestic);
        entity.SetPreparingDay(input.PreparingDay);
        entity.SetMaxPurchaseQuantity(input.MaxPurchaseQuantity);
        entity.SetActive(input.IsActive);
        entity.SetAttributes(input.Attributes.Select(a => new N11ListingAttribute(a.Name, a.Value)));
        entity.SetSpecialInfo(input.SpecialInfo.Select(s => new N11ListingSpecialInfo(s.Key, s.Value)));
    }

    private async Task<N11ProductListing> GetOwnedAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.Id == id && x.CompanyId == companyId));
        if (entity is null)
        {
            throw new BusinessException("TradeXpress:N11:Product:ListingNotFound");
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

    private async Task EnsureNotListedAsync(Guid salesChannelId, Guid productId)
    {
        var exists = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.SalesChannelId == salesChannelId && x.ProductId == productId));
        if (exists)
        {
            throw new BusinessException("TradeXpress:N11:Product:AlreadyListed");
        }
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
