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
/// kargo şablonu/condition/özel bilgi) bizde tutulur; <see cref="PushToN11Async"/> ürünü + varyantlarını (stockItems)
/// + fiyat/stok/görselleriyle N11'e SaveProduct ile gönderir (kanalın KENDİ kimliğiyle) ve durumu işaretler.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelTrN11ProductAppService : TradeXpressAppService, ISalesChannelTrN11ProductAppService
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _attributeValueRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _variantAttributeRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IN11ProductClient _client;
    private readonly IPublicImageLinkProvider _publicImageLink;

    public SalesChannelTrN11ProductAppService(
        IRepository<SalesChannelTrN11Product, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<ProductAttribute, Guid> attributeRepository,
        IRepository<ProductAttributeValue, Guid> attributeValueRepository,
        IRepository<ProductVariantAttributeValue, Guid> variantAttributeRepository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IRepository<CurrencyUnit, Guid> currencyRepository,
        ICurrentCompany currentCompany,
        IN11ProductClient client,
        IPublicImageLinkProvider publicImageLink)
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
        _publicImageLink = publicImageLink;
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
        return items.Select(x => ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(x)).ToList();
    }

    public virtual async Task<SalesChannelTrN11ProductDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        return ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
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

        return ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
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

        return ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
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
            var data = await BuildProductDataAsync(entity);
            var result = await _client.SaveProductAsync(data, channel.AppKey, channel.AppSecret);

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
                catch
                {
                    // Push BAŞARILI; doğrulama okuması düştü → geri alınamaz, yalnız uyar (eşitleme bir sonraki push'ta).
                    syncWarnings.Add(L["N11Product:PullFailed"]);
                }
            }

            entity.MarkSynced(result.N11ProductId, saleStatus, approvalStatus, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
        }
        catch (Exception ex)
        {
            // Hatayı kaydet (kullanıcı görsün) + yeniden fırlat (toast). Gizleme YOK — kayıt + propagate.
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        dto.SyncWarnings = syncWarnings;
        return dto;
    }

    /// <summary>N11'in döndürdüğü ürün gerçeğini yerel kayda uygular — <b>SpecialInfo HARİÇ</b> (2026-07-07 kararı:
    /// N11 kuralları kendi tarafında oynatır; yerel kayıt yayın kopyasıdır). Yanıtta OLMAYAN alana dokunulmaz
    /// (N11'in desteklemediği alan yerel değeri silmesin). Kategori değişimi KRİTİK → kullanıcı uyarısı.</summary>
    private void ApplyN11Truth(SalesChannelTrN11Product entity, N11ProductDetail detail, List<string> syncWarnings)
    {
        if (detail.CategoryId is { Length: > 0 } categoryId)
        {
            if (!string.Equals(categoryId, entity.CategoryExternalId, StringComparison.Ordinal))
            {
                // KRİTİK: N11 ürünü farklı kategoriye/gruba taşıdı — güvenli bilgilendirme (eski → yeni).
                syncWarnings.Add(L[
                    "N11Product:CategoryChangedByN11",
                    entity.CategoryName ?? entity.CategoryExternalId,
                    detail.CategoryName ?? categoryId]);
            }

            entity.SetCategory(categoryId, detail.CategoryName);
        }

        if (detail.ShipmentTemplate is { Length: > 0 } shipmentTemplate)
        {
            entity.SetShipmentTemplate(shipmentTemplate);
        }

        if (detail.ProductCondition is 1 or 2)
        {
            entity.SetCondition((N11ProductCondition)detail.ProductCondition.Value);
        }

        if (detail.PreparingDay is >= 1)
        {
            entity.SetPreparingDay(detail.PreparingDay.Value);
        }

        if (detail.MaxPurchaseQuantity is >= 1)
        {
            entity.SetMaxPurchaseQuantity(detail.MaxPurchaseQuantity.Value);
        }

        if (detail.Attributes is not null)
        {
            entity.SetAttributes(detail.Attributes.Select(a => new SalesChannelTrN11ProductAttribute(a.Name, a.Value)));
        }
    }

    // ── Push veri kurulumu (ürün grafı → N11ProductData) ────────────────────────────────────────────

    private async Task<N11ProductData> BuildProductDataAsync(SalesChannelTrN11Product channelProduct)
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

        // Stok kodları da KAYIT-scoped ("{VaryantKodu}-{SequenceNo}") — aynı ürünün ikinci N11 listelemesinde
        // satıcı-geneli sellerStockCode çakışmasın (N11 stok kodu satıcı genelinde benzersizdir).
        var stockItems = variants.Select(v => new N11ProductStockItem(
            SellerStockCode: $"{v.Code}-{channelProduct.SequenceNo}",
            Quantity: v.StockQuantity,
            OptionPrice: v.SalePrice,
            Attributes: variantOptions.TryGetValue(v.Id, out var opts) ? opts : new List<N11ProductAttributePair>(),
            Gtin: null,
            Mpn: null)).ToList();

        var images = imageUrls
            .Select((url, index) => new N11ProductImage(url, index + 1))
            .ToList();

        return new N11ProductData(
            ProductSellerCode: channelProduct.SellerCode,   // KAYIT-bazlı upsert kimliği — her kayıt N11'de AYRI listeleme
            Title: product.Name,
            Description: product.Description ?? product.Name,
            Domestic: channelProduct.Domestic,
            CategoryId: channelProduct.CategoryExternalId,
            Price: variants[0].SalePrice!.Value,          // ana/ilk fiyatlı varyant = base fiyat
            CurrencyType: currencyType,
            ProductCondition: (byte)channelProduct.Condition,
            PreparingDay: channelProduct.PreparingDay,
            ShipmentTemplate: channelProduct.ShipmentTemplateName,
            MaxPurchaseQuantity: channelProduct.MaxPurchaseQuantity,
            Images: images,
            Attributes: channelProduct.Attributes.Select(a => new N11ProductAttributePair(a.Name, a.Value)).ToList(),
            StockItems: stockItems,
            SpecialInfo: channelProduct.SpecialInfo.Select(s => new N11ProductSpecialInfo(s.Key, s.Value)).ToList());
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

    private void ApplyInput(SalesChannelTrN11Product entity, ISalesChannelTrN11ProductInput input)
    {
        entity.SetCategory(input.CategoryExternalId, input.CategoryName);
        entity.SetCondition(input.Condition);
        entity.SetShipmentTemplate(input.ShipmentTemplateName);
        entity.SetDomestic(input.Domestic);
        entity.SetPreparingDay(input.PreparingDay);
        entity.SetMaxPurchaseQuantity(input.MaxPurchaseQuantity);
        entity.SetActive(input.IsActive);
        entity.SetAttributes(input.Attributes.Select(a => new SalesChannelTrN11ProductAttribute(a.Name, a.Value)));
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
