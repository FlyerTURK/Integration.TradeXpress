using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Pazaryerinden İÇE AKTARMA dilimi (Trendyol_ProductSync, 2026-07-10/11 kullanıcı kararları) — pazaryerindeki
/// MEVCUT satıcı ürünleri salt GET ile çekilir (READ-ONLY pazaryeri ilkesi: Trendyol'a SIFIR yazma) ve TAM ZİNCİR
/// yazılır: ŞABLON <see cref="Product"/> + varyant(lar) [otomatik üretim — onaylı ara adım YOK] + bağlı
/// <see cref="SalesChannelTrTrendyolProduct"/> grafı (kategori/marka/attribute + Sku + StockItem override + yan-maliyet
/// reçetesi).
///
/// <para><b>İdempotency anahtarları:</b> varyant = BARCODE (filtered unique index <c>(TenantId, Barcode)</c>);
/// kanal kaydı = <c>RemoteProductMainId ?? stockCode/barcode</c> (Skus üzerinden). İkinci import dublike üretmez,
/// yalnız kanal grafını (fiyat/stok override dahil) günceller. Aynı anahtar yorumuyla stockCode PAYLAŞAN uzak
/// gruplar okuma katmanında TEK ürüne birleştirilir (<see cref="MergeGroupsSharingStockCode"/>) — İLK import kardeş
/// varyantların tamamını kurar, kod çakışması son-ekle ("-2", "-3"...) ayrışır.</para>
///
/// <para><b>Minimal-güncelleme kuralı (2026-07-11 netleşen hâli):</b> yerelde ZATEN var olan şablon/varyant ALANLARI
/// GÜNCELLENMEZ (kullanıcı düzenlemiş olabilir) — ama remote'ta olup yerelde OLMAYAN barkodlu kalemler şablona
/// OTOMATİK varyant olarak EKLENİR (eski "Eksik Varyantları Tamamla" ucu import'a gömüldü; ekleme-only, ana varyant
/// değişmez). Uzak fiyat kanal katmanına <see cref="SalesChannelTrTrendyolProductStockItem.OverridePrice"/> olarak
/// yazılır (kullanıcı onaylı yön); uzak STOK ise K12 politikasına tabidir (2026-07-23 kesin karar): çekirdek
/// <c>StockQuantity</c> yalnız İLK kuruluşta (varyant bu importta doğarken) tohumlanır, sonraki importlarda remote
/// stok çekirdeği EZMEZ — fark varsa <see cref="SalesChannelTrTrendyolProductStockItem.OverrideStock"/>'a yazılır
/// (kanal gerçeği) + LogWarning + rapor sayacı; çekirdekle AYNIYSA override null kalır (gürültü üretilmez).</para>
/// </summary>
public partial class SalesChannelTrTrendyolProductAppService
{
    /// <summary>Uzak kayıtta MARKA id'si hiç yoksa yazılan sentinel — entity BrandId zorunlu (min 1); "0" Trendyol'da
    /// geçersiz id'dir, kullanıcı düzenleyene dek push zaten NumericId geçerli ama onaysız kalır. KATEGORİ için
    /// sentinel KALKTI (Trendyol_CategoryOptional, 2026-07-11): eksik/taşan kategori NULL yazılır
    /// (<see cref="SafeCategoryId"/>) ve UnmatchedCategories raporunda görünür.</summary>
    private const string UnknownExternalId = "0";

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<TrendyolImportResultDto> ImportFromMarketplaceAsync(Guid salesChannelId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);

        // Salt GET: tüm satıcı ürünleri sayfa sayfa çekilir + productMainId'ye göre gruplanır (P1 sarmalayıcı)
        // + stockCode paylaşan gruplar TEK ürüne birleştirilir (kardeş varyant kuruluşu — canlı vaka düzeltmesi).
        var remoteProducts = await FetchRemoteProductsAsync(channel);

        var report = new TrendyolImportResultDto
        {
            TotalFetchedItems = remoteProducts.Sum(p => p.Variants.Count),
            TotalRemoteProducts = remoteProducts.Count,
        };

        var knownCategoryIds = await LoadKnownCategoryIdsAsync();
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync();
        if (tryCurrencyUnitId is null)
        {
            // Trendyol fiyatı HER ZAMAN TRY'dir — TRY çözülemezse fiyatlar para-birimsiz (yerel birim semantiği)
            // yazılır; bu finansal olarak riskli bir fallback → SESSİZ geçilmez, rapora uyarı düşülür.
            report.Warnings.Add(L["TrendyolProduct:Import:TryCurrencyMissing"].Value);
        }

        var sideCostPlan = SideCostPlan.From(channel.SideCosts, resolvedCommissionRate: null, variantOptInEnabled: false);

