using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
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
    private readonly IN11CategoryClient _categoryClient;
    private readonly N11ProductPushValidator _pushValidator;
    private readonly IDistributedCache<N11LeafAttributes> _leafAttributeCache;
    private readonly IBlobContainer<ProductImagesContainer> _imageContainer;

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
        IPublicImageLinkProvider publicImageLink,
        IN11CategoryClient categoryClient,
        N11ProductPushValidator pushValidator,
        IDistributedCache<N11LeafAttributes> leafAttributeCache,
        IBlobContainer<ProductImagesContainer> imageContainer)
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
                    item.Attributes.Select(a => new SalesChannelTrN11ProductAttribute(a.Name, a.Value)));
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
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
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
            var variants = (await AsyncExecuter.ToListAsync(
                    (await _variantRepository.GetQueryableAsync())
                        .Where(v => v.ProductId == product.Id && v.IsActive)))
                .Where(v => v.SalePrice is not null)
                .OrderByDescending(v => v.IsMain)   // base fiyat ANA varyanttan — tam push ile hizalı
                .ToList();

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

            // Değişen varyantları (dirty) belirle: SKU satırı olan + N11 SKU id'si bilinen + adet/fiyatı sapmış.
            var stockItems = new List<N11ProductBasicStockItem>();
            var anyDirty = false;
            foreach (var variant in variants)
            {
                var sku = entity.Skus.FirstOrDefault(s => s.ProductVariantId == variant.Id);
                if (sku is null || sku.N11SkuId is not { } n11SkuId)
                {
                    // Bu varyant hiç push edilmemiş / SKU id'si yok → hafif senkron adresleyemez; tam push gerekir.
                    syncWarnings.Add(L["N11Product:SkuNotPushed", variant.Code]);
                    continue;
                }

                var dirty = sku.LastSentQuantity != variant.StockQuantity || sku.LastSentOptionPrice != variant.SalePrice;
                anyDirty |= dirty;

                // Merge/replace belirsizliğinden (rapor A3) kaçınmak için TÜM bilinen SKU'ları güncel değerleriyle
                // gönderiyoruz — gönderilmeyen SKU'nun N11'de sıfırlanma riski olmasın.
                stockItems.Add(new N11ProductBasicStockItem(
                    sku.SellerStockCode,
                    n11SkuId,
                    variant.StockQuantity,
                    variant.SalePrice));
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
                    variants[0].SalePrice,
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
            entity.MarkSyncFailed(ex.Message, Clock.Now.ToUniversalTime());
            await _repository.UpdateAsync(entity, autoSave: true);
            throw;
        }

        var dto = ObjectMapper.Map<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>(entity);
        dto.SyncWarnings = syncWarnings;
        return dto;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<N11PushPreviewDto> GetPushPreviewAsync(Guid id)
    {
        var entity = await GetOwnedAsync(id);
        var product = await GetOwnedProductAsync(entity.ProductId);

        // Push'ta gidecek varyant seti — SaveProduct ile AYNI filtre/sıra (aktif + fiyatlı + IsMain önce).
        var variants = (await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.ProductId == product.Id && v.IsActive)))
            .Where(v => v.SalePrice is not null)
            .OrderByDescending(v => v.IsMain)
            .ToList();

        var options = await LoadVariantOptionsAsync(product.Id, variants.Select(v => v.Id).ToList());
        var preview = new N11PushPreviewDto
        {
            Variants = variants.Select(v => new N11PreviewVariantDto
            {
                Code = v.Code,
                Name = v.Name,
                StockQuantity = v.StockQuantity,
                SalePrice = v.SalePrice,
                Options = options.TryGetValue(v.Id, out var pairs)
                    ? string.Join("; ", pairs.Select(p => $"{p.Name}: {p.Value}"))
                    : string.Empty,
            }).ToList(),
            Images = await BuildPreviewImagesAsync(product),
        };

        return preview;
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
            entity.SetAttributes(detail.Attributes.Select(a => new SalesChannelTrN11ProductAttribute(
                a.Name.Truncate(N11ProductConsts.AttributeNameMaxLength)!,
                a.Value.Truncate(N11ProductConsts.AttributeValueMaxLength) ?? string.Empty)));
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

        // ── Faz 1: kategori-farkındalıklı validasyon — varyant EKSENLERİNİ kategori belirler (isVariant seti),
        // customValue=false değer listeden birebir, zorunlu eksen her SKU'da dolu; sapma FAIL-FAST.
        var leaf = await GetLeafAttributesCachedAsync(channelProduct.CategoryExternalId, channel);

        // Adaylar: N11 SİHİRBAZI (VariantAxes) doluysa eksen kartezyeni → her kombinasyon isim/değer imzasıyla
        // ERP varyantına eşleşir (fiyat/stok/kod ORADAN); boşsa ERP varyantları doğrudan (mevcut davranış).
        var variantById = variants.ToDictionary(v => v.Id);
        var candidates = BuildPushCandidates(channelProduct, variants, variantOptions);
        var validated = _pushValidator.Validate(leaf, channelProduct.Attributes, candidates);

        // Reconcile/imza adayları KANONİK değerlerle kurulur (validated) — RecordSkuPush snapshot'ı da kanonik
        // olduğundan, sonraki push'ta imza eşleşmesi ham/kanonik karışımından ETKİLENMEZ (review bulgusu).
        var canonicalCandidates = candidates
            .Select(c => new N11SkuPushCandidate(
                c.VariantId,
                c.VariantCode,
                (validated.VariantOptions.TryGetValue(c.VariantId, out var cp) ? cp : new List<N11ProductAttributePair>())
                    .Select(p => new SalesChannelTrN11ProductAttribute(p.Name, p.Value))
                    .ToList()))
            .ToList();

        // Stok kodları PLANLANIR (entity mutasyonu YOK): mevcut dondurulmuş satır kodu tercih edilir, yoksa üretilir.
        // Satırlar ancak BAŞARILI push sonrası ReconcileSkus ile kalıcılaşır — başarısız push bayat kod dondurmasın.
        var stockCodePlan = channelProduct.PlanStockCodes(canonicalCandidates);

        // stockItem'lar ADAY-bazlı: fiyat/stok/kimlik eşleşen ERP varyantından, attribute'ler validasyondan geçen
        // (sihirbaz kullanıldıysa N11-uyumlu ad/değer, aksi halde ERP nitelikleri).
        var stockItems = canonicalCandidates.Select(c =>
        {
            var v = variantById[c.VariantId];
            return new N11ProductStockItem(
                SellerStockCode: stockCodePlan[c.VariantId],
                Quantity: v.StockQuantity,
                OptionPrice: v.SalePrice,
                Attributes: validated.VariantOptions.TryGetValue(c.VariantId, out var pairs) ? pairs : new List<N11ProductAttributePair>(),
                Gtin: v.Gtin,
                Mpn: v.Mpn,
                Oem: v.Oem);
        }).ToList();

        var images = imageUrls
            .Select((url, index) => new N11ProductImage(url, index + 1))
            .ToList();

        var data = new N11ProductData(
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
            Attributes: validated.ProductAttributes,       // varyant eksenleri FİLTRELİ + kanonik değerler
            StockItems: stockItems,
            SpecialInfo: channelProduct.SpecialInfo.Select(s => new N11ProductSpecialInfo(s.Key, s.Value)).ToList(),
            Discount: BuildDiscount(product));             // ürün-seviyesi indirim (None ise null)

        return new N11ProductPushPlan(data, canonicalCandidates);
    }

    /// <summary>Push planı — N11'e gidecek veri + BAŞARILI push sonrası SKU satırlarını kurmak için kanonik adaylar
    /// (kod donması yalnız başarılı push'ta gerçekleşsin diye ReconcileSkus çağrısı push sonrasına ertelenir).</summary>
    private sealed record N11ProductPushPlan(N11ProductData Data, List<N11SkuPushCandidate> Candidates);

    // Push adayları: sihirbaz (VariantAxes) varsa eksen kartezyeni → her kombinasyon imza eşleşen ERP varyantından
    // fiyat/stok/kod alır (attribute = N11 ad/değer); yoksa ERP varyantları doğrudan (nitelikleri stockItem'a gider).
    private List<N11SkuPushCandidate> BuildPushCandidates(
        SalesChannelTrN11Product channelProduct,
        List<ProductVariant> variants,
        Dictionary<Guid, List<N11ProductAttributePair>> variantOptions)
    {
        if (channelProduct.VariantAxes.Count == 0)
        {
            return variants
                .Select(v => new N11SkuPushCandidate(
                    v.Id,
                    v.Code,
                    (variantOptions.TryGetValue(v.Id, out var opts) ? opts : new List<N11ProductAttributePair>())
                        .Select(p => new SalesChannelTrN11ProductAttribute(p.Name, p.Value))
                        .ToList()))
                .ToList();
        }

        var combinations = BuildAxisCombinations(channelProduct.VariantAxes);
        var result = new List<N11SkuPushCandidate>();
        foreach (var combo in combinations)
        {
            // TAM imza eşleşmesi: N11 eksen seti, ERP varyantının nitelik setini BİREBİR kapsamalı (isim/değer kararı).
            // Fazladan/eksik nitelik → imza farkı → eşleşme yok → net hata (kullanıcı N11 eksenlerini üründekiyle hizalar).
            var signature = AxisSignature(combo.Select(p => (p.Name, p.Value)));
            var match = variants.FirstOrDefault(v =>
                variantOptions.TryGetValue(v.Id, out var opts)
                && AxisSignature(opts.Select(p => (p.Name, p.Value))) == signature);
            if (match is null)
            {
                var comboText = string.Join(", ", combo.Select(p => $"{p.Name}: {p.Value}"));
                throw new BusinessException("TradeXpress:N11:Product:AxisCombinationNoVariant").WithData("Combination", comboText);
            }

            result.Add(new N11SkuPushCandidate(
                match.Id,
                match.Code,
                combo.Select(p => new SalesChannelTrN11ProductAttribute(p.Name, p.Value)).ToList()));
        }

        return result;
    }

    // Eksenlerin DEĞER KARTEZYENİ — [Beden:{S,M}, Renk:{K,M}] → {Beden:S,Renk:K}, {Beden:S,Renk:M}, ...
    private static List<List<N11ProductAttributePair>> BuildAxisCombinations(List<SalesChannelTrN11ProductVariantAxis> axes)
    {
        var result = new List<List<N11ProductAttributePair>> { new() };
        foreach (var axis in axes)
        {
            var next = new List<List<N11ProductAttributePair>>();
            foreach (var partial in result)
            {
                foreach (var value in axis.Values)
                {
                    next.Add(new List<N11ProductAttributePair>(partial) { new(axis.Name, value) });
                }
            }

            result = next;
        }

        return result;
    }

    // Ada göre sıralı, Türkçe-normalize "NAME<US>VALUE" çiftleri <RS> ile — entity SignatureOf ile aynı mantık
    // (İ/ı katlaması tutarlı; ayraçlar metinde geçmez → birleşim belirsizliği yok).
    private static string AxisSignature(IEnumerable<(string Name, string Value)> pairs)
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        return string.Join(
            '',
            pairs
                .Select(p => $"{p.Name.Trim().ToUpper(turkish)}{p.Value.Trim().ToUpper(turkish)}")
                .OrderBy(x => x, StringComparer.Ordinal));
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
            FormatDiscountDate(product.DiscountStartDate),
            FormatDiscountDate(product.DiscountEndDate));
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
            FormatDiscountDate(product.DiscountStartDate),
            FormatDiscountDate(product.DiscountEndDate));
    }

    private static string DiscountTypeCode(ProductDiscountType type)
    {
        return type == ProductDiscountType.Percentage ? "2" : "1";
    }

    private static string FormatDiscountDate(DateTime? date)
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
        entity.SetVariantAxes(input.VariantAxes.Select(a => new SalesChannelTrN11ProductVariantAxis(a.Name, a.Values)));
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
