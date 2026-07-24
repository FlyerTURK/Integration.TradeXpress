using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy pazaryerinden İÇE AKTARMA dilimi (Trendyol <c>ImportFromMarketplaceAsync</c> faithful port + Etsy delta).
/// Mağazadaki MEVCUT aktif listelemeler salt GET ile çekilir (Etsy'ye SIFIR yazma) ve TAM ZİNCİR yazılır: ŞABLON
/// <see cref="Product"/> + <b>GERÇEK offering grafı</b> (kartezyen DEĞİL — Etsy'nin girdiği set) [<see cref="EntityAttribute"/>/
/// <see cref="EntityAttributeValue"/>/<see cref="EntityVariant"/>/<see cref="EntityVariantAttributeValue"/> +
/// <see cref="ProductVariantDetail"/>] + bağlı <see cref="SalesChannelEtsyProduct"/>.
///
/// <para><b>İdempotency:</b> kanal kaydı = <see cref="SalesChannelEtsyProduct.EtsyListingId"/> (fetch'ten set); offering =
/// Etsy inventory <c>product_id</c> → <see cref="SalesChannelEtsyProductSku.EtsyProductId"/>. YENİ ALAN/MIGRATION YOK.
/// İkinci import dublike üretmez; mevcut kaydı bulur, kanal alanlarını tazeler + Sku kimliklerini yeniden bağlar
/// (ekleme-only — mevcut şablon/varyant grafına DOKUNMAZ; Trendyol minimal-güncelleme kuralıyla hizalı). Uzak STOK
/// K12 politikasına tabidir (2026-07-23 kesin karar): çekirdek <c>StockQuantity</c> yalnız İLK import'ta tohumlanır;
/// re-import'ta remote stok çekirdeği EZMEZ — fark <see cref="SalesChannelEtsyProductStockItem.OverrideStock"/>'a
/// yazılır (kanal gerçeği) + LogWarning + rapor sayacı (<see cref="ApplyImportStockPolicyAsync"/>).</para>
///
/// <para><b>Varyant grafı doğrudan repo insert ile kurulur</b> (Trendyol gibi; <c>IEntityVariantGraphService</c>/
/// synchronizer KULLANILMAZ — kartezyen regen istemiyoruz, Etsy'nin GERÇEK offering setini koruyoruz).</para>
/// </summary>
public partial class SalesChannelEtsyProductAppService
{
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<EtsyImportResultDto> ImportFromMarketplaceAsync(Guid salesChannelId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);
        if (string.IsNullOrWhiteSpace(channel.ShopId))
        {
            // OAuth bağlantısı mağaza kimliğini çözmediyse listeleme ucu çağrılamaz — dostane fail-fast.
            throw new BusinessException("TradeXpress:Etsy:Product:ShopNotResolved");
        }

        // Salt GET: mağazanın tüm aktif listelemeleri (inventory offering'leri + görselleriyle) sayfa sayfa çekilir.
        var credentials = new EtsyCredentials(channel.Id, $"{channel.Keystring}:{channel.SharedSecret}", channel.ShopId!);
        var listings = await _etsyProductClient.GetAllListingsAsync(credentials);

        var report = new EtsyImportResultDto
        {
            TotalRemoteListings = listings.Count,
            TotalFetchedOfferings = listings.Sum(l => l.Offerings.Count),
        };