        // Kanalın mevcut kayıtları — eşleşme anahtarı RemoteProductMainId ?? stockCode/barcode (Skus JSON'u entity
        // ile gelir; import bağlamında bellek-içi tarama yeterli).
        var existingRecords = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == channel.CompanyId && x.SalesChannelId == channel.Id));

        // Tüm uzak barcode'lar TEK seferde yerel varyantlara çözülür (yeni filtered unique index güvencesiyle tekil).
        // Arama TENANT kapsamlı — unique index (TenantId, Barcode) ile AYNI kapsam: başka şirketin sahiplendiği
        // barcode ForeignBarcodes'a düşer ve kalem atlanıp raporlanır (yoksa insert ham unique ihlaliyle patlar).
        var (variantsByBarcode, foreignBarcodes) = await LoadVariantsByBarcodeAsync(
            channel.CompanyId,
            remoteProducts.SelectMany(p => p.Variants.Select(v => v.Barcode)).ToList());

        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatchedCategories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var remote in remoteProducts)
        {
            var validVariants = FilterImportableVariants(remote, seenBarcodes, foreignBarcodes, report.SkippedRows);
            if (validVariants.Count == 0)
            {
                continue;   // grubun tüm kalemleri raporlanarak elendi
            }

            TrackUnmatchedCategory(remote, knownCategoryIds, unmatchedCategories, report);

            var existing = FindExistingChannelRecord(remote, validVariants, existingRecords);
            var product = await ResolveOrCreateTemplateAsync(
                channel, remote, validVariants, existing, variantsByBarcode, tryCurrencyUnitId, report);

            // MEVCUT şablonda karşılığı olmayan barkodlu kalemler şablona OTOMATİK varyant olur (ekleme-only;
            // 2026-07-11 kullanıcı kararı — eski "Eksik Varyantları Tamamla" düğmesi import'a gömüldü).
            await EnsureTemplateVariantsAsync(remote, validVariants, product, variantsByBarcode, tryCurrencyUnitId, report);

            var entity = await UpsertChannelRecordAsync(channel, remote, validVariants, existing, product, variantsByBarcode, report);
            if (existing is null)
            {
                existingRecords.Add(entity);   // aynı import içinde ikinci grup aynı kaydı bulabilsin
            }

            await UpsertStockItemsAsync(entity, product, validVariants, variantsByBarcode, tryCurrencyUnitId, sideCostPlan, report);
        }

        return report;
    }

    // ── Uzak okuma katmanı ──────────────────────────────────────────────────────────────────────────

    /// <summary>Uzak satıcı ürünlerini çeker (salt GET, productMainId gruplu) + STOKKODU PAYLAŞAN grupları TEK ürüne
    /// birleştirir. Gerekçe (canlı vaka "Velvet Ruj", 2026-07-11): satıcı 11 renk kalemini productMainId'siz ama AYNI
    /// stockCode ile listeler → gruplama her rengi ayrı ürün sayar; ilk kalem şablonu kurar, kalan kalemler
    /// <see cref="FindExistingChannelRecord"/>'un stockCode fallback'iyle AYNI kanal kaydına düşer ve "şablonda varyant
    /// yok" diye atlanırdı (12 renk sessizce 1 varyanta düşer). Kanal kaydı eşleşmesi zaten
    /// "RemoteProductMainId ?? stockCode" (kullanıcı kararı) — "aynı stockCode = aynı ürün" yorumu şablon KURULUŞUNA da
    /// uygulanır ki İLK import tüm kardeş varyantları doğursun.</summary>
    private async Task<IReadOnlyList<TrendyolRemoteProduct>> FetchRemoteProductsAsync(SalesChannelTrTrendyol channel)
    {
        var remoteProducts = await _client.GetAllSellerProductsAsync(CredentialsOf(channel));
        return MergeGroupsSharingStockCode(remoteProducts);
    }

    /// <summary>stockCode kesişen uzak grupları birleştirir — ortak alanlar İLK gruptan (GroupByProductMainId ile aynı
    /// ilke), varyantlar geliş sırasıyla eklenir. Kod-çakışan kardeşlerin varyant kodları şablon kuruluşunda
    /// <see cref="BuildUniqueVariantCode"/> son-ekiyle ("-2", "-3"...) ayrışır. Bir grup birden fazla önceki gruba
    /// köprü kuruyorsa İLK eşleşen kazanır (deterministik); zaten eşlenmiş stockCode yeniden eşlenmez.</summary>
    private static IReadOnlyList<TrendyolRemoteProduct> MergeGroupsSharingStockCode(IReadOnlyList<TrendyolRemoteProduct> groups)
    {
        var merged = new List<TrendyolRemoteProduct>();
        var indexByStockCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var stockCodes = group.Variants
                .Where(v => v.StockCode is { Length: > 0 })
                .Select(v => v.StockCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var targetIndex = -1;
            foreach (var stockCode in stockCodes)
            {
                if (indexByStockCode.TryGetValue(stockCode, out var index))
                {
                    targetIndex = index;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                merged.Add(group);
                targetIndex = merged.Count - 1;
            }
            else
            {
                var target = merged[targetIndex];
                merged[targetIndex] = target with { Variants = target.Variants.Concat(group.Variants).ToList() };
            }

            foreach (var stockCode in stockCodes)
            {
                indexByStockCode.TryAdd(stockCode, targetIndex);
            }
        }

        return merged;
    }

    // ── Uzak kalem eleme + rapor ────────────────────────────────────────────────────────────────────

    /// <summary>İçe alınabilir kalemleri süzer: barcode'suz kalem, barcode uzunluk taşması, aynı tenant'taki BAŞKA
    /// şirketin sahiplendiği barcode ve import-içi duplike barcode ATLANIR + raporlanır (sessiz geçilmez).
    /// Atlanan satırlar <paramref name="skippedRows"/>'a yazılır.</summary>
    private List<TrendyolRemoteVariant> FilterImportableVariants(
        TrendyolRemoteProduct remote,
        HashSet<string> seenBarcodes,
        HashSet<string> foreignBarcodes,
        List<TrendyolImportIssueDto> skippedRows)
    {
        var result = new List<TrendyolRemoteVariant>();
        foreach (var variant in remote.Variants)
        {
            if (string.IsNullOrWhiteSpace(variant.Barcode)
                || variant.Barcode.Length > TrendyolProductConsts.BarcodeMaxLength)
            {
                AddSkipped(skippedRows, variant, L["TrendyolProduct:Import:InvalidBarcode"].Value);
                continue;
            }

            // Unique index (TenantId, Barcode) tenant kapsamlıdır — barcode başka şirketin varyantındaysa insert
            // ham DbUpdateException'la TÜM importu düşürür ve o veri düzelmeden import hiç tamamlanamaz → atla+raporla.
            if (foreignBarcodes.Contains(variant.Barcode))
            {
                AddSkipped(skippedRows, variant, L["TrendyolProduct:Import:BarcodeOwnedByOtherCompany"].Value);
                continue;
            }

            if (!seenBarcodes.Add(variant.Barcode))
            {
                AddSkipped(skippedRows, variant, L["TrendyolProduct:Import:DuplicateBarcode"].Value);
                continue;
            }

            result.Add(variant);
        }

        return result;
    }

    /// <summary>Kategori eşleşmesini denetler: uzak kategori yerel Trendyol ağacında YOKSA (ya da hiç gelmemişse)
    /// rapora eklenir — ürün ATLANMAZ, kanal kaydı ham kategori id'siyle yazılır (kullanıcı sonradan eşler).</summary>
    private static void TrackUnmatchedCategory(
        TrendyolRemoteProduct remote, HashSet<string> knownCategoryIds, HashSet<string> unmatched, TrendyolImportResultDto report)
    {
        if (remote.CategoryId is { Length: > 0 } categoryId && knownCategoryIds.Contains(categoryId))
        {
            return;
        }

        var label = $"{remote.CategoryId ?? "?"} — {remote.CategoryName ?? remote.Title}";
        if (unmatched.Add(label))
        {
            report.UnmatchedCategories.Add(label);
        }
    }

    private static void AddSkipped(List<TrendyolImportIssueDto> skippedRows, TrendyolRemoteVariant variant, string reason)
    {
        skippedRows.Add(new TrendyolImportIssueDto
        {
            Barcode = variant.Barcode.Length > 0 ? variant.Barcode : null,   // barcode'suz kalemde StockCode'a düşsün
            StockCode = variant.StockCode,
            Reason = reason,
        });
    }

    // ── Eşleşme (idempotency anahtarları) ───────────────────────────────────────────────────────────

    /// <summary>Mevcut kanal kaydı eşleşmesi — anahtar zinciri: (1) <c>RemoteProductMainId</c> birebir;
    /// (2a) Skus içinde BARCODE kesişimi (tenant genelinde tekil → güvenli anahtar); (2b) stockCode kesişimi son ağ
    /// (kullanıcı kararı: RemoteProductMainId ?? stockCode). stockCode TEKİL DEĞİLDİR (aynı ürünün çoklu
    /// listelemelerinde Sku.StockCode birebir aynı) → adaylar SequenceNo→Id ile DETERMİNİSTİK sıralanır ki eşleşme
    /// DB satır sırasına göre importlar arası flip-flop yapmasın (en düşük sıra = ilk listeleme kazanır).</summary>
    private static SalesChannelTrTrendyolProduct? FindExistingChannelRecord(
        TrendyolRemoteProduct remote,
        List<TrendyolRemoteVariant> variants,
        List<SalesChannelTrTrendyolProduct> existingRecords)
    {
        if (!string.IsNullOrWhiteSpace(remote.ProductMainId))
        {
            var byMainId = existingRecords.FirstOrDefault(r =>
                string.Equals(r.RemoteProductMainId, remote.ProductMainId, StringComparison.Ordinal));
            if (byMainId is not null)
            {
                return byMainId;
            }
        }

        var byBarcode = existingRecords
            .Where(r => r.Skus.Any(s => variants.Any(v =>
                string.Equals(s.Barcode, v.Barcode, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(r => r.SequenceNo)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
        if (byBarcode is not null)
        {
            return byBarcode;
        }

        return existingRecords
            .Where(r => r.Skus.Any(s => variants.Any(v =>
                v.StockCode is { Length: > 0 } stockCode
                && string.Equals(s.StockCode, stockCode, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(r => r.SequenceNo)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }

    /// <summary>Şablon ürünü çözer: mevcut kanal kaydı → onun ürünü; barcode'la eşleşen yerel varyant → onun ürünü;
    /// hiçbiri yoksa YENİ şablon + varyantlar otomatik üretilir (onaylı ara adım YOK — kullanıcı kararı).</summary>
    private async Task<Product> ResolveOrCreateTemplateAsync(
        SalesChannelTrTrendyol channel,
        TrendyolRemoteProduct remote,
        List<TrendyolRemoteVariant> variants,
        SalesChannelTrTrendyolProduct? existing,
        Dictionary<string, EntityVariant> variantsByBarcode,
        Guid? tryCurrencyUnitId,
        TrendyolImportResultDto report)
    {
        if (existing is not null)
        {
            return await GetOwnedProductAsync(existing.ProductId);
        }

        var matched = variants
            .Select(v => variantsByBarcode.TryGetValue(v.Barcode, out var local) ? local : null)
            .FirstOrDefault(local => local is not null);
        if (matched is not null)
        {
            return await GetOwnedProductAsync(matched.EntityId);
        }

        var product = await CreateTemplateProductAsync(channel, remote, variants, variantsByBarcode, tryCurrencyUnitId);
        report.CreatedProducts++;
        return product;
    }

    // ── Şablon Product + varyant üretimi (yalnız İLK import; sonrası dokunulmaz) ────────────────────

    /// <summary>Uzak üründen şablon <see cref="Product"/> üretir: Code stockCode'dan normalize (benzersizlik döngülü),
    /// Name = Trendyol başlığı CASING KORUNARAK (<c>SetName(name, normalizeTitle:false)</c> — TitleCase import'ta
    /// başlığı bozar), Description (şablon sınırına kırpılır), görseller URL-kaynaklı, para birimi TRY. Her uzak kalem
    /// için varyant üretilir (ilk kalem MAIN); ana-varyant değişmezi MERKEZİ kapıdan geçer
    /// (<see cref="EntityVariantManager.EnsureMainVariantAsync"/>).</summary>
    private async Task<Product> CreateTemplateProductAsync(
        SalesChannelTrTrendyol channel,
        TrendyolRemoteProduct remote,
        List<TrendyolRemoteVariant> variants,
        Dictionary<string, EntityVariant> variantsByBarcode,
        Guid? tryCurrencyUnitId)
    {
        var first = variants[0];
        var code = await BuildUniqueProductCodeAsync(channel.CompanyId, first.StockCode ?? first.Barcode);

        // Ad TEK atamayla casing-korumalı yazılır: ctor'a geçici ad olarak KOD verilir (ctor SetName'i TitleCase
        // normalize eder ama hemen ezilir), gerçek başlık bir kez normalizeTitle:false ile set edilir.
        var product = new Product(channel.CompanyId, code, code);
        product.SetName(BuildSafeName(remote.Title, code), normalizeTitle: false);
        product.SetDescription(BuildTemplateDescription(remote.Description));
        product.SetCurrencyUnit(tryCurrencyUnitId);
        product.SetImages(await _imageDownloader.BuildFromUrlsAsync(code, remote.ImageUrls));
        await _productRepository.InsertAsync(product, autoSave: true);

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < variants.Count; i++)
        {
            var remoteVariant = variants[i];
            var variantCode = BuildUniqueVariantCode(remoteVariant.StockCode ?? remoteVariant.Barcode, usedCodes);

            // Agnostik EntityVariant — Ad CASE-KORUR (EnsureRequiredText; TitleCase YOK) → gerçek başlık doğrudan ctor'a
            // (ProductVariant'ın temp-ad dansı GEREKMEZ; satıcı casing'i "iPhone 15" korunur). Barkod ayrı (SetBarcode).
            var variant = new EntityVariant(
                channel.CompanyId,
                ProductEntityName,
                product.Id,
                variantCode,
                BuildSafeName(remote.Title, variantCode),
                isMain: i == 0);
            variant.SetBarcode(remoteVariant.Barcode);
            variant.SetStock(Math.Max(0, remoteVariant.Quantity));
            await _variantRepository.InsertAsync(variant, autoSave: true);

            // Satış fiyatı Product uzantısında (ProductVariantDetail; EntityVariantId ile bağlı). Negatif uzak fiyat
            // guard'la süzülür — pazaryerinden gelen tek anomali kalem (SetSalePrice fail-fast) TÜM importu düşürmesin.
            var salePrice = remoteVariant.SalePrice is >= 0 ? remoteVariant.SalePrice : null;
            var detail = new ProductVariantDetail(channel.CompanyId, variant.Id);
            detail.SetSalePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
            await _variantDetailRepository.InsertAsync(detail, autoSave: true);

            variantsByBarcode[remoteVariant.Barcode] = variant;
        }

        // Ana-varyant değişmezi merkezi kapıdan (tekil main garanti; idempotent) — agnostik EntityVariantManager.
        await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId);
        return product;
    }

    /// <summary>Uzakta olup YEREL şablonda karşılığı OLMAYAN barkodlu kalemleri şablona varyant olarak EKLER
    /// (2026-07-11 kullanıcı kararı: eski "Eksik Varyantları Tamamla" düğmesi geçici çözümdü — davranış import'a
    /// gömüldü). Minimal-güncelleme kuralının kalan kısmı: mevcut hiçbir varyant/şablon ALANI GÜNCELLENMEZ, yalnız
    /// varyant EKLENİR; ANA VARYANT DEĞİŞMEZ (yeni eklenen main doğmaz; tekil-main değişmezi merkezî kapıdan —
    /// <see cref="EntityVariantManager.EnsureMainVariantAsync"/>). Barkodu BAŞKA şablonun varyantında kayıtlı kalem
    /// eklenemez (atla+raporla — unique index (TenantId, Barcode) zaten reddederdi). Kod çakışması son-ekle
    /// ("-2", "-3"...) çözülür. İDEMPOTENT: ikinci geçiş 0 ekler. Eklenenler rapora sayı+barkod olarak düşer.</summary>
    private async Task EnsureTemplateVariantsAsync(
        TrendyolRemoteProduct remote,
        List<TrendyolRemoteVariant> variants,
        Product product,
        Dictionary<string, EntityVariant> variantsByBarcode,
        Guid? tryCurrencyUnitId,
        TrendyolImportResultDto report)
    {
        HashSet<string>? usedCodes = null;   // tembel — grubun eksik kalemi yoksa kod sorgusu hiç atılmaz
        var addedAny = false;

        foreach (var remoteVariant in variants)
        {
            if (variantsByBarcode.TryGetValue(remoteVariant.Barcode, out var localVariant))
            {
                if (localVariant.EntityId != product.Id)
                {
                    AddSkipped(report.SkippedRows, remoteVariant, L["TrendyolProduct:Import:BarcodeOnAnotherTemplate"].Value);
                }

                continue;   // varyant zaten var → idempotent no-op (mevcut alanlara DOKUNULMAZ)
            }

            usedCodes ??= await LoadVariantCodesAsync(product.Id);
            var variantCode = BuildUniqueVariantCode(remoteVariant.StockCode ?? remoteVariant.Barcode, usedCodes);

            // Kuruluş importuyla AYNI desen (agnostik EntityVariant; Ad CASE-KORUR): gerçek başlık doğrudan ctor'a,
            // barkod ayrı (SetBarcode); yeni eklenen ASLA main doğmaz (kırmızı çizgi). Satış fiyatı ProductVariantDetail'e.
            var variant = new EntityVariant(product.CompanyId, ProductEntityName, product.Id, variantCode,
                BuildSafeName(remote.Title, variantCode), isMain: false);
            variant.SetBarcode(remoteVariant.Barcode);
            variant.SetStock(Math.Max(0, remoteVariant.Quantity));
            await _variantRepository.InsertAsync(variant, autoSave: true);

            var salePrice = remoteVariant.SalePrice is >= 0 ? remoteVariant.SalePrice : null;
            var detail = new ProductVariantDetail(product.CompanyId, variant.Id);
            detail.SetSalePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
            await _variantDetailRepository.InsertAsync(detail, autoSave: true);

            variantsByBarcode[remoteVariant.Barcode] = variant;

            addedAny = true;
            report.AddedVariants++;
            report.AddedBarcodes.Add(remoteVariant.Barcode);
        }

        if (addedAny)
        {
            // Ana-varyant değişmezi MERKEZÎ kapıdan (idempotent): mevcut main KORUNUR — yeni eklenenler main OLMAZ.
            await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId);
        }
    }

    /// <summary>Ürünün MEVCUT varyant kodları — soft-delete filtresi KAPALI okunur: unique index
    /// <c>(TenantId, ProductId, Code)</c> IsDeleted filtresizdir, silinmiş satır kodu hâlâ işgal eder
    /// (<see cref="BuildUniqueProductCodeAsync"/> ile aynı bilinçli simetri).</summary>
    private async Task<HashSet<string>> LoadVariantCodesAsync(Guid productId)
    {
        using (DataFilter.Disable<ISoftDelete>())
        {
            var codes = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId)
                    .Select(v => v.Code));
            return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Şablon kodu: stockCode/barcode normalize edilir (UPPER + tek boşluk), kısa ise "TY-" ön eki, uzun ise
    /// kırpılır; şirket içinde benzersizlik "-2/-3..." son ekiyle döngülü sağlanır (Code unique index'i ham DB hatasına
    /// düşmesin).</summary>
    private async Task<string> BuildUniqueProductCodeAsync(Guid companyId, string rawCode)
    {
        // Soft-delete filtresi KAPALI sorgulanır — Product unique index'i (TenantId, CompanyId, Code) IsDeleted
        // filtresizdir: silinmiş satır kodu hâlâ işgal eder; filtre açık kalsa sonda "boş" der, insert ham DB unique
        // hatasıyla patlar (NextSequenceNoAsync ile aynı bilinçli simetri).
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

    /// <summary>Varyant kodu — aynı normalize; benzersizlik YENİ ürünün kendi içinde (bellek-içi küme; ürün taze,
    /// DB'de varyantı yok).</summary>
    private static string BuildUniqueVariantCode(string rawCode, HashSet<string> usedCodes)
    {
        var baseCode = NormalizeImportCode(rawCode, ProductConsts.CodeMaxLength);
        var candidate = baseCode;
        var suffix = 2;
        while (!usedCodes.Add(candidate))
        {
            var suffixText = $"-{suffix}";
            candidate = Truncate(baseCode, ProductConsts.CodeMaxLength - suffixText.Length) + suffixText;
            suffix++;
        }

        return candidate;
    }

    /// <summary>Import kod normalizasyonu — Code konvansiyonuyla aynı çekirdek (<c>NormalizeAsCode</c>: Trim + tek
    /// boşluk + UPPER-invariant), üstüne import dayanıklılığı: boş → "TY", kısa (&lt;3) → "TY-" ön eki, uzun → kırp.
    /// Fail-fast yerine onarım BİLİNÇLİ: uzak veri bizim kontrolümüzde değil, kalem kaybetmek daha kötü.</summary>
    private static string NormalizeImportCode(string rawCode, int maxLength)
    {
        var normalized = rawCode.NormalizeAsCode();
        if (normalized.Length == 0)
        {
            normalized = "TY";
        }

        if (normalized.Length < EntityFieldConsts.CodeMinLength)
        {
            normalized = $"TY-{normalized}";
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

    /// <summary>Şablon açıklaması: kanal Description'ı TAM saklar (30.000), şablon 4.000 ile sınırlı → kırpılır;
    /// min sınırın (10) altındaysa null (opsiyonel alan zorlanmaz).</summary>
    private static string? BuildTemplateDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < EntityFieldConsts.DescriptionMinLength)
        {
            return null;
        }

        return Truncate(trimmed, ProductConsts.DescriptionMaxLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    // ── Kanal kaydı upsert ──────────────────────────────────────────────────────────────────────────

    /// <summary>Kanal ürününü upsert eder: yeni kayıtta bizim ProductMainId'imiz üretilir ("{Kod}-{Sıra}", frozen);
    /// her geçişte kategori/marka/KDV/desi/açıklama/teslimat/attribute + uzak görüntü alanları
    /// (RemoteProductMainId/RemoteApproved/RemoteOnSale/ListPrice) tazelenir ve eşleşen yerel varyantların Sku
    /// kimlikleri (barcode remote'tan, FROZEN) işlenir. Eksik varyant bu noktada kalmaz —
    /// <see cref="EnsureTemplateVariantsAsync"/> önceden ekledi (2026-07-11).</summary>
    private async Task<SalesChannelTrTrendyolProduct> UpsertChannelRecordAsync(
        SalesChannelTrTrendyol channel,
        TrendyolRemoteProduct remote,
        List<TrendyolRemoteVariant> variants,
        SalesChannelTrTrendyolProduct? existing,
        Product product,
        Dictionary<string, EntityVariant> variantsByBarcode,
        TrendyolImportResultDto report)
    {
        var entity = existing;
        if (entity is null)
        {
            var sequenceNo = await NextSequenceNoAsync(channel.Id, product.Id);
            entity = new SalesChannelTrTrendyolProduct(
                channel.CompanyId,
                channel.Id,
                product.Id,
                BuildProductMainId(product.Code, sequenceNo),
                sequenceNo,
                SafeCategoryId(remote.CategoryId),
                SafeExternalId(remote.BrandId, TrendyolProductConsts.BrandIdMaxLength));
        }

        entity.SetCategory(
            SafeCategoryId(remote.CategoryId),
            TruncateOptional(remote.CategoryName, TrendyolProductConsts.CategoryNameMaxLength));
        entity.SetBrand(
            SafeExternalId(remote.BrandId, TrendyolProductConsts.BrandIdMaxLength),
            TruncateOptional(remote.BrandName, TrendyolProductConsts.BrandNameMaxLength));
        if (remote.VatRate is >= 0 and <= 100)
        {
            entity.SetVatRate(remote.VatRate.Value);
        }

        if (remote.DimensionalWeight is >= 0)
        {
            entity.SetDimensionalWeight(remote.DimensionalWeight);
        }

        entity.SetDescription(TruncateOptional(remote.Description, TrendyolProductConsts.DescriptionMaxLength));

        // Teslimat: uzak süre geçerliyse yazılır; hızlı-teslimat tipi YEREL karardır — süre 1 kaldıkça korunur.
        if (remote.DeliveryDuration is >= 1)
        {
            entity.SetDeliveryOption(
                remote.DeliveryDuration,
                remote.DeliveryDuration == 1 ? entity.FastDeliveryType : null);
        }

        // Kategori attribute'ları — grubun İLK kaleminden (Trendyol listing yanıtında kalem-başına gelir;
        // ürün-seviyesi tektir). SetAttributes id'siz öğeleri zaten eler.
        var first = variants[0];
        entity.SetAttributes(first.Attributes.Select(a => new SalesChannelTrTrendyolProductCategoryAttribute(
            a.AttributeId,
            a.AttributeValueId,
            TruncateOptional(a.CustomValue ?? (a.AttributeValueId is null ? a.AttributeValue : null), TrendyolProductConsts.CustomAttributeValueMaxLength))));

        entity.ApplyRemoteSnapshot(
            remote.ProductMainId,
            AggregateFlag(variants.Select(v => v.Approved)),
            AggregateFlag(variants.Select(v => v.OnSale)),
            first.ListPrice is >= 0 ? first.ListPrice : null);

        // Sku kimlikleri: yalnız YEREL varyantı çözülen kalemler (barcode remote'tan gelir, FROZEN — yerel
        // "{Kod}-{Sıra}" üretimi bu satırlara uygulanmaz). Eksik varyant burada artık OLAMAZ (EnsureTemplateVariants
        // önceden ekledi); çözülemeyen tek durum barkodu BAŞKA şablonun varyantında kayıtlı kalemdir — o da orada
        // zaten raporlandı (çift rapor üretme).
        foreach (var remoteVariant in variants)
        {
            if (!variantsByBarcode.TryGetValue(remoteVariant.Barcode, out var localVariant)
                || localVariant.EntityId != product.Id)
            {
                continue;   // başka şablonun barkodu — EnsureTemplateVariantsAsync raporladı
            }

            // stockCode uzunluk emniyeti: 100'ü aşan uzak kod kırpılır (entity guard fail-fast'i tek anomali
            // kalemle TÜM importu düşürmesin — NormalizeImportCode'daki bilinçli onarım felsefesiyle aynı).
            var stockCode = remoteVariant.StockCode is { Length: > 0 } rawStockCode
                ? Truncate(rawStockCode, TrendyolProductConsts.StockCodeMaxLength)
                : remoteVariant.Barcode;
            entity.UpsertImportedSku(
                localVariant.Id,
                remoteVariant.Barcode,
                stockCode,
                remoteVariant.ProductContentId);
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

        // K3 write-through: import'un getirdiği gerçek marka (id+ad) da cache'e düşer — picker açılış listesi
        // tenant'ın fiilen kullandığı markalarla dolar. Sentinel ("0") / adsız kayıt manager'da zaten elenir.
        await _brandCacheManager.UpsertAsync(entity.BrandId, entity.BrandName);

        return entity;
    }

    /// <summary>Uzak MARKA id emniyeti: boş → sentinel; entity üst sınırını aşan id de sentinel'e düşer
    /// (SetBrand fail-fast guard'ı tek anomali kalemle TÜM importu düşürmesin — kullanıcı sonradan eşler).</summary>
    private static string SafeExternalId(string? id, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > maxLength)
        {
            return UnknownExternalId;
        }

        return id;
    }

    /// <summary>Uzak KATEGORİ id emniyeti — kategori OPSİYONEL (Trendyol_CategoryOptional, 2026-07-11): boş ya da
    /// üst sınırı aşan id NULL yazılır (sentinel "0" kalktı); satır UnmatchedCategories raporunda zaten görünür,
    /// kullanıcı kategoriyi sonradan seçer. Yerel ağaçta eşleşmeyen ama GEÇERLİ uzak id HAM yazılmaya devam eder.</summary>
    private static string? SafeCategoryId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > TrendyolProductConsts.CategoryIdMaxLength)
        {
            return null;
        }

        return id;
    }

    private static string? TruncateOptional(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }

    /// <summary>Kalem bayraklarını ürün-seviyesine indirger: hepsi true → true; en az biri false → false;
    /// hiçbiri bilinmiyor → null.</summary>
    private static bool? AggregateFlag(IEnumerable<bool?> flags)
    {
        var list = flags.ToList();
        if (list.Any(f => f == false))
        {
            return false;
        }

        return list.Count > 0 && list.All(f => f == true) ? true : null;
    }

    // ── StockItem (fiyat/stok override) + yan-maliyet reçetesi ──────────────────────────────────────

    /// <summary>Uzak fiyat/stok kanal override katmanına yazılır (kullanıcı onaylı yön): varyant-başına başlık
    /// upsert edilir; YENİ başlıkta kanal gider satırları da kurulur (<see cref="SideCostRecipeComposer.EnsureLines"/> —
    /// mevcut klon yollarıyla tutarlı). Mevcut başlıkta reçeteye DOKUNULMAZ (kullanıcı emeği), yalnız override tazelenir.
    /// <b>Stok — K12 politikası (2026-07-23 kesin karar):</b> çekirdek <c>StockQuantity</c> yalnız varyant BU importta
    /// doğarken tohumlanır (create yolu — <see cref="CreateTemplateProductAsync"/>/<see cref="EnsureTemplateVariantsAsync"/>);
    /// burada remote stok çekirdeği ASLA EZMEZ. Çekirdek == remote → override null (fark yok, gürültü üretme);
    /// farklıysa remote değer <see cref="SalesChannelTrTrendyolProductStockItem.OverrideStock"/> olur (kanal gerçeği)
    /// + fark görünür kılınır (<see cref="ResolveOverrideStock"/>: satır-bazında LogWarning + rapor sayacı).</summary>
    private async Task UpsertStockItemsAsync(
        SalesChannelTrTrendyolProduct entity,
        Product product,
        List<TrendyolRemoteVariant> variants,
        Dictionary<string, EntityVariant> variantsByBarcode,
        Guid? tryCurrencyUnitId,
        SideCostPlan sideCostPlan,
        TrendyolImportResultDto report)
    {
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrTrendyolProductId == entity.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        foreach (var remoteVariant in variants)
        {
            if (!variantsByBarcode.TryGetValue(remoteVariant.Barcode, out var localVariant)
                || localVariant.EntityId != entity.ProductId)
            {
                continue;   // yerel varyant yok — Sku aşamasında zaten raporlandı
            }

            var salePrice = remoteVariant.SalePrice is >= 0 ? remoteVariant.SalePrice : null;
            var overrideStock = ResolveOverrideStock(product, localVariant, remoteVariant.Quantity, report);

            if (headers.TryGetValue(localVariant.Id, out var header))
            {
                // TASARIM (kullanıcı onaylı yön — SalesChannelTrTrendyolProductImportTests'te pinli): ÇEKİRDEK asla
                // ezilmez (ürün adı, ProductVariantDetail fiyatı korunur), uzak gerçek KANAL katmanına yazılır.
                // OverridePrice/OverrideStock kullanıcının rezerv alanı DEĞİL, pazaryerinin AYNASIDIR → her import'ta
                // remote değerle tazelenir.
                header.SetOverridePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
                header.SetOverrideStock(overrideStock);
                await _stockItemRepository.UpdateAsync(header, autoSave: true);
                continue;
            }

            header = new SalesChannelTrTrendyolProductStockItem(entity.CompanyId, entity.Id, localVariant.Id);
            header.SetOverridePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
            header.SetOverrideStock(overrideStock);
            await _stockItemRepository.InsertAsync(header, autoSave: true);
            headers[localVariant.Id] = header;

            // Yan-maliyet satırları yalnız YENİ başlıkta kurulur (klon yollarıyla aynı yaşam anı) — persist
            // MEVCUT merkezi mekanikle (SaveChannelRecipeLinesAsync; paralel kayıt yolu YOK).
            var recipeLines = new List<ProductRecipeLineGraphDto>();
            if (SideCostRecipeComposer.EnsureLines(recipeLines, sideCostPlan))
            {
                await SaveChannelRecipeLinesAsync(entity, header.Id, recipeLines);
            }
        }
    }

    /// <summary>K12 stok politikasının karar noktası: remote stok (negatif → 0 clamp'li) çekirdek
    /// <see cref="EntityVariant.StockQuantity"/> ile AYNIYSA null döner (override yazılmaz — "fark yok" gürültüsüz);
    /// FARKLIYSA remote değer döner (kanal override'ı olur) + fark satır-bazında LogWarning + rapor sayacıyla
    /// görünür kılınır (sessiz geçilmez). BU importta doğan varyantın çekirdeği remote'la tohumlandığından
    /// (create yolu) doğal olarak "fark yok" dalına düşer — create/update ayrımı için zaman karşılaştırması GEREKMEZ.</summary>
    private int? ResolveOverrideStock(Product product, EntityVariant localVariant, int remoteQuantity, TrendyolImportResultDto report)
    {
        var remoteStock = Math.Max(0, remoteQuantity);
        if (remoteStock == localVariant.StockQuantity)
        {
            return null;
        }

        report.StockDifferenceCount++;
        Logger.LogWarning(
            "Trendyol import stok farkı: ürün {ProductCode} / varyant {VariantCode} — çekirdek {CoreStock}, remote {RemoteStock}. Çekirdek EZİLMEDİ; remote değer kanal OverrideStock'una yazıldı.",
            product.Code,
            localVariant.Code,
            localVariant.StockQuantity,
            remoteStock);
        return remoteStock;
    }

    // ── Yardımcı yüklemeler ─────────────────────────────────────────────────────────────────────────

    /// <summary>Yerel Trendyol kategori ağacının ExternalId kümesi — HOST-GLOBAL tablo (kategori sync deseniyle aynı
    /// Change(null) sabitlemesi). Eşleşme yalnız RAPOR içindir; kanal kaydına ham uzak id her koşulda yazılır.</summary>
    private async Task<HashSet<string>> LoadKnownCategoryIdsAsync()
    {
        using (CurrentTenant.Change(null))
        {
            var ids = await AsyncExecuter.ToListAsync(
                (await _trendyolCategoryRepository.GetQueryableAsync()).Select(c => c.ExternalId));
            return ids.ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>TRY para birimi (pivot) id'si — şablon CurrencyUnit + override fiyat birimi için. Bulunamazsa null
    /// (yerel birim varsayılır; import kırılmaz).</summary>
    private async Task<Guid?> ResolveTryCurrencyUnitIdAsync()
    {
        // TRY tipik kurulumda HOST kaydıdır (CurrencyUnit host‖tenant çapraz katalog) — tenant data-filter'ı
        // host satırını gizleyince fiyatlar para-birimsiz düşüyordu (canlıda yaşandı, 2026-07-11). Maden
        // kataloğu deseniyle filtre KAPALI okunur; tenant kendi TRY'sini tanımlamışsa o tercih edilir.
        using (DataFilter.Disable<IMultiTenant>())
        {
            var candidates = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(c => c.Code == CurrencyUnitCode.TRY));
            var preferred = candidates.FirstOrDefault(c => c.TenantId == CurrentTenant.Id)
                            ?? candidates.FirstOrDefault(c => c.TenantId == null);
            return preferred?.Id;
        }
    }

    /// <summary>Uzak barcode'ları yerel varyantlara TEK geçişte çözer (parça parça IN sorgusu). Arama kapsamı
    /// unique index (TenantId, Barcode) ile AYNI: TENANT-scoped (tenant data-filter zaten uygular) — şirket filtresi
    /// KONMAZ, yoksa başka şirketin sahiplendiği barcode görünmez kalır ve insert ham unique ihlaliyle TÜM importu
    /// düşürür. Kanalın şirketine ait varyantlar sözlüğe, diğer şirketlerinkiler ForeignBarcodes kümesine ayrışır
    /// (kalem atla+raporla için). Barcode başına en çok BİR varyant döner (index güvencesi).</summary>
    private async Task<(Dictionary<string, EntityVariant> OwnedByBarcode, HashSet<string> ForeignBarcodes)> LoadVariantsByBarcodeAsync(
        Guid companyId, List<string> barcodes)
    {
        var owned = new Dictionary<string, EntityVariant>(StringComparer.OrdinalIgnoreCase);
        var foreign = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = barcodes
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Company görünürlük filtresi (IDataFilter<ICompanyScoped>) KAPALI: agnostik varyant tablosu tenant kapsamlı
        // olduğundan diğer şirketlerin varyantları da görünmeli ki çakışan barcode atla+raporla ile yakalanabilsin.
        // EntityName=="Product" ZORUNLU: agnostik tablo TÜM entity'lerin (Good/Metal/…) varyantlarını tutar; eski
        // ProductVariant filtered-unique-index'i (TenantId, Barcode) yalnız ürün varyantlarını kapsıyordu → aynı
        // barkod-tekilliği/import-idempotency kapsamını EntityName filtresiyle koru (ürün varyantına daralt).
        using (DataFilter.Disable<ICompanyScoped>())
        {
            const int chunkSize = 500;
            foreach (var chunk in distinct.Chunk(chunkSize))
            {
                var variants = await AsyncExecuter.ToListAsync(
                    (await _variantRepository.GetQueryableAsync())
                        .Where(v => v.EntityName == ProductEntityName && v.Barcode != null && chunk.Contains(v.Barcode)));
                foreach (var variant in variants)
                {
                    if (variant.CompanyId == companyId)
                    {
                        owned[variant.Barcode!] = variant;
                    }
                    else
                    {
                        foreign.Add(variant.Barcode!);
                    }
                }
            }
        }

        return (owned, foreign);
    }
}
