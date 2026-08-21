using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Attachments;
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
/// K12 politikasına tabidir (2026-07-23 kesin karar): core <c>StockQuantity</c> yalnız İLK import'ta seed'lenir;
/// re-import'ta remote stok core'u EZMEZ — fark <see cref="SalesChannelEtsyProductStockItem.OverrideStock"/>'a
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

            var existing = await DiscardOrphanedRecordAsync(
                FindExistingChannelRecord(listing, existingRecords), existingRecords, report);

            Product product;
            Dictionary<long, Guid> variantByEtsyProductId;
            if (existing is not null)
            {
                // Re-import: mevcut şablon + varyant grafına DOKUNULMAZ (kullanıcı düzenlemiş olabilir); yalnız kanal
                // alanları tazelenir + Sku kimlikleri mevcut varyantlara yeniden bağlanır (ekleme-only, basit sürüm).
                product = await GetOwnedProductAsync(existing.ProductId);

                // Görsel GERİ-DOLDURMA: ürünün DAM'da medyası YOKSA listelemeden doldur (DOLDURMA-ONLY — mevcut
                // görselleri EZMEZ, kullanıcı düzenlemesi korunur). Eski görsel-bug'lı import'ları re-import kurtarır.
                if (listing.ImageUrls.Count > 0
                    && (await _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id)).Count == 0)
                {
                    report.SkippedImages += (await _imageDownloader.ImportToProductAsync(product, listing.ImageUrls))
                        .SkippedForCapacityCount;
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
                // K12 stok politikası (2026-07-23 kesin karar): core kayıt ZATEN VARDI (update yolu) →
                // remote stok core StockQuantity'yi EZMEZ; fark kanal OverrideStock'una yazılır (kanal
                // gerçeği) + görünür kılınır (LogWarning + rapor sayacı). Create yolunda gereksiz: varyantlar
                // az önce remote stokla seed'lendi (CreateTemplateProductAsync), fark tanım gereği yok.
                await ApplyImportStockPolicyAsync(channel, entity, listing, product, variantByEtsyProductId, report);
            }

            // Varyasyon fotoğrafları EN SONA bırakılır: eşleştirmenin girdisi offering→ERP varyantı haritasıdır ve
            // o harita ancak Sku kimlikleri yazıldıktan sonra (her iki yolda da) tamamlanmış olur.
            await ImportVariationImagesAsync(credentials, listing, product, variantByEtsyProductId, report);
        }

        ReportSkippedImages(report);
        ReportUnmappedVariationImages(report);
        return report;
    }

    /// <summary>Eşleştirilemeyen VARYASYON fotoğraflarını rapora taşır — import-başı TEK satır (görsel sınırı
    /// satırıyla aynı gerekçe: ürün başına uyarı kalabalık mağazada raporu okunmaz kılar).
    ///
    /// <para><b>Neden ayrı bir satır:</b> "sınıra takıldı" ile "hangi varyanta ait olduğu çözülemedi" farklı
    /// sorunlardır ve farklı çözümleri vardır (biri galeriyi boşaltmayı, diğeri Etsy tarafındaki varyasyon
    /// kurulumunu ilgilendirir). İkisini tek sayaçta toplamak kullanıcıyı yanlış tarafa bakmaya iterdi.</para></summary>
    private void ReportUnmappedVariationImages(EtsyImportResultDto report)
    {
        if (report.UnmappedVariationImages > 0)
        {
            report.Warnings.Add(L["EtsyImport:VariationImagesUnmapped", report.UnmappedVariationImages].Value);
        }
    }

    /// <summary>Görsel sınırına takılıp hiç bağlanmayan pazaryeri görsellerini RAPORA taşır (N11/Trendyol ikizi).
    ///
    /// <para><b>Neden gerekli:</b> sınır aşımı indiricide yalnız server-log'a düşüyordu; kullanıcı "içe aktarım
    /// başarılı" raporunu görüp fotoğrafın neden gelmediğini hiçbir ekranda bulamıyordu. Sayı ürün-başı değil
    /// import-başı TEK satırda verilir — kalabalık mağazada ürün başına uyarı raporu okunmaz hâle
    /// getirirdi.</para></summary>
    private void ReportSkippedImages(EtsyImportResultDto report)
    {
        if (report.SkippedImages > 0)
        {
            report.Warnings.Add(
                L["EtsyImport:ImagesSkippedForLimit", report.SkippedImages, ProductConsts.MaxImageCount].Value);
        }
    }

    /// <summary>Listelemenin VARYASYON fotoğraflarını ilgili ERP varyantlarının kendi medya bağlamına indirir
    /// ("ProductVariant" + varyant Id'si) — N11/Trendyol'un kalem-başına görsel yolunun Etsy karşılığı.
    ///
    /// <para><b>Eşleştirme zinciri:</b> <c>variation_images[].{property_id, value_id}</c> → o değeri TAŞIYAN
    /// offering'ler → offering'in bağlandığı ERP varyantı → <c>image_id</c> → listelemenin görsel setinden URL →
    /// indirici. Zincirin her halkası KİMLİKTİR.</para>
    ///
    /// <para><b>ETSY'NİN MODELİ: fotoğraf DEĞERE bağlanır, kombinasyona değil.</b> Etsy bir listelemede yalnız TEK
    /// varyasyon grubuna (ör. Renk) fotoğraf bağlanmasına izin verir; bizim varyantlarımız ise KOMBİNASYON başınadır
    /// (Renk×Beden). Dolayısıyla "Renk=Kırmızı" fotoğrafı, kırmızının TÜM bedenlerine — yani birden çok ERP
    /// varyantına — iner. Bu çoğaltma hata değil, iki modelin doğru çevirisidir: kırmızı-S ile kırmızı-M gerçekten
    /// aynı fotoğrafı paylaşır ve indirici içerik-hash dedup'ıyla dosyayı bir kez saklar.</para>
    ///
    /// <para><b>Kimlik yoksa UYDURMA EŞLEŞME YOK:</b> offering'in property'si <c>property_id</c>/<c>value_id</c>
    /// taşımıyorsa metin (ad/değer) eşleşmesine DÜŞÜLMEZ — Etsy'de aynı görünen iki değer farklı eksenlere ait
    /// olabilir ve fotoğrafı yanlış varyanta bağlamak, hiç bağlamamaktan çok daha zor fark edilir. Eşleşmeyen bağ
    /// sessizce yutulmaz, rapora sayılır.</para>
    ///
    /// <para><b>Hata izolasyonu:</b> uç patlarsa içe aktarım DURMAZ (görsel dalının mevcut sözleşmesi) — uyarı
    /// loglanır ve yalnız bu listelemenin varyant görselleri atlanır. İzolasyon <b>her</b> istisnayı kapsar,
    /// yalnız <see cref="BusinessException"/>'ı değil: bu, kardeşlerinin (Trendyol/N11 varyant görselini listeleme
    /// yanıtının İÇİNDE alır) aksine, DB yazımlarının ORTASINDA ve listeleme BAŞINA yapılan tek ağ çağrısıdır;
    /// gerçek hayattaki en olası arıza (zaman aşımı → <c>TaskCanceledException</c>, ağ/DNS/TLS →
    /// <c>HttpRequestException</c>, token yenileme hatası) <see cref="BusinessException"/> DEĞİLDİR ve dar bir
    /// catch onları dışarı bırakıp UoW'u rollback ederek mağazanın o ana kadar işlenmiş TÜM listelemelerini
    /// kaybettirirdi. Bu kök-neden-gizleyen boş bir <c>catch</c> değil, görsel dalında zaten kabul edilmiş
    /// gerekçesi loglanan izolasyondur (<c>MarketplaceImageDownloader.TryImportAsync</c> aynı desen).</para></summary>
    private async Task ImportVariationImagesAsync(
        EtsyCredentials credentials,
        EtsyRemoteListing listing,
        Product product,
        Dictionary<long, Guid> variantByEtsyProductId,
        EtsyImportResultDto report)
    {
        if (listing.Images.Count == 0 || variantByEtsyProductId.Count == 0)
        {
            return;   // indirilecek adres ya da bağlanacak varyant yok — uç boşuna çağrılmaz
        }

        IReadOnlyList<EtsyVariationImage> variationImages;
        try
        {
            variationImages = await _etsyProductClient.GetVariationImagesAsync(credentials, listing.ListingId);
        }
        catch (Exception ex) when (!IsGenuineCancellation(ex))
        {
            Logger.LogWarning(
                ex,
                "Etsy varyasyon fotoğrafları okunamadı (listing {ListingId}) — bu listelemenin varyant görselleri atlandı, içe aktarım sürüyor.",
                listing.ListingId);
            return;
        }

        if (variationImages.Count == 0)
        {
            return;   // varyasyon fotoğrafı olmayan listeleme NORMALDİR (fotoğraflar kayıt geneli galeride durur)
        }

        var urlByImageId = BuildImageUrlIndex(listing);
        var urlsByVariantId = new Dictionary<Guid, List<string>>();
        foreach (var variationImage in variationImages)
        {
            if (!urlByImageId.TryGetValue(variationImage.ImageId, out var url))
            {
                report.UnmappedVariationImages++;   // bağın işaret ettiği fotoğraf listelemenin setinde yok
                continue;
            }

            var matched = false;
            foreach (var offering in listing.Offerings)
            {
                if (offering.EtsyProductId <= 0
                    || !CarriesVariationValue(offering, variationImage)
                    || !variantByEtsyProductId.TryGetValue(offering.EtsyProductId, out var variantId))
                {
                    continue;
                }

                matched = true;
                if (!urlsByVariantId.TryGetValue(variantId, out var urls))
                {
                    urls = new List<string>();
                    urlsByVariantId[variantId] = urls;
                }

                if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(url);   // aynı fotoğraf aynı varyanta iki kez yazılmasın (aynı değeri taşıyan offering'ler)
                }
            }

            if (!matched)
            {
                report.UnmappedVariationImages++;
            }
        }

        if (urlsByVariantId.Count == 0)
        {
            return;
        }

        // Varyant KODU indiricinin kütüphane adlandırması için gerekir (hangi görsel hangi varyantın — addan okunur).
        var variantIds = urlsByVariantId.Keys.ToList();
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => variantIds.Contains(v.Id)));

        foreach (var variant in variants)
        {
            if (!urlsByVariantId.TryGetValue(variant.Id, out var urls))
            {
                continue;
            }

            // İndirici URL-başına dayanıklıdır (bozuk görsel atlanır + loglanır) ve EKLEMELİDİR: kullanıcının
            // varyanta elle bağladığı görseller bu çağrıyla EZİLMEZ. Sınıra takılan görsel rapora taşınır.
            report.SkippedImages += (await _imageDownloader.ImportToVariantAsync(
                    variant.Id,
                    product.CompanyId,
                    product.Code,
                    variant.Code,
                    urls))
                .SkippedForCapacityCount;
        }
    }

    /// <summary>İstisna GERÇEK bir iptal mi (çağıran/host durdurdu) — yoksa yalnız arıza mı? Geniş izolasyon bile
    /// iptali YUTMAMALIDIR: iptal "bu işi bırak" emridir, atlanacak bir görsel arızası değil.
    ///
    /// <para><b>Ayrımın inceliği:</b> <c>HttpClient</c> zaman aşımı da <c>TaskCanceledException</c> fırlatır (yani
    /// tipe bakmak yetmez), ama .NET zaman aşımını iç istisna olarak <see cref="TimeoutException"/> ile
    /// işaretler. Zaman aşımı = arıza (izole edilir), iç istisnasız iptal = emir (yeniden fırlatılır).</para></summary>
    private static bool IsGenuineCancellation(Exception ex)
    {
        return ex is OperationCanceledException && ex.InnerException is not TimeoutException;
    }

    /// <summary>Listelemenin görsellerini <c>listing_image_id</c> → URL olarak indeksler. Kimliksiz görsel (id 0)
    /// indekse GİRMEZ: 0 gerçek bir kimlik değil "Etsy kimliği vermedi"nin karşılığıdır, indekse alınsaydı ilk
    /// kimliksiz fotoğraf tüm eşleşmeleri kendine çekerdi.</summary>
    private static Dictionary<long, string> BuildImageUrlIndex(EtsyRemoteListing listing)
    {
        var index = new Dictionary<long, string>();
        foreach (var image in listing.Images)
        {
            if (image.ImageId > 0 && !index.ContainsKey(image.ImageId))
            {
                index[image.ImageId] = image.Url;
            }
        }

        return index;
    }

    /// <summary>Offering, varyasyon fotoğrafının bağlandığı (eksen, değer) çiftini taşıyor mu? YALNIZ KİMLİK
    /// karşılaştırılır — kimliği okunamamış property (null) hiçbir bağla eşleşmez ve metne düşülmez.</summary>
    private static bool CarriesVariationValue(EtsyRemoteOffering offering, EtsyVariationImage variationImage)
    {
        foreach (var property in offering.Properties)
        {
            if (property.PropertyId is { } propertyId && property.ValueId is { } valueId
                && propertyId == variationImage.PropertyId && valueId == variationImage.ValueId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Re-import stok politikası (K12): offering'in remote stoğu (negatif → 0 clamp'li) eşlenen core
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

        // OTORİTE DEVRİ (2026-08-05 Hakan kararı): ürün sınıflandırıldıysa (Calculated) pazaryerinde duran
        // stok GEÇERSİZDİR — sistem belirler. Yansıması yazılırsa her import devri geri alır (push zinciri
        // OverrideStock'u ÖNCELER). Varsa bayat yansıma temizlenir; yenisi YAZILMAZ.
        var authorityTransferred = product.StockPolicy == ProductStockPolicy.Calculated;

        SideCostPlan? sideCostPlan = null;   // tembel — yalnız YENİ başlık kurulursa gerekir
        foreach (var offering in listing.Offerings)
        {
            if (offering.EtsyProductId <= 0
                || !variantByEtsyProductId.TryGetValue(offering.EtsyProductId, out var variantId)
                || !variantsById.TryGetValue(variantId, out var variant))
            {
                continue;   // core varyant çözülemedi — Sku bağı sonraki import'ta kurulur
            }

            var remoteStock = Math.Max(0, offering.Quantity);
            if (remoteStock == variant.StockQuantity || authorityTransferred)
            {
                // Fark yok → varsa bayat override temizlenir (null = ERP'den devral); başlık yoksa kurulmaz.
                // TASARIM NOTU: OverrideStock kullanıcının rezerv alanı DEĞİL, pazaryerinin YANSIMASIDIR (K12 yönü:
                // core ezilmez, uzak gerçek kanal katmanına yazılır) → remote core'a eşitlendiğinde yansıma
                // değerinin sürmesi için sebep kalmaz. Trendyol ikizi de her import'ta remote ile tazeler.
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

    /// <summary>ÖKSÜZ kanal kaydını eler — Trendyol ikizi
    /// (<c>SalesChannelTrTrendyolProductAppService.Import.DiscardOrphanedRecordAsync</c>; gerekçe ve bedeli orada).
    /// Kısaca: şablon ürünü silinmiş kanal kaydı kullanılamaz durumdadır ve önceden TEK böyle kayıt tüm içe
    /// aktarımı <c>ProductNotFound</c> ile iptal ediyordu.</summary>
    private async Task<SalesChannelEtsyProduct?> DiscardOrphanedRecordAsync(
        SalesChannelEtsyProduct? existing,
        List<SalesChannelEtsyProduct> existingRecords,
        EtsyImportResultDto report)
    {
        if (existing is null || await FindOwnedProductAsync(existing.ProductId) is not null)
        {
            return existing;
        }

        // Kullanıcıya RAPORLANMAZ — gerekçe Trendyol ikizinde.
        Logger.LogInformation(
            "Etsy içe aktarım: {SellerSkuBase} kanal kaydının şablon ürünü ({ProductId}) silinmiş — kayıt kaldırıldı, ürün mağazadan yeniden kurulacak.",
            existing.SellerSkuBase,
            existing.ProductId);

        await _remover.RemoveGraphAsync(existing);
        existingRecords.Remove(existing);
        return null;
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
    /// fiyat (<see cref="ProductVariantDetail"/>). Ana-varyant değişmezi merkezî metottan
    /// (<see cref="EntityVariantManager.EnsureMainVariantAsync"/>). Döner: ürün + offering.EtsyProductId → varyant.Id.</summary>
    private async Task<(Product Product, Dictionary<long, Guid> VariantByEtsyProductId)> CreateTemplateProductAsync(
        SalesChannelEtsy channel, EtsyRemoteListing listing, Guid? currencyUnitId, EtsyImportResultDto report)
    {
        var companyId = channel.CompanyId;
        var offerings = listing.Offerings;
        var code = await BuildUniqueProductCodeAsync(companyId, offerings[0].Sku, listing.ListingId, report);

        // Ad TEK atamayla casing-korumalı yazılır (ctor'a geçici ad = KOD; hemen normalizeTitle:false ile gerçek başlık).
        var product = new Product(companyId, code, code);
        product.SetName(BuildSafeName(listing.Title, code), normalizeTitle: false);
        product.SetDescription(BuildTemplateDescription(listing.Description, ProductConsts.DescriptionMaxLength));
        product.SetCurrencyUnit(currencyUnitId);
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

        // Görseller DAM'a — link ürün Id'sine bağlandığından INSERT'ten SONRA (dedup + ilk görsel cover).
        report.SkippedImages += (await _imageDownloader.ImportToProductAsync(product, listing.ImageUrls))
            .SkippedForCapacityCount;

        var (attributeByName, valueByKey) = await BuildAttributeGraphAsync(companyId, product.Id, offerings);

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variantByEtsyProductId = new Dictionary<long, Guid>();
        for (var i = 0; i < offerings.Count; i++)
        {
            var offering = offerings[i];
            var variantCode = BuildUniqueVariantCode(offering.Sku, code, usedCodes);

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

        // Ana-varyant değişmezi merkezî EnsureMainVariantAsync'ten (tekil main garanti; idempotent) — agnostik EntityVariantManager.
        await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, companyId, product.Code, product.Name);
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
    /// ekiyle döngülü sağlanır (Code unique index'i ham DB hatasına düşmesin). Soft-delete filtresi AÇIK: Product
    /// indeksi 2026-08-07'de <c>IsDeleted = 0</c> kazandı → silinmiş ürünün kodu SERBESTTİR (Trendyol ikizi;
    /// gerekçe orada).</summary>
    private async Task<string> BuildUniqueProductCodeAsync(
        Guid companyId, string? sku, long listingId, EtsyImportResultDto report)
    {
        var rawCode = sku is { Length: > 0 } value ? value : $"ETSY-{listingId}";
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

        // Son ek gerekti = aynı kodlu CANLI ürün var → kullanıcıya raporlanır (Trendyol ikizi).
        if (!string.Equals(candidate, baseCode, StringComparison.Ordinal))
        {
            report.Warnings.Add(L["EtsyImport:CodeUniquified", baseCode, candidate].Value);
        }

        return candidate;
    }

    /// <summary>Varyant kodu — sku ?? ÜRÜN KODU (çıplak); benzersizlik YENİ ürünün kendi içinde (bellek-içi küme:
    /// ikinci sku'suz kalem "-2" alır). Eski fallback "{ÜrünKodu}-{index}" idi ve TEK varyantlı üründe bile
    /// "1234-1" üretiyordu (2026-08-07 Hakan bulgusu — "-1" hiçbir üreticide yok artık).</summary>
    private static string BuildUniqueVariantCode(string? sku, string productCode, HashSet<string> usedCodes)
    {
        var rawCode = sku is { Length: > 0 } value ? value : productCode;
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

    /// <summary>Import kod normalizasyonu — Code konvansiyonuyla aynı taban (<c>NormalizeAsCode</c>), üstüne import
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
