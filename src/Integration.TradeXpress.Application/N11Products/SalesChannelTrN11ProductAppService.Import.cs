using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Progress;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products.Rest;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 mağazasından İÇE AKTARMA dilimi — <b>salt GET</b> (<c>/ms/product-query</c>; N11'e SIFIR yazma). Mevcut
/// mağazadaki ürünler için TAM ZİNCİR kurulur: şablon <see cref="Product"/> + varyantlar + bağlı
/// <see cref="SalesChannelTrN11Product"/> kaydı (SKU kimlikleri + fiyat/stok override + yan-maliyet reçetesi).
///
/// <para><b>Neden gerekti:</b> Trendyol ve Etsy'de içe aktarım VARDI, N11'de YOKTU — mevcut N11 mağazası olan
/// satıcı her ürününü tek tek elle girmek zorundaydı.</para>
///
/// <para><b>REST'in yapısal farkı gruplamayı belirliyor:</b> SOAP'ta tek ürün + içinde stockItems vardı; REST'te
/// her SKU BAĞIMSIZ bir satır ve varyantlığı yalnız ortak <c>productMainId</c> kuruyor. Bu yüzden düz satır listesi
/// önce <c>productMainId</c>'ye göre gruplanır; <c>productMainId</c>'si olmayan satır kendi başına bir üründür.</para>
///
/// <para><b>İdempotency anahtar zinciri:</b> (1) kanal kaydı <see cref="SalesChannelTrN11Product.SellerCode"/> =
/// uzak <c>productMainId</c> — bu ikisi AYNI kavramdır (push'ta <c>productMainId</c> olarak bizim SellerCode'umuzu
/// göndeririz), o yüzden kendi push ettiğimiz ürünü geri okurken kayıt birebir bulunur; (2) SKU stok kodu kesişimi.
/// İkinci içe aktarım kayıt ÇOĞALTMAZ.</para>
///
/// <para><b>Uzak stok kodu DONDURULUR</b> (<see cref="SalesChannelTrN11Product.UpsertImportedSku"/>): kod yeniden
/// üretilseydi ("{VaryantKodu}-{SequenceNo}") sonraki push var olan SKU'yu güncellemek yerine İKİNCİ bir SKU
/// açardı — mağazada aynı ürün iki kez listelenirdi.</para>
///
/// <para><b>Bilinen sınır — yanıt DAR:</b> product-query KDV, kargo şablonu, hazırlık süresi, maksimum alım,
/// ürün durumu (yeni/yenilenmiş) ve NİTELİKLERİ döndürmez. Bu alanlar uydurulmaz: KDV boş bırakılır (kullanıcı
/// seçer — yanlış oran yanlış fatura demektir), kargo şablonu kanalın şablonlarından çözülür, kalanlar entity
/// varsayılanında kalır. Her biri rapora uyarı olarak düşer.</para>
///
/// <para><b>REÇETEYE HİÇBİR SATIR YAZILMAZ</b> (2026-08-04 Hakan kararı) — ne maddi satır ne yan-maliyet.
/// Unutulmuş değil, BİLİNÇLİ:
/// <list type="bullet">
///   <item><b>Maddi satır:</b> N11 ürünün neyden yapıldığını söylemez. Uydurulan bir emtia bağı yanlış maliyet
///   demektir; reçeteyi kullanıcı kurar (hazırdaki emtiayı seçerek ya da üründen yeni emtia oluşturarak).</item>
///   <item><b>Yan-maliyet (paketleme/kargo/komisyon):</b> bunlar HİZMET satırıdır — kendinden önceki satırların
///   üstüne yüzde/brütleştirme uygulayan TÜREV bedeller. Maddi satır yokken tabanları BOŞ olur: komisyon
///   brütleştirmesi sıfırın üstünden hesaplanır ve anlamsız bir fiyat üretir. Bunun bugüne dek görünmemesinin
///   tek sebebi fiyat zincirinde <c>OverridePrice</c>'ın (N11'deki gerçek fiyat) kazanmasıydı — yani orada
///   sessizce yanlış bir hesap duruyordu. Kullanıcı reçeteyi kurduğunda yan maliyetler
///   <c>ReapplySideCostsAsync</c> ile eklenir.</item>
/// </list>
/// <b>Trendyol içe aktarımı hâlâ yan-maliyet satırı yazıyor</b> — sevk edilmiş davranış, ayrı karar; bilinçli
/// bir ayrım (hizalanacaksa iki taraf birlikte ele alınmalı).</para>
/// </summary>
public partial class SalesChannelTrN11ProductAppService
{
    /// <summary>Kanalda hiç kargo şablonu tanımlı değilse yazılan yer tutucu. Entity <c>ShipmentTemplateName</c>'i
    /// zorunlu kılar (min 1) ama uzak yanıt bu alanı taşımaz — boş bırakma seçeneği yok. Push denenirse N11 bunu
    /// "şablon bulunamadı" ile reddeder; sessiz yanlış şablondan İYİDİR ve rapora uyarı düşer.</summary>
    private const string ImportedShipmentTemplatePlaceholder = "?";

    /// <summary>Sayfa boyutu — istemci zaten dokümanın 250 tavanına kırpar; burada tavanı açıkça istiyoruz ki
    /// büyük mağaza en az istekle çekilsin.</summary>
    private const int ImportPageSize = 250;

    /// <summary>İlerleme kanalı — ambient scoped sink (Trendyol import'uyla aynı gerekçe: ctor'a eklenmez, aynı
    /// scope'tan çözülür; dinleyen yoksa rapor kaybolur).</summary>
    private IOperationProgressSink Progress => LazyServiceProvider.LazyGetRequiredService<IOperationProgressSink>();

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<N11ImportResultDto> ImportFromMarketplaceAsync(Guid salesChannelId, int? defaultVatRate = null)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);

        try
        {
            return await ImportCoreAsync(channel, defaultVatRate);
        }
        finally
        {
            Progress.Complete();
        }
    }

    private async Task<N11ImportResultDto> ImportCoreAsync(SalesChannelTrN11 channel, int? defaultVatRate)
    {
        // ÇEKİM fazı: tek çağrı, sayfa geri bildirimi yok → belirsiz çubuk (kullanıcı "çalışıyor" görsün).
        Progress.Report(new OperationProgress(L["N11Product:Import:Phase:Fetching"].Value, 0, null));

        // Salt GET — filtresiz: hiçbir parametre zorunlu değil, boş filtre satıcının TÜM ürünlerini sayfalar.
        var rows = await _queryClient.QueryAllAsync(
            new N11ProductQueryFilter(
                Page: 0,
                Size: ImportPageSize,
                StockCode: null,
                SaleStatus: null,
                ProductStatus: null,
                BrandName: null,
                CategoryIds: null),
            channel.AppKey,
            channel.AppSecret);

        var report = new N11ImportResultDto { TotalFetchedItems = rows.Count };

        var usableRows = FilterImportableRows(rows, report);
        var groups = GroupByProductMainId(usableRows);
        report.TotalRemoteProducts = groups.Count;
        if (groups.Count == 0)
        {
            return report;
        }

        var tryCurrencyUnitId = await _marketplaceCurrency.ResolveTryUnitIdAsync();
        if (tryCurrencyUnitId is null)
        {
            // N11 fiyatı HER ZAMAN TRY'dir — çözülemezse fiyatlar para-birimsiz (yerel birim semantiği) yazılır.
            // Finansal olarak riskli bir düşüş → SESSİZ geçilmez.
            report.Warnings.Add(L["N11Product:Import:TryCurrencyMissing"].Value);
        }

        var shipmentTemplateName = await ResolveImportShipmentTemplateAsync(channel.Id, report);
        var categoryNames = await LoadN11CategoryNamesAsync();

        // KDV uzak yanıtta YOK. Kullanıcı sihirbazda bir oran SEÇTİYSE yeni kayıtlara o damgalanır; seçmediyse
        // alan BOŞ kalır (kıymetli maden %0 + istisna faturası, işçilik %20 — oran ürüne göre değişir, uydurulmaz)
        // ve push fail-fast reddeder → uyarı yalnız o durumda anlamlı.
        if (defaultVatRate is null)
        {
            report.Warnings.Add(L["N11Product:Import:VatRateNotProvided"].Value);
        }

        var existingRecords = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == channel.CompanyId && x.SalesChannelId == channel.Id));

        var unmatchedCategories = new HashSet<string>(StringComparer.Ordinal);

        var processed = 0;
        foreach (var group in groups)
        {
            // İŞLEME fazı: ürün başına ilerleme + o anki ürünün adı.
            Progress.Report(new OperationProgress(
                L["N11Product:Import:Phase:Processing"].Value, ++processed, groups.Count, group.Title));

            if (group.CategoryExternalId is not { Length: > 0 } categoryExternalId)
            {
                // Kategori entity'de ZORUNLU (yaprak kategori olmadan listeleme kurulamaz) → grup atlanır.
                AddSkipped(report, group.Rows, L["N11Product:Import:MissingCategory"].Value);
                continue;
            }

            TrackUnmatchedCategory(group, categoryNames, unmatchedCategories, report);

            var existing = await DiscardOrphanedRecordAsync(
                FindExistingChannelRecord(group, existingRecords), existingRecords, report);

            var (product, variantsByStockCode) = await ResolveTemplateAsync(
                channel, group, existing, tryCurrencyUnitId, categoryNames, report);

            await EnsureTemplateVariantsAsync(group, product, variantsByStockCode, tryCurrencyUnitId, report);

            // REST yanıtında görseller SATIR (yani SKU/varyant) başına gelir → varyanta özel görsel varyantın
            // kendi bağlamına iner. Grup-seviyesi (birleşik) görsel seti kayıt geneline yazılmaya devam eder.
            await ImportVariantImagesAsync(product, group, variantsByStockCode, report);

            var entity = await UpsertChannelRecordAsync(
                channel, group, existing, categoryExternalId, shipmentTemplateName, product, variantsByStockCode,
                categoryNames, defaultVatRate, report);
            if (existing is null)
            {
                existingRecords.Add(entity);   // aynı içe aktarımda ikinci grup aynı kaydı bulabilsin
            }

            await UpsertStockItemsAsync(entity, group, product, variantsByStockCode, tryCurrencyUnitId, report);
        }

        ReportSkippedImages(report);
        return report;
    }

    /// <summary>Görsel sınırına takılıp hiç bağlanmayan pazaryeri görsellerini RAPORA taşır (Trendyol/Etsy ikizi).
    ///
    /// <para><b>Neden gerekli:</b> sınır aşımı indiricide yalnız server-log'a düşüyordu; kullanıcı "içe aktarım
    /// başarılı" raporunu görüp fotoğrafın neden gelmediğini hiçbir ekranda bulamıyordu. Sayı ürün-başı değil
    /// import-başı TEK satırda verilir — 103 ürünlük bir mağazada ürün başına uyarı raporu okunmaz hâle
    /// getirirdi.</para></summary>
    private void ReportSkippedImages(N11ImportResultDto report)
    {
        if (report.SkippedImages > 0)
        {
            report.Warnings.Add(
                L["N11Product:Import:ImagesSkippedForLimit", report.SkippedImages, ProductConsts.MaxImageCount].Value);
        }
    }

    // ── Uzak satır eleme + gruplama ─────────────────────────────────────────────────────────────────

    /// <summary>İçe alınabilir satırları süzer. Stok kodu SATIRIN KİMLİĞİDİR: yoksa ya da entity sınırını aşıyorsa
    /// satır atlanır ve raporlanır — kırpmak burada YANLIŞ olurdu, kırpılmış kod N11'deki gerçek SKU'yu adreslemez
    /// ve sonraki push var olmayan bir SKU'ya giderdi.
    ///
    /// <para>Aynı stok kodu birden çok kez gelirse İLK satır tutulur. Bu bir veri sorunu değil sayfalama artefaktıdır
    /// (istemci uyarısı: sayfalar arası N11 tarafında ürün eklenip çıkarsa aynı SKU iki kez düşebilir) → satır başına
    /// gürültü üretilmez, yalnız toplam sayı uyarı olarak bildirilir.</para></summary>
    private List<N11RestProductSummary> FilterImportableRows(IReadOnlyList<N11RestProductSummary> rows, N11ImportResultDto report)
    {
        var result = new List<N11RestProductSummary>(rows.Count);
        var seenStockCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.StockCode))
            {
                AddSkipped(report, row, L["N11Product:Import:MissingStockCode"].Value);
                continue;
            }

            if (row.StockCode.Length > N11ProductConsts.StockCodeMaxLength)
            {
                AddSkipped(report, row, L["N11Product:Import:StockCodeTooLong"].Value);
                continue;
            }

            if (!seenStockCodes.Add(row.StockCode))
            {
                duplicateCount++;
                continue;
            }

            result.Add(row);
        }

        if (duplicateCount > 0)
        {
            report.Warnings.Add(L["N11Product:Import:DuplicateRowsSkipped", duplicateCount].Value);
        }

        return result;
    }

    /// <summary>Düz SKU satırlarını uzak ÜRÜNLERE gruplar: anahtar <c>productMainId</c>, yoksa satırın kendi stok
    /// kodu (tek varyantlı ürün). Geliş sırası korunur — aynı mağaza iki kez içe aktarıldığında rapor sırası
    /// değişmesin.</summary>
    private static List<N11RemoteProductGroup> GroupByProductMainId(List<N11RestProductSummary> rows)
    {
        var groups = new List<N11RemoteProductGroup>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = string.IsNullOrWhiteSpace(row.ProductMainId) ? row.StockCode : row.ProductMainId!.Trim();
            if (indexByKey.TryGetValue(key, out var index))
            {
                groups[index].Rows.Add(row);
                continue;
            }

            indexByKey[key] = groups.Count;
            groups.Add(new N11RemoteProductGroup(key, new List<N11RestProductSummary> { row }));
        }

        return groups;
    }

    /// <summary>Uzak kategori yerel N11 ağacında YOKSA rapora eklenir — ürün ATLANMAZ, ham uzak id yazılır
    /// (kullanıcı sonradan eşler ya da kategori ağacı sync'lenince kendiliğinden oturur).</summary>
    private static void TrackUnmatchedCategory(
        N11RemoteProductGroup group, Dictionary<string, string> categoryNames, HashSet<string> unmatched, N11ImportResultDto report)
    {
        if (group.CategoryExternalId is { Length: > 0 } categoryId && categoryNames.ContainsKey(categoryId))
        {
            return;
        }

        var label = $"{group.CategoryExternalId ?? "?"} — {group.Title ?? group.Key}";
        if (unmatched.Add(label))
        {
            report.UnmatchedCategories.Add(label);
        }
    }

    private static void AddSkipped(N11ImportResultDto report, N11RestProductSummary row, string reason)
    {
        report.SkippedRows.Add(new N11ImportIssueDto
        {
            StockCode = row.StockCode is { Length: > 0 } code ? code : null,
            Title = row.Title,
            Reason = reason,
        });
    }

    private static void AddSkipped(N11ImportResultDto report, IEnumerable<N11RestProductSummary> rows, string reason)
    {
        foreach (var row in rows)
        {
            AddSkipped(report, row, reason);
        }
    }

    // ── Eşleşme (idempotency) ───────────────────────────────────────────────────────────────────────

    /// <summary>Mevcut kanal kaydı eşleşmesi. (1) <see cref="SalesChannelTrN11Product.SellerCode"/> = uzak
    /// <c>productMainId</c> — kendi push ettiğimiz ürünü geri okurken birebir tutar; (2) SKU stok kodu kesişimi —
    /// mağaza panelinden açılmış, bizim üretmediğimiz <c>productMainId</c>'ler için son ağ.
    ///
    /// <para>Adaylar <c>SequenceNo</c> → <c>Id</c> ile DETERMİNİSTİK sıralanır: aynı ürünün ikinci listelemesi de
    /// olabildiğinden eşleşme DB satır sırasına göre içe aktarımlar arası flip-flop yapmamalı (en düşük sıra =
    /// ilk listeleme kazanır).</para></summary>
    private static SalesChannelTrN11Product? FindExistingChannelRecord(
        N11RemoteProductGroup group, List<SalesChannelTrN11Product> existingRecords)
    {
        var bySellerCode = existingRecords
            .Where(r => string.Equals(r.SellerCode, group.Key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.SequenceNo)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
        if (bySellerCode is not null)
        {
            return bySellerCode;
        }

        var stockCodes = group.Rows.Select(r => r.StockCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existingRecords
            .Where(r => r.Skus.Any(s => stockCodes.Contains(s.SellerStockCode)))
            .OrderBy(r => r.SequenceNo)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }

    // ── Şablon Product + varyantlar ─────────────────────────────────────────────────────────────────

    /// <summary>Şablon ürünü çözer + uzak stok kodu → yerel varyant haritasını kurar.
    ///
    /// <para><b>Bilinçli sınır:</b> mevcut kanal kaydı YOKSA daima YENİ şablon üretilir. Trendyol'da barcode
    /// tenant-genelinde tekil olduğu için "bu kalem başka şablonun varyantı" sorgulanabiliyor; N11 yanıtı barcode
    /// TAŞIMAZ, elimizdeki tek kimlik satıcı stok kodudur ve o da yalnız kanal kaydı üzerinden anlamlı. Uydurma bir
    /// eşleştirme (kod ayrıştırma) yanlış ürüne varyant eklerdi — yeni şablon üretmek geri alınabilir, yanlış
    /// birleştirme değil.</para></summary>
    /// <summary>ÖKSÜZ kanal kaydını eler — Trendyol ikizi
    /// (<c>SalesChannelTrTrendyolProductAppService.Import.DiscardOrphanedRecordAsync</c>; gerekçe ve bedeli orada).
    /// Kısaca: şablon ürünü silinmiş kanal kaydı kullanılamaz durumdadır ve önceden TEK böyle kayıt tüm içe
    /// aktarımı <c>ProductNotFound</c> ile iptal ediyordu.</summary>
    private async Task<SalesChannelTrN11Product?> DiscardOrphanedRecordAsync(
        SalesChannelTrN11Product? existing,
        List<SalesChannelTrN11Product> existingRecords,
        N11ImportResultDto report)
    {
        if (existing is null || await FindOwnedProductAsync(existing.ProductId) is not null)
        {
            return existing;
        }

        // Kullanıcıya RAPORLANMAZ — gerekçe Trendyol ikizinde.
        Logger.LogInformation(
            "N11 içe aktarım: {SellerCode} kanal kaydının şablon ürünü ({ProductId}) silinmiş — kayıt kaldırıldı, ürün mağazadan yeniden kurulacak.",
            existing.SellerCode,
            existing.ProductId);

        await _remover.RemoveGraphAsync(existing);
        existingRecords.Remove(existing);
        return null;
    }

    private async Task<(Product Product, Dictionary<string, EntityVariant> VariantsByStockCode)> ResolveTemplateAsync(
        SalesChannelTrN11 channel,
        N11RemoteProductGroup group,
        SalesChannelTrN11Product? existing,
        Guid? tryCurrencyUnitId,
        Dictionary<string, string> categoryNames,
        N11ImportResultDto report)
    {
        if (existing is not null)
        {
            var product = await GetOwnedProductAsync(existing.ProductId);
            return (product, await LoadVariantsBySkuAsync(existing, product.Id));
        }

        var created = await CreateTemplateProductAsync(channel, group, tryCurrencyUnitId, categoryNames, report);
        report.CreatedProducts++;
        return created;
    }

    /// <summary>Mevcut kanal kaydının SKU satırlarından uzak stok kodu → yerel varyant haritası. Varyantı silinmiş
    /// SKU satırı haritaya girmez (satır SİLİNMEZ — N11'de yaşıyor olabilir), o stok kodu eksik sayılır ve
    /// <see cref="EnsureTemplateVariantsAsync"/> yeniden varyant açar.</summary>
    private async Task<Dictionary<string, EntityVariant>> LoadVariantsBySkuAsync(SalesChannelTrN11Product entity, Guid productId)
    {
        var map = new Dictionary<string, EntityVariant>(StringComparer.OrdinalIgnoreCase);
        var variantIds = entity.Skus.Select(s => s.ProductVariantId).Distinct().ToList();
        if (variantIds.Count == 0)
        {
            return map;
        }

        var variants = (await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId && variantIds.Contains(v.Id))))
            .ToDictionary(v => v.Id);

        foreach (var sku in entity.Skus)
        {
            if (variants.TryGetValue(sku.ProductVariantId, out var variant))
            {
                map[sku.SellerStockCode] = variant;
            }
        }

        return map;
    }

    /// <summary>Uzak gruptan şablon <see cref="Product"/> + varyantlarını üretir. Ad CASING KORUR (pazaryeri
    /// başlığı TitleCase'e sokulursa "iPhone 15 Pro" → "İphone 15 Pro" olur ve doğru başlık geri üretilemez);
    /// kod uzak stok kodundan normalize edilir, şirket içinde benzersizlik son-ekle sağlanır.</summary>
    private async Task<(Product Product, Dictionary<string, EntityVariant> VariantsByStockCode)> CreateTemplateProductAsync(
        SalesChannelTrN11 channel, N11RemoteProductGroup group, Guid? tryCurrencyUnitId,
        Dictionary<string, string> categoryNames, N11ImportResultDto report)
    {
        var first = group.Rows[0];
        var code = await BuildUniqueProductCodeAsync(channel.CompanyId, first.StockCode, report);

        // Ad TEK atamayla casing-korumalı yazılır: ctor'a geçici ad olarak KOD verilir (ctor SetName'i normalize
        // ederdi ama hemen ezilir), gerçek başlık bir kez normalizeTitle:false ile set edilir.
        var product = new Product(channel.CompanyId, code, code);
        product.SetName(BuildSafeName(group.Title, code), normalizeTitle: false);
        product.SetCurrencyUnit(tryCurrencyUnitId);

        // ÜRÜNÜN KENDİ kategorisi kanal kategorisinden çözülür/kurulur (2026-08-06 Hakan kararı) — yalnız YENİ üründe
        // (Trendyol ikizi; mevcut ürünün kategorisi kullanıcı beyanıdır, EZİLMEZ).
        product.SetProductCategory(await _categoryResolver.ResolveOrCreateAsync(
            channel.CompanyId, SalesChannelType.TrN11, group.CategoryExternalId,
            group.CategoryExternalId is { } catId ? categoryNames.GetValueOrDefault(catId) : null));

        await _productRepository.InsertAsync(product, autoSave: true);

        // Görseller DAM'a — link ürün Id'sine bağlandığından INSERT'ten SONRA. YALNIZ kuruluşta: mevcut ürünün
        // kayıt-geneli galerisi KULLANICI BEYANIDIR, içe aktarım onu tazelemez (kategori ve varyant alanlarıyla
        // aynı minimal-güncelleme politikası). İndirici 2026-08-20'den beri EKLEMELİ olduğundan ezme riski
        // kalmadı; buradaki kısıt teknik değil POLİTİKA — kanalda sonradan eklenen görsel bilinçle inmez.
        report.SkippedImages += (await _imageDownloader.ImportToProductAsync(product, group.ImageUrls))
            .SkippedForCapacityCount;

        var map = new Dictionary<string, EntityVariant>(StringComparer.OrdinalIgnoreCase);
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < group.Rows.Count; i++)
        {
            var row = group.Rows[i];
            map[row.StockCode] = await CreateVariantAsync(
                product, row, group.Title, usedCodes, tryCurrencyUnitId, isMain: i == 0);
        }

        // Ana-varyant değişmezi MERKEZÎ EnsureMainVariantAsync'ten (tekil main garanti; idempotent).
        await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId, product.Code, product.Name);
        return (product, map);
    }

    /// <summary>Uzakta olup YEREL şablonda karşılığı OLMAYAN SKU'ları şablona varyant olarak EKLER (ekleme-only:
    /// mevcut varyant ALANLARINA dokunulmaz — kullanıcı düzenlemiş olabilir; ANA VARYANT DEĞİŞMEZ). İDEMPOTENT:
    /// ikinci geçiş 0 ekler.</summary>
    private async Task EnsureTemplateVariantsAsync(
        N11RemoteProductGroup group,
        Product product,
        Dictionary<string, EntityVariant> variantsByStockCode,
        Guid? tryCurrencyUnitId,
        N11ImportResultDto report)
    {
        HashSet<string>? usedCodes = null;   // tembel — grubun eksik kalemi yoksa kod sorgusu hiç atılmaz
        var addedAny = false;

        foreach (var row in group.Rows)
        {
            if (variantsByStockCode.ContainsKey(row.StockCode))
            {
                continue;   // varyant zaten var → idempotent no-op
            }

            usedCodes ??= await LoadVariantCodesAsync(product.Id);
            variantsByStockCode[row.StockCode] = await CreateVariantAsync(
                product, row, group.Title, usedCodes, tryCurrencyUnitId, isMain: false);

            addedAny = true;
            report.AddedVariants++;
            report.AddedStockCodes.Add(row.StockCode);
        }

        if (addedAny)
        {
            // Mevcut main KORUNUR — yeni eklenenler main OLMAZ (merkezî EnsureMainVariantAsync idempotenttir).
            await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId, product.Code, product.Name);
        }
    }

    /// <summary>Satır-başına gelen uzak görselleri İLGİLİ ERP VARYANTININ kendi medya bağlamına indirir
    /// ("ProductVariant" + varyant Id'si) — Trendyol ikizi.
    ///
    /// <para><b>Neden gerekti (2026-08-20):</b> REST yanıtında her SKU satırı KENDİ görsellerini taşır, ama içe
    /// aktarım bunları yalnız grup seviyesinde birleştirip (<see cref="N11RemoteProductGroup.ImageUrls"/>) ürün
    /// bağlamına yazıyordu. Görsel KAYBOLMUYORDU ama satır↔görsel eşleşmesi kayboluyordu: hangi fotoğrafın hangi
    /// varyanta ait olduğu bilgisi hiçbir yerde durmuyordu.</para>
    ///
    /// <para>Grup-seviyesi (birleşik) set kayıt geneline yazılmaya DEVAM eder — iki bağlam birbirinin yerine
    /// geçmez (CLAUDE.md §6) ve push zinciri varyant→kayıt-geneli fallback'iyle okur.</para></summary>
    private async Task ImportVariantImagesAsync(
        Product product,
        N11RemoteProductGroup group,
        Dictionary<string, EntityVariant> variantsByStockCode,
        N11ImportResultDto report)
    {
        foreach (var row in group.Rows)
        {
            if (row.ImageUrls is not { Count: > 0 } imageUrls)
            {
                continue;
            }

            if (!variantsByStockCode.TryGetValue(row.StockCode, out var variant)
                || variant.EntityId != product.Id)
            {
                continue;   // satır ERP varyantına eşleşmedi → görsel yanlış varyanta bağlanmaktansa hiç bağlanmaz
            }

            // İndirici URL-başına dayanıklıdır (bozuk görsel atlanır + loglanır) ve EKLEMELİDİR: kullanıcının
            // varyanta elle bağladığı görseller bu çağrıyla EZİLMEZ. Sınıra takılan görsel RAPORA taşınır —
            // varyant bağlamı her turda yazıldığı için sınır aşımının en olası yeri burasıdır.
            report.SkippedImages += (await _imageDownloader.ImportToVariantAsync(
                    variant.Id,
                    product.CompanyId,
                    product.Code,
                    variant.Code,
                    imageUrls))
                .SkippedForCapacityCount;
        }
    }

    /// <summary>Tek varyant + satış fiyatı uzantısı. Stok yalnız BURADA (varyant doğarken) uzaktan seed'lenir;
    /// sonraki içe aktarımlar core stoğu EZMEZ (K12 politikası — bkz. <see cref="ResolveOverrideStock"/>).</summary>
    private async Task<EntityVariant> CreateVariantAsync(
        Product product,
        N11RestProductSummary row,
        string? groupTitle,
        HashSet<string> usedCodes,
        Guid? tryCurrencyUnitId,
        bool isMain)
    {
        var variantCode = BuildUniqueVariantCode(row.StockCode, usedCodes);

        // EntityVariant adı CASE-KORUR (TitleCase yok) → uzak başlık doğrudan ctor'a.
        var variant = new EntityVariant(
            product.CompanyId,
            ProductEntityName,
            product.Id,
            variantCode,
            BuildSafeName(row.Title ?? groupTitle, variantCode),
            isMain);
        variant.SetStock(Math.Max(0, row.Quantity ?? 0));
        await _variantRepository.InsertAsync(variant, autoSave: true);

        // Negatif uzak fiyat guard'la süzülür — tek anomali kalem (SetSalePrice fail-fast) TÜM içe aktarımı düşürmesin.
        var salePrice = row.SalePrice is >= 0 ? row.SalePrice : null;
        var detail = new ProductVariantDetail(product.CompanyId, variant.Id);
        detail.SetSalePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
        await _variantDetailRepository.InsertAsync(detail, autoSave: true);

        return variant;
    }

    /// <summary>Ürünün CANLI varyant kodları. Soft-delete filtresi AÇIK: varyant kodu indeksi 2026-08-07'de
    /// <c>IsDeleted = 0</c> filtresine kavuştu → silinmiş satır artık kodu İŞGAL ETMEZ (Trendyol ikizi).</summary>
    private async Task<HashSet<string>> LoadVariantCodesAsync(Guid productId)
    {
        var codes = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId)
                .Select(v => v.Code));
        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Şablon kodu: uzak stok kodu normalize edilir (UPPER + tek boşluk), kısa ise "N11-" ön eki, uzun ise
    /// kırpılır; şirket içinde benzersizlik "-2/-3..." son ekiyle döngülü sağlanır (ham DB unique hatasına düşmesin).
    /// Soft-delete filtresi AÇIK: Product indeksi 2026-08-07'de <c>IsDeleted = 0</c> kazandı → silinmiş ürünün kodu
    /// SERBESTTİR ve yeniden içe aktarımda orijinal stok kodu geri gelir (Trendyol ikizi; gerekçe orada).
    /// Son ek gerekirse kullanıcıya RAPORLANIR (Trendyol ikizi).</summary>
    private async Task<string> BuildUniqueProductCodeAsync(Guid companyId, string rawCode, N11ImportResultDto report)
    {
        var baseCode = NormalizeImportCode(rawCode);
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

        if (!string.Equals(candidate, baseCode, StringComparison.Ordinal))
        {
            report.Warnings.Add(L["N11Product:Import:CodeUniquified", baseCode, candidate].Value);
        }

        return candidate;
    }

    /// <summary>Varyant kodu — aynı normalize; benzersizlik verilen küme içinde (bellek-içi).</summary>
    private static string BuildUniqueVariantCode(string rawCode, HashSet<string> usedCodes)
    {
        var baseCode = NormalizeImportCode(rawCode);
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

    /// <summary>İçe aktarım kod normalizasyonu — Code konvansiyonunun çekirdeği (<c>NormalizeAsCode</c>) + içe
    /// aktarım dayanıklılığı: boş → "N11", kısa (&lt;3) → "N11-" ön eki, uzun → kırp. Fail-fast yerine onarım
    /// BİLİNÇLİ: uzak veri bizim kontrolümüzde değil, kalem kaybetmek daha kötü.</summary>
    private static string NormalizeImportCode(string rawCode)
    {
        var normalized = rawCode.NormalizeAsCode();
        if (normalized.Length == 0)
        {
            normalized = "N11";
        }

        if (normalized.Length < EntityFieldConsts.CodeMinLength)
        {
            normalized = $"N11-{normalized}";
        }

        return Truncate(normalized, ProductConsts.CodeMaxLength);
    }

    /// <summary>Ad emniyeti: başlık boş/çok kısaysa kod kullanılır; uzun başlık şablon sınırına kırpılır.</summary>
    private static string BuildSafeName(string? title, string fallback)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length >= EntityFieldConsts.NameMinLength
            ? Truncate(trimmed, ProductConsts.NameMaxLength)
            : fallback;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    // ── Kanal kaydı upsert ──────────────────────────────────────────────────────────────────────────

    /// <summary>N11 kanal kaydını upsert eder. YENİ kayıtta <see cref="SalesChannelTrN11Product.SellerCode"/> uzak
    /// <c>productMainId</c>'den alınır (üretilmez!) — aksi hâlde sonraki push var olan listelemeyi güncellemek
    /// yerine İKİNCİ bir listeleme açardı. Her geçişte kategori + uzak durum tazelenir ve SKU kimlikleri
    /// dondurulmuş uzak stok koduyla işlenir.</summary>
    private async Task<SalesChannelTrN11Product> UpsertChannelRecordAsync(
        SalesChannelTrN11 channel,
        N11RemoteProductGroup group,
        SalesChannelTrN11Product? existing,
        string categoryExternalId,
        string shipmentTemplateName,
        Product product,
        Dictionary<string, EntityVariant> variantsByStockCode,
        Dictionary<string, string> categoryNames,
        int? defaultVatRate,
        N11ImportResultDto report)
    {
        var entity = existing;
        if (entity is null)
        {
            var sequenceNo = await NextSequenceNoAsync(channel.Id, product.Id);
            entity = new SalesChannelTrN11Product(
                channel.CompanyId,
                channel.Id,
                product.Id,
                Truncate(group.Key, N11ProductConsts.SellerCodeMaxLength),
                sequenceNo,
                categoryExternalId,
                shipmentTemplateName);

            // KDV yalnız YENİ kayda damgalanır — mevcut kaydınkini ezmek kullanıcının ürün bazında yaptığı
            // seçimi sessizce silmek olurdu. Geçersiz oran entity guard'ında fail-fast döner.
            entity.SetVatRate(defaultVatRate);
        }

        // Kategori ADI yalnız yerel ağaçta eşleşiyorsa yazılır (UI kolaylığı); ham uzak id her koşulda yazılıdır.
        entity.SetCategory(
            categoryExternalId,
            categoryNames.TryGetValue(categoryExternalId, out var categoryName) ? categoryName : null);

        var first = group.Rows[0];
        entity.ApplyImportedSnapshot(first.N11ProductId, first.SaleStatus, first.ProductStatus, Clock.Now.ToUniversalTime());

        foreach (var row in group.Rows)
        {
            if (!variantsByStockCode.TryGetValue(row.StockCode, out var variant))
            {
                continue;   // varyant kurulamadı — bu noktada olamaz (EnsureTemplateVariants ekledi)
            }

            // REST'in düzleşmiş modelinde satırın KENDİ n11ProductId'si fiilen SKU kimliğidir.
            entity.UpsertImportedSku(variant.Id, row.StockCode, row.N11ProductId is > 0 ? row.N11ProductId : null);
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

    // ── StockItem (fiyat/stok override) + yan-maliyet reçetesi ──────────────────────────────────────

    /// <summary>Uzak fiyat/stok kanal override katmanına yazılır (core EZİLMEZ): varyant-başına başlık upsert
    /// edilir; YENİ başlıkta kanal gider satırları da kurulur. Mevcut başlıkta reçeteye DOKUNULMAZ (kullanıcı emeği),
    /// yalnız override tazelenir — override kullanıcının rezerv alanı değil, pazaryerinin YANSIMASIDIR.</summary>
    private async Task UpsertStockItemsAsync(
        SalesChannelTrN11Product entity,
        N11RemoteProductGroup group,
        Product product,
        Dictionary<string, EntityVariant> variantsByStockCode,
        Guid? tryCurrencyUnitId,
        N11ImportResultDto report)
    {
        var headers = (await AsyncExecuter.ToListAsync(
                (await _stockItemRepository.GetQueryableAsync())
                    .Where(h => h.SalesChannelTrN11ProductId == entity.Id && h.ProductVariantId != null)))
            .ToDictionary(h => h.ProductVariantId!.Value);

        foreach (var row in group.Rows)
        {
            if (!variantsByStockCode.TryGetValue(row.StockCode, out var variant))
            {
                continue;
            }

            // OTORİTE DEVRİ (2026-08-05 Hakan kararı): ürün sınıflandırıldıysa (Calculated) pazaryerinde duran
            // stok ve fiyat GEÇERSİZDİR — ikisini de sistem belirler. Buraya remote değeri yazmak, her içe
            // aktarımda devri geri alırdı: push zinciri override'ı ÖNCELEDİĞİ için hesaplanan değer sessizce
            // gölgelenir ve kimse fark etmezdi.
            var authorityTransferred = product.StockPolicy == ProductStockPolicy.Calculated;

            var salePrice = authorityTransferred
                ? null
                : row.SalePrice is >= 0 ? row.SalePrice : null;
            var overrideStock = authorityTransferred
                ? null
                : ResolveOverrideStock(entity, variant, row, report);

            if (headers.TryGetValue(variant.Id, out var header))
            {
                header.SetOverridePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
                header.SetOverrideStock(overrideStock);
                await _stockItemRepository.UpdateAsync(header, autoSave: true);
                continue;
            }

            header = new SalesChannelTrN11ProductStockItem(entity.CompanyId, entity.Id, variant.Id);
            header.SetOverridePrice(salePrice, salePrice is null ? null : tryCurrencyUnitId);
            header.SetOverrideStock(overrideStock);
            await _stockItemRepository.InsertAsync(header, autoSave: true);
            headers[variant.Id] = header;
        }
    }

    /// <summary>Stok politikası (Trendyol içe aktarımıyla AYNI K12 kuralı): uzak stok, core
    /// <see cref="EntityVariant.StockQuantity"/> ile AYNIYSA null döner (override yazılmaz — "fark yok" gürültüsü
    /// üretilmez); FARKLIYSA uzak değer kanal override'ı olur + fark LogWarning + rapor sayacıyla GÖRÜNÜR kılınır.
    /// Core stok ASLA ezilmez: kullanıcının ERP'deki sayımı pazaryerinin anlık verisinden daha otoriterdir.</summary>
    private int? ResolveOverrideStock(
        SalesChannelTrN11Product entity, EntityVariant variant, N11RestProductSummary row, N11ImportResultDto report)
    {
        var remoteStock = Math.Max(0, row.Quantity ?? 0);
        if (remoteStock == variant.StockQuantity)
        {
            return null;
        }

        report.StockDifferenceCount++;
        Logger.LogWarning(
            "N11 içe aktarım stok farkı: kanal kaydı {SellerCode} / varyant {VariantCode} (uzak stok kodu {StockCode}) — çekirdek {CoreStock}, uzak {RemoteStock}. Çekirdek EZİLMEDİ; uzak değer kanal OverrideStock'una yazıldı.",
            entity.SellerCode,
            variant.Code,
            row.StockCode,
            variant.StockQuantity,
            remoteStock);
        return remoteStock;
    }

    // ── Yardımcı yüklemeler ─────────────────────────────────────────────────────────────────────────

    /// <summary>Yerel N11 kategori ağacı: id → ad. Anahtar varlığı "bu kategoriyi tanıyoruz" demektir (ayrı bir küme
    /// tutulmaz). <see cref="N11Category"/> HOST-GLOBAL'dir (<c>IMultiTenant</c> DEĞİL) → tenant filtresi uygulanmaz,
    /// ek kapsam sabitlemesi gerekmez. Eşleşme yalnız RAPOR + görüntü adı içindir; kanal kaydına ham uzak id her
    /// koşulda yazılır (ağaç henüz sync'lenmemişse bile içe aktarım tamamlanır).</summary>
    private async Task<Dictionary<string, string>> LoadN11CategoryNamesAsync()
    {
        var rows = await AsyncExecuter.ToListAsync(
            (await _n11CategoryRepository.GetQueryableAsync()).Select(c => new { c.ExternalId, c.Name }));

        return rows
            .GroupBy(r => r.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
    }

    /// <summary>İçe aktarılan kayıtlara yazılacak kargo şablonu adı. Uzak yanıt bu alanı TAŞIMAZ ama entity zorunlu
    /// kılar → kanalın kendi şablonlarından çözülür: tek aktif şablon varsa sessizce o; birden fazlaysa ilki + uyarı
    /// (kullanıcı doğrulamalı); hiç yoksa yer tutucu + uyarı.</summary>
    private async Task<string> ResolveImportShipmentTemplateAsync(Guid salesChannelId, N11ImportResultDto report)
    {
        var names = await AsyncExecuter.ToListAsync(
            (await _n11ShipmentTemplateRepository.GetQueryableAsync())
                .Where(t => t.SalesChannelId == salesChannelId && t.IsActive)
                .OrderBy(t => t.TemplateName)
                .Select(t => t.TemplateName));

        if (names.Count == 0)
        {
            report.Warnings.Add(L["N11Product:Import:ShipmentTemplateMissing"].Value);
            return ImportedShipmentTemplatePlaceholder;
        }

        if (names.Count > 1)
        {
            report.Warnings.Add(L["N11Product:Import:ShipmentTemplateAmbiguous", names[0]].Value);
        }

        return names[0];
    }
}

/// <summary>Uzak SKU satırlarının <c>productMainId</c>'ye göre gruplanmış hâli = bir uzak ÜRÜN.
/// <see cref="Key"/> gruplama anahtarı (<c>productMainId</c>, yoksa satırın stok kodu) ve aynı zamanda yeni kanal
/// kaydının <c>SellerCode</c>'u olur.</summary>
internal sealed record N11RemoteProductGroup(string Key, List<N11RestProductSummary> Rows)
{
    /// <summary>Grubun başlığı — ilk DOLU satır başlığı (bazı satırlar başlıksız gelebilir).</summary>
    public string? Title
    {
        get { return Rows.Select(r => r.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)); }
    }

    /// <summary>Grubun kategori id'si — ilk DOLU satırdan (varyantlar aynı kategoridedir).</summary>
    public string? CategoryExternalId
    {
        get { return Rows.Select(r => r.CategoryId).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)); }
    }

    /// <summary>Grubun görselleri — satırlar arası birleşik, sıra korunarak tekilleştirilmiş.</summary>
    public IReadOnlyList<string> ImageUrls
    {
        get
        {
            return Rows
                .SelectMany(r => r.ImageUrls)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