        // Kanalın mevcut kayıtları — eşleşme anahtarı EtsyListingId (import bağlamında bellek-içi tarama yeterli).
        var existingRecords = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == channel.CompanyId && x.SalesChannelId == channel.Id));

        var currencyCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        foreach (var listing in listings)
        {
            if (listing.Offerings.Count == 0)
            {
                // Offering'siz listeleme (inventory boş) — varyant grafı kurulamaz, atla + raporla (sessiz geçilmez).
                report.SkippedRows.Add(new EtsyImportIssueDto
                {
                    ListingId = listing.ListingId,
                    Title = listing.Title,
                    Reason = L["EtsyImport:NoOffering"].Value,
                });
                continue;
            }

            var currencyUnitId = await ResolveCurrencyUnitIdByCodeAsync(listing.CurrencyCode, currencyCache, report);

            var existing = FindExistingChannelRecord(listing, existingRecords);

            Product product;
            Dictionary<long, Guid> variantByEtsyProductId;
            if (existing is not null)
            {
                // Re-import: mevcut şablon + varyant grafına DOKUNULMAZ (kullanıcı düzenlemiş olabilir); yalnız kanal
                // alanları tazelenir + Sku kimlikleri mevcut varyantlara yeniden bağlanır (ekleme-only, basit sürüm).
                product = await GetOwnedProductAsync(existing.ProductId);

                // Görsel GERİ-DOLDURMA: ürünün görseli YOKSA listelemeden doldur (DOLDURMA-ONLY — mevcut görselleri
                // EZMEZ, kullanıcı düzenlemesi korunur). Eski görsel-bug'lı import'ların görselini re-import ile kurtarır.
                if (product.Images.Count == 0 && listing.ImageUrls.Count > 0)
                {
                    product.SetImages(await _imageDownloader.BuildFromUrlsAsync(product.Code, listing.ImageUrls));
                    await _productRepository.UpdateAsync(product, autoSave: true);
                }

                variantByEtsyProductId = existing.Skus
                    .Where(s => s.EtsyProductId is > 0)
                    .GroupBy(s => s.EtsyProductId!.Value)
                    .ToDictionary(g => g.Key, g => g.First().ProductVariantId);
            }
            else
            {
                (product, variantByEtsyProductId) = await CreateTemplateProductAsync(channel, listing, currencyUnitId, report);
            }

            var entity = await UpsertChannelRecordAsync(channel, listing, existing, product, currencyUnitId, variantByEtsyProductId, report);
            if (existing is null)
            {
                existingRecords.Add(entity);   // aynı import içinde ikinci geçiş aynı kaydı bulabilsin
            }
            else
            {
                // K12 stok politikası (2026-07-23 kesin karar): çekirdek kayıt ZATEN VARDI (update yolu) →
                // remote stok çekirdek StockQuantity'yi EZMEZ; fark kanal OverrideStock'una yazılır (kanal
                // gerçeği) + görünür kılınır (LogWarning + rapor sayacı). Create yolunda gereksiz: varyantlar
                // az önce remote stokla tohumlandı (CreateTemplateProductAsync), fark tanım gereği yok.
                await ApplyImportStockPolicyAsync(channel, entity, listing, product, variantByEtsyProductId, report);
            }
        }

        return report;
    }

    /// <summary>Re-import stok politikası (K12): offering'in remote stoğu (negatif → 0 clamp'li) eşlenen çekirdek
    /// varyantın <see cref="EntityVariant.StockQuantity"/>'siyle karşılaştırılır. AYNIYSA mevcut başlıktaki bayat
    /// <see cref="SalesChannelEtsyProductStockItem.OverrideStock"/> temizlenir (null = ERP'den devral; başlık yoksa
    /// KURULMAZ — gürültü üretme). FARKLIYSA remote değer kanal override'ı olur (başlık yoksa kurulur; YENİ başlığa
    /// kanal gider satırları da eklenir — <see cref="SideCostRecipeComposer.EnsureLines"/>, klon/Trendyol import
    /// yaşam anıyla aynı) + fark satır-bazında LogWarning + rapor sayacıyla görünür kılınır (sessiz geçilmez).
    /// Eşlenemeyen offering (Sku bağı yok) atlanır — Sku bağı kurulunca sonraki import değerlendirir.</summary>
    private async Task ApplyImportStockPolicyAsync(
        SalesChannelEtsy channel,
        SalesChannelEtsyProduct entity,
        EtsyRemoteListing listing,
        Product product,
        Dictionary<long, Guid> variantByEtsyProductId,
        EtsyImportResultDto report)
    {
        var variantIds = listing.Offerings
            .Where(o => o.EtsyProductId > 0)
            .Select(o => variantByEtsyProductId.TryGetValue(o.EtsyProductId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (variantIds.Count == 0)
        {
            return;
        }

        var variantsById = (await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync()).Where(v => variantIds.Contains(v.Id))))
            .ToDictionary(v => v.Id);
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelEtsyProductId == entity.Id && h.ProductVariantId != null
                        && variantIds.Contains(h.ProductVariantId!.Value))))
            .ToDictionary(h => h.ProductVariantId!.Value);

        SideCostPlan? sideCostPlan = null;   // tembel — yalnız YENİ başlık kurulursa gerekir
        foreach (var offering in listing.Offerings)
        {
            if (offering.EtsyProductId <= 0
                || !variantByEtsyProductId.TryGetValue(offering.EtsyProductId, out var variantId)
                || !variantsById.TryGetValue(variantId, out var variant))
            {
                continue;   // çekirdek varyant çözülemedi — Sku bağı sonraki import'ta kurulur
            }

            var remoteStock = Math.Max(0, offering.Quantity);
            if (remoteStock == variant.StockQuantity)
            {
                // Fark yok → varsa bayat override temizlenir (null = ERP'den devral); başlık yoksa kurulmaz.
                if (headers.TryGetValue(variantId, out var cleanHeader) && cleanHeader.OverrideStock is not null)
                {
                    cleanHeader.SetOverrideStock(null);
                    await _stockItemRepository.UpdateAsync(cleanHeader, autoSave: true);
                }

                continue;
            }

            report.StockDifferenceCount++;
            Logger.LogWarning(
                "Etsy import stok farkı: ürün {ProductCode} / varyant {VariantCode} — çekirdek {CoreStock}, remote {RemoteStock}. Çekirdek EZİLMEDİ; remote değer kanal OverrideStock'una yazıldı.",
                product.Code,
                variant.Code,
                variant.StockQuantity,
                remoteStock);

            if (headers.TryGetValue(variantId, out var header))
            {
                header.SetOverrideStock(remoteStock);
                await _stockItemRepository.UpdateAsync(header, autoSave: true);
                continue;
            }

            header = new SalesChannelEtsyProductStockItem(entity.CompanyId, entity.Id, variantId);
            header.SetOverrideStock(remoteStock);
            await _stockItemRepository.InsertAsync(header, autoSave: true);
            headers[variantId] = header;

            // Yan-maliyet satırları yalnız YENİ başlıkta kurulur (klon/Trendyol import yollarıyla aynı yaşam anı) —
            // persist MEVCUT merkezi mekanikle (SaveChannelRecipeLinesAsync; paralel kayıt yolu YOK).
            sideCostPlan ??= SideCostPlan.From(channel.SideCosts, resolvedCommissionRate: null, variantOptInEnabled: false);
            var recipeLines = new List<ProductRecipeLineGraphDto>();
            if (SideCostRecipeComposer.EnsureLines(recipeLines, sideCostPlan))
            {
                await SaveChannelRecipeLinesAsync(entity, header.Id, recipeLines);
            }
        }
    }

    // ── Eşleşme (idempotency: EtsyListingId) ────────────────────────────────────────────────────────

    /// <summary>Mevcut kanal kaydı eşleşmesi — anahtar <see cref="SalesChannelEtsyProduct.EtsyListingId"/> birebir
    /// (fetch'ten gelen listing_id). Çoklu aday olası değildir (listing_id kanal içinde tekildir); yine de
    /// deterministik olsun diye SequenceNo→Id sıralanır.</summary>
    private static SalesChannelEtsyProduct? FindExistingChannelRecord(
        EtsyRemoteListing listing, List<SalesChannelEtsyProduct> existingRecords)
    {
        return existingRecords
            .Where(r => r.EtsyListingId == listing.ListingId)
            .OrderBy(r => r.SequenceNo)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }

    // ── Şablon Product + GERÇEK offering grafı (yalnız İLK import) ───────────────────────────────────

    /// <summary>Uzak listelemeden şablon <see cref="Product"/> + GERÇEK offering grafını üretir (repo insert). Code =
    /// ilk offering sku ?? "ETSY-{listing_id}" (benzersizlik döngülü), Name = başlık CASING KORUNARAK
    /// (<c>SetName(name, normalizeTitle:false)</c>), Description şablon sınırına kırpılır, görseller URL-kaynaklı, para
    /// birimi shop currency; menşe alanları (who_made/when_made) listelemeden. Graf: offering'lerin DISTINCT
    /// property'lerinden <see cref="EntityAttribute"/>/<see cref="EntityAttributeValue"/>, her offering → bir
    /// <see cref="EntityVariant"/> (ilk offering MAIN) + seçili değer bağları (<see cref="EntityVariantAttributeValue"/>) +
    /// fiyat (<see cref="ProductVariantDetail"/>). Ana-varyant değişmezi merkezî kapıdan
    /// (<see cref="EntityVariantManager.EnsureMainVariantAsync"/>). Döner: ürün + offering.EtsyProductId → varyant.Id.</summary>
    private async Task<(Product Product, Dictionary<long, Guid> VariantByEtsyProductId)> CreateTemplateProductAsync(
        SalesChannelEtsy channel, EtsyRemoteListing listing, Guid? currencyUnitId, EtsyImportResultDto report)
    {
        var companyId = channel.CompanyId;
        var offerings = listing.Offerings;
        var code = await BuildUniqueProductCodeAsync(companyId, offerings[0].Sku, listing.ListingId);

        // Ad TEK atamayla casing-korumalı yazılır (ctor'a geçici ad = KOD; hemen normalizeTitle:false ile gerçek başlık).
        var product = new Product(companyId, code, code);
        product.SetName(BuildSafeName(listing.Title, code), normalizeTitle: false);
        product.SetDescription(BuildTemplateDescription(listing.Description, ProductConsts.DescriptionMaxLength));
        product.SetCurrencyUnit(currencyUnitId);
        product.SetImages(await _imageDownloader.BuildFromUrlsAsync(code, listing.ImageUrls));
        if (listing.WhoMade is { } whoMade)
        {
            product.SetWhoMade(whoMade);
        }

        if (listing.WhenMade is { } whenMade)
        {
            product.SetMadePeriod(whenMade);
        }

        await _productRepository.InsertAsync(product, autoSave: true);
        report.CreatedProducts++;

        var (attributeByName, valueByKey) = await BuildAttributeGraphAsync(companyId, product.Id, offerings);

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variantByEtsyProductId = new Dictionary<long, Guid>();
        for (var i = 0; i < offerings.Count; i++)
        {
            var offering = offerings[i];
            var variantCode = BuildUniqueVariantCode(offering.Sku, code, i + 1, usedCodes);

            // Agnostik EntityVariant — Ad CASE-KORUR (EnsureRequiredText; TitleCase YOK) → başlık doğrudan.
            var variant = new EntityVariant(
                companyId,
                ProductEntityName,
                product.Id,
                variantCode,
                BuildSafeName(listing.Title, variantCode),
                isMain: i == 0);
            variant.SetStock(Math.Max(0, offering.Quantity));
            await _variantRepository.InsertAsync(variant, autoSave: true);
            report.CreatedVariants++;

            // Varyant ↔ seçili nitelik-değer bağları (bu offering'in property çiftleri).
            foreach (var property in offering.Properties)
            {
                if (!attributeByName.TryGetValue(property.Name, out var attribute)
                    || !valueByKey.TryGetValue((attribute.Id, NormalizeValueKey(property.Value)), out var value))
                {
                    continue;   // grafta karşılığı yoksa (savunma) atla — offering yine oluşur
                }

                var link = new EntityVariantAttributeValue(companyId, variant.Id, attribute.Id, value.Id);
                await _variantAttributeRepository.InsertAsync(link, autoSave: true);
            }

            // Satış fiyatı Product uzantısında (ProductVariantDetail). Negatif uzak fiyat guard'la süzülür.
            var salePrice = offering.Price is >= 0 ? offering.Price : null;
            var detail = new ProductVariantDetail(companyId, variant.Id);
            detail.SetSalePrice(salePrice, salePrice is null ? null : currencyUnitId);
            await _variantDetailRepository.InsertAsync(detail, autoSave: true);

            if (offering.EtsyProductId > 0)
            {
                variantByEtsyProductId[offering.EtsyProductId] = variant.Id;
            }
        }

        // Ana-varyant değişmezi merkezî kapıdan (tekil main garanti; idempotent) — agnostik EntityVariantManager.
        await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, companyId);
        return (product, variantByEtsyProductId);
    }

    /// <summary>Offering'lerin DISTINCT property adlarından <see cref="EntityAttribute"/>, her adın DISTINCT
    /// değerlerinden <see cref="EntityAttributeValue"/> üretir (repo insert; ilk-görülme sırası). Döner: ad→nitelik +
    /// (nitelikId, normalize değer)→değer sözlükleri (varyant bağı kurulumu için). Offering'de property yoksa
    /// (tek-varyant listeleme) boş graf.</summary>
    private async Task<(Dictionary<string, EntityAttribute> AttributeByName, Dictionary<(Guid AttributeId, string ValueKey), EntityAttributeValue> ValueByKey)>
        BuildAttributeGraphAsync(Guid companyId, Guid productId, IReadOnlyList<EtsyRemoteOffering> offerings)
    {
        var attributeByName = new Dictionary<string, EntityAttribute>(StringComparer.OrdinalIgnoreCase);
        var valueByKey = new Dictionary<(Guid AttributeId, string ValueKey), EntityAttributeValue>();
        var valueOrderByAttribute = new Dictionary<Guid, int>();
        var attributeOrder = 0;

        foreach (var offering in offerings)
        {
            foreach (var property in offering.Properties)
            {
                if (!attributeByName.TryGetValue(property.Name, out var attribute))
                {
                    attribute = new EntityAttribute(companyId, ProductEntityName, productId, property.Name, attributeOrder++);
                    await _attributeRepository.InsertAsync(attribute, autoSave: true);
                    attributeByName[property.Name] = attribute;
                    valueOrderByAttribute[attribute.Id] = 0;
                }

                var valueKey = (attribute.Id, NormalizeValueKey(property.Value));
                if (!valueByKey.ContainsKey(valueKey))
                {
                    var order = valueOrderByAttribute[attribute.Id];
                    var value = new EntityAttributeValue(companyId, attribute.Id, property.Value, order);
                    await _attributeValueRepository.InsertAsync(value, autoSave: true);
                    valueByKey[valueKey] = value;
                    valueOrderByAttribute[attribute.Id] = order + 1;
                }
            }
        }

        return (attributeByName, valueByKey);
    }

    private static string NormalizeValueKey(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    // ── Kanal kaydı upsert ──────────────────────────────────────────────────────────────────────────

    /// <summary>Etsy kanal ürününü upsert eder: yeni kayıtta SellerSkuBase ("{Kod}-{Sıra}", frozen) üretilir; her
    /// geçişte taksonomi/listeleme tipi/etiket/malzeme/para birimi/açıklama-override + <see cref="SalesChannelEtsyProduct.EtsyListingId"/>
    /// (MarkSynced) tazelenir ve offering'lerin Sku kimlikleri (FrozenSku remote'tan, EtsyProductId inventory'den, FROZEN)
    /// işlenir.</summary>
    private async Task<SalesChannelEtsyProduct> UpsertChannelRecordAsync(
        SalesChannelEtsy channel,
        EtsyRemoteListing listing,
        SalesChannelEtsyProduct? existing,
        Product product,
        Guid? currencyUnitId,
        Dictionary<long, Guid> variantByEtsyProductId,
        EtsyImportResultDto report)
    {
        var entity = existing;
        if (entity is null)
        {
            var sequenceNo = await NextSequenceNoAsync(channel.Id, product.Id);
            entity = new SalesChannelEtsyProduct(
                channel.CompanyId,
                channel.Id,
                product.Id,
                BuildSellerSkuBase(product.Code, sequenceNo),
                sequenceNo,
                listing.ListingType);
        }

        entity.SetTaxonomy(listing.TaxonomyId is > 0 ? listing.TaxonomyId : null);
        entity.SetListingType(listing.ListingType);
        entity.SetTags(listing.Tags.Select(t => new SalesChannelEtsyProductTag(t)));
        entity.SetMaterials(listing.Materials.Select(m => new SalesChannelEtsyProductMaterial(m)));
        entity.SetCurrencyUnit(currencyUnitId);
        entity.SetDescriptionOverride(BuildTemplateDescription(listing.Description, SalesChannelEtsyProductConsts.DescriptionOverrideMaxLength));
        entity.MarkSynced(listing.ListingId, "active", Clock.Now.ToUniversalTime());

        // Sku kimlikleri — offering.EtsyProductId → yerel varyant (yeni üründe map dolu; mevcut üründe var olan Sku
        // bağından yeniden kurulur). Çözülemeyen offering (map'te yok) atlanır — Sku bağı bir sonraki import'ta kurulur.
        foreach (var offering in listing.Offerings)
        {
            if (offering.EtsyProductId <= 0
                || !variantByEtsyProductId.TryGetValue(offering.EtsyProductId, out var variantId))
            {
                continue;
            }

            var frozenSku = offering.Sku is { Length: > 0 } sku
                ? Truncate(sku.Trim(), SalesChannelEtsyProductConsts.StockCodeMaxLength)
                : Truncate($"ETSY-{offering.EtsyProductId}", SalesChannelEtsyProductConsts.StockCodeMaxLength);
            entity.UpsertImportedSku(variantId, frozenSku, offering.EtsyProductId);
        }

        if (existing is null)
        {
            await _repository.InsertAsync(entity, autoSave: true);
            report.CreatedChannelProducts++;
        }
        else
        {
            await _repository.UpdateAsync(entity, autoSave: true);
            report.UpdatedChannelProducts++;
        }

        return entity;
    }

    // ── Para birimi çözümü ──────────────────────────────────────────────────────────────────────────

    /// <summary>Etsy shop <c>currency_code</c>'unu (ör. "USD"/"EUR") yerel <see cref="CurrencyUnit"/> id'sine çevirir
    /// (HOST/TENANT kaydı; <see cref="IMultiTenant"/> filtresi KAPALI — order-sync <c>ResolveCurrencyUnitIdByCode</c>
    /// deseni; host kaydı tenant filtresiyle gizlenmesin). Kanal-başı cache. Bulunamazsa null + rapora uyarı
    /// (tutar yine yazılır; yalnız para birimi bağı boş kalır).</summary>
    private async Task<Guid?> ResolveCurrencyUnitIdByCodeAsync(string? code, Dictionary<string, Guid?> cache, EtsyImportResultDto report)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        using (DataFilter.Disable<IMultiTenant>())
        {
            var candidates = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(c => c.Code == normalized));
            var preferred = candidates.FirstOrDefault(c => c.TenantId == CurrentTenant.Id)
                            ?? candidates.FirstOrDefault(c => c.TenantId == null);
            if (preferred is null)
            {
                report.Warnings.Add(L["EtsyImport:CurrencyMissing", normalized].Value);
            }

            cache[normalized] = preferred?.Id;
            return preferred?.Id;
        }
    }

    // ── Kod / metin normalizasyon (Trendyol import onarım felsefesiyle hizalı) ───────────────────────

    /// <summary>Şablon kodu: sku ?? "ETSY-{listing_id}" normalize edilir; şirket içinde benzersizlik "-2/-3..." son
    /// ekiyle döngülü sağlanır (Code unique index'i ham DB hatasına düşmesin). Soft-delete filtresi KAPALI: silinmiş
    /// satır kodu hâlâ işgal eder (Trendyol BuildUniqueProductCodeAsync ile aynı bilinçli simetri).</summary>
    private async Task<string> BuildUniqueProductCodeAsync(Guid companyId, string? sku, long listingId)
    {
        var rawCode = sku is { Length: > 0 } value ? value : $"ETSY-{listingId}";
        using (DataFilter.Disable<ISoftDelete>())
        {
            var baseCode = NormalizeImportCode(rawCode, ProductConsts.CodeMaxLength);
            var candidate = baseCode;
            var suffix = 2;
            while (await AsyncExecuter.AnyAsync(
                       (await _productRepository.GetQueryableAsync())
                           .Where(p => p.CompanyId == companyId && p.Code == candidate)))
            {
                var suffixText = $"-{suffix}";
                candidate = Truncate(baseCode, ProductConsts.CodeMaxLength - suffixText.Length) + suffixText;
                suffix++;
            }

            return candidate;
        }
    }

    /// <summary>Varyant kodu — sku ?? "{ÜrünKodu}-{index}"; benzersizlik YENİ ürünün kendi içinde (bellek-içi küme).</summary>
    private static string BuildUniqueVariantCode(string? sku, string productCode, int index, HashSet<string> usedCodes)
    {
        var rawCode = sku is { Length: > 0 } value ? value : $"{productCode}-{index}";
        var baseCode = NormalizeImportCode(rawCode, EntityVariantConsts.VariantCodeMaxLength);
        var candidate = baseCode;
        var suffix = 2;
        while (!usedCodes.Add(candidate))
        {
            var suffixText = $"-{suffix}";
            candidate = Truncate(baseCode, EntityVariantConsts.VariantCodeMaxLength - suffixText.Length) + suffixText;
            suffix++;
        }

        return candidate;
    }

    /// <summary>Import kod normalizasyonu — Code konvansiyonuyla aynı çekirdek (<c>NormalizeAsCode</c>), üstüne import
    /// dayanıklılığı: boş → "ETSY", kısa (&lt;3) → "ETSY-" ön eki, uzun → kırp. Fail-fast yerine onarım BİLİNÇLİ:
    /// uzak veri bizim kontrolümüzde değil, kalem kaybetmek daha kötü (Trendyol NormalizeImportCode ile aynı felsefe).</summary>
    private static string NormalizeImportCode(string rawCode, int maxLength)
    {
        var normalized = rawCode.NormalizeAsCode();
        if (normalized.Length == 0)
        {
            normalized = "ETSY";
        }

        if (normalized.Length < EntityFieldConsts.CodeMinLength)
        {
            normalized = $"ETSY-{normalized}";
        }

        return Truncate(normalized, maxLength);
    }

    /// <summary>Ad emniyeti: başlık boş/çok kısaysa kod kullanılır; uzun başlık şablon sınırına kırpılır.</summary>
    private static string BuildSafeName(string? title, string fallback)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length >= EntityFieldConsts.NameMinLength
            ? Truncate(trimmed, ProductConsts.NameMaxLength)
            : fallback;
    }

    /// <summary>Açıklama: verilen sınıra kırpılır; min sınırın (10) altındaysa null (opsiyonel alan zorlanmaz).</summary>
    private static string? BuildTemplateDescription(string? description, int maxLength)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < EntityFieldConsts.DescriptionMinLength)
        {
            return null;
        }

        return Truncate(trimmed, maxLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
