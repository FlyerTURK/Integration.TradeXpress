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

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol ürün listeleme CRUD + push — <b>company-owned + per-tenant</b>. Yapılandırma (kategori/marka/KDV/kargo/
/// attribute) bizde tutulur; <see cref="ListToTrendyolAsync"/> ürünü + varyantlarını (items) Trendyol'a ASENKRON
/// gönderir (batch id döner), <see cref="RefreshStatusAsync"/> durumu çeker. Push kanalın KENDİ kimliğiyle yapılır.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class TrendyolProductListingAppService : TradeXpressAppService, ITrendyolProductListingAppService
{
    private readonly IRepository<TrendyolProductListing, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ITrendyolProductClient _client;

    public TrendyolProductListingAppService(
        IRepository<TrendyolProductListing, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        IRepository<CurrencyUnit, Guid> currencyRepository,
        ICurrentCompany currentCompany,
        ITrendyolProductClient client)
    {
        _repository = repository;
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _channelRepository = channelRepository;
        _currencyRepository = currencyRepository;
        _currentCompany = currentCompany;
        _client = client;
    }

    public virtual async Task<TrendyolProductListingDto?> GetForProductAsync(Guid productId, Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.ProductId == productId && x.SalesChannelId == salesChannelId));
        return entity is null ? null : ObjectMapper.Map<TrendyolProductListing, TrendyolProductListingDto>(entity);
    }

    public virtual async Task<TrendyolProductListingDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        return ObjectMapper.Map<TrendyolProductListing, TrendyolProductListingDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<TrendyolProductListingDto> CreateAsync(TrendyolProductListingCreateDto input)
    {
        var channel = await GetOwnedChannelAsync(input.SalesChannelId);
        await EnsureProductOwnedAsync(input.ProductId);
        await EnsureNotListedAsync(channel.Id, input.ProductId);

        var entity = new TrendyolProductListing(
            channel.CompanyId,
            channel.Id,
            input.ProductId,
            input.CategoryId,
            input.BrandId);
        ApplyInput(entity, input);
        await _repository.InsertAsync(entity, autoSave: true);

        return ObjectMapper.Map<TrendyolProductListing, TrendyolProductListingDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<TrendyolProductListingDto> UpdateAsync(Guid id, TrendyolProductListingUpdateDto input)
    {
        var entity = await GetOwnedAsync(id);
        ApplyInput(entity, input);
        await _repository.UpdateAsync(entity, autoSave: true);

        return ObjectMapper.Map<TrendyolProductListing, TrendyolProductListingDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<TrendyolProductListingDto> ListToTrendyolAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        var data = await BuildProductDataAsync(entity);

        try
        {
            var result = await _client.SubmitProductAsync(data, CredentialsOf(channel));
            entity.MarkSubmitted(result.BatchRequestId, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // Hatayı kaydet (kullanıcı görsün) + yeniden fırlat (toast). Gizleme YOK — kayıt + propagate.
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        return ObjectMapper.Map<TrendyolProductListing, TrendyolProductListingDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<TrendyolProductListingDto> RefreshStatusAsync(Guid id)
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
            entity.MarkStatus(status.Status, error, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        return ObjectMapper.Map<TrendyolProductListing, TrendyolProductListingDto>(entity);
    }

    // ── Push veri kurulumu (ürün grafı → TrendyolProductData) ─────────────────────────────────────────

    private async Task<TrendyolProductData> BuildProductDataAsync(TrendyolProductListing listing)
    {
        var product = await GetOwnedProductAsync(listing.ProductId);

        if (product.ImageUrls.Count == 0)
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

        // Tek para birimi zorunlu (Trendyol item currencyType ürün genelinde tutarlı olmalı).
        var currencyUnitIds = variants.Select(v => v.SalePriceCurrencyUnitId).Where(x => x is not null).Distinct().ToList();
        if (currencyUnitIds.Count > 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:MixedCurrency");
        }

        var currencyType = await ResolveCurrencyTypeAsync(currencyUnitIds.FirstOrDefault());

        var items = variants.Select(v => new TrendyolProductItem(
            Barcode: v.Code,
            StockCode: v.Code,
            Quantity: v.StockQuantity,
            ListPrice: v.SalePrice!.Value,
            SalePrice: v.SalePrice!.Value,
            CurrencyType: currencyType)).ToList();

        return new TrendyolProductData(
            ProductMainId: product.Code,
            Title: product.Name,
            Description: product.Description ?? product.Name,
            CategoryId: listing.CategoryId,
            BrandId: listing.BrandId,
            VatRate: listing.VatRate,
            CargoCompanyId: listing.CargoCompanyId,
            DimensionalWeight: listing.DimensionalWeight,
            ImageUrls: product.ImageUrls.ToList(),
            Attributes: listing.Attributes
                .Select(a => new TrendyolAttributeValue(a.AttributeId, a.AttributeValueId, a.CustomValue))
                .ToList(),
            Items: items);
    }

    /// <summary>CurrencyUnit kodu → Trendyol currencyType (varsayılan "TRY"; USD/EUR aynen; TL→TRY).</summary>
    private async Task<string> ResolveCurrencyTypeAsync(Guid? currencyUnitId)
    {
        if (currencyUnitId is not { } id)
        {
            return "TRY";
        }

        var unit = await _currencyRepository.FindAsync(id);
        return (unit?.Code.Trim().ToUpperInvariant()) switch
        {
            "USD" => "USD",
            "EUR" => "EUR",
            _ => "TRY",
        };
    }

    // ── Uygulama + güvenlik ─────────────────────────────────────────────────────────────────────────

    private static TrendyolCredentials CredentialsOf(SalesChannelTrTrendyol channel)
    {
        return new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
    }

    private void ApplyInput(TrendyolProductListing entity, ITrendyolProductListingInput input)
    {
        entity.SetCategory(input.CategoryId, input.CategoryName);
        entity.SetBrand(input.BrandId);
        entity.SetVatRate(input.VatRate);
        entity.SetCargoCompany(input.CargoCompanyId);
        entity.SetDimensionalWeight(input.DimensionalWeight);
        entity.SetActive(input.IsActive);
        entity.SetAttributes(input.Attributes.Select(a => new TrendyolListingAttribute(a.AttributeId, a.AttributeValueId, a.CustomValue)));
    }

    private async Task<TrendyolProductListing> GetOwnedAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.Id == id && x.CompanyId == companyId));
        if (entity is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ListingNotFound");
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
            throw new BusinessException("TradeXpress:Trendyol:Product:AlreadyListed");
        }
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
