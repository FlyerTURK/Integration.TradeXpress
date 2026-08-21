using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Progress;
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
/// yazılır (kullanıcı onaylı yön); uzak STOK ise K12 politikasına tabidir (2026-07-23 kesin karar): core
/// <c>StockQuantity</c> yalnız İLK kuruluşta (varyant bu importta doğarken) seed'lenir, sonraki importlarda remote
/// stok core'u EZMEZ — fark varsa <see cref="SalesChannelTrTrendyolProductStockItem.OverrideStock"/>'a yazılır
/// (kanal gerçeği) + LogWarning + rapor sayacı; core ile AYNIYSA override null kalır (gürültü üretilmez).</para>
/// </summary>
public partial class SalesChannelTrTrendyolProductAppService
{
    /// <summary>Uzak kayıtta MARKA id'si hiç yoksa yazılan sentinel — entity BrandId zorunlu (min 1); "0" Trendyol'da
    /// geçersiz id'dir, kullanıcı düzenleyene dek push zaten NumericId geçerli ama onaysız kalır. KATEGORİ için
    /// sentinel KALKTI (Trendyol_CategoryOptional, 2026-07-11): eksik/taşan kategori NULL yazılır
    /// (<see cref="SafeCategoryId"/>) ve UnmatchedCategories raporunda görünür.</summary>
    private const string UnknownExternalId = "0";

    /// <summary>İlerleme kanalı — ambient scoped sink (Blazor bileşeni aynı circuit'te dinler; HTTP API'de dinleyen
    /// yok, rapor kaybolur — zararsız). Ctor'a eklenmedi: 36 parametreli ctor + tüm test sahteleri kırılırdı;
    /// LazyServiceProvider aynı scope'tan çözer.</summary>
    private IOperationProgressSink Progress => LazyServiceProvider.LazyGetRequiredService<IOperationProgressSink>();

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<TrendyolImportResultDto> ImportFromMarketplaceAsync(Guid salesChannelId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);

        try
        {
            return await ImportCoreAsync(channel);
        }
        finally
        {
            // Panel kapanır — başarı/hata fark etmez (yarım kalan çubuk "hâlâ çalışıyor" izlenimi verirdi).
            Progress.Complete();
        }
    }

    private async Task<TrendyolImportResultDto> ImportCoreAsync(SalesChannelTrTrendyol channel)
    {
        // Salt GET: tüm satıcı ürünleri sayfa sayfa çekilir + productMainId'ye göre gruplanır (P1: FetchRemoteProductsAsync)
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

        // Kanalın mevcut kayıtları — eşleşme anahtarı RemoteProductMainId ?? stockCode/barcode (Skus JSON'u entity
        // ile gelir; import bağlamında bellek-içi tarama yeterli).
        var existingRecords = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == channel.CompanyId && x.SalesChannelId == channel.Id));

        // Tüm uzak barcode'lar TEK seferde yerel varyantlara çözülür (filtered unique index güvencesiyle tekil).
        // Arama ŞİRKET kapsamlı — unique index (TenantId, CompanyId, Barcode) ile AYNI kapsam. Aynı tenant altındaki
        // BAŞKA şirketin aynı barkodu artık çakışma değildir (her şirket kendi pazaryeri hesabıyla aynı malı satabilir).
        var variantsByBarcode = await LoadVariantsByBarcodeAsync(
            channel.CompanyId,
            remoteProducts.SelectMany(p => p.Variants.Select(v => v.Barcode)).ToList());

        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatchedCategories = new HashSet<string>(StringComparer.Ordinal);

        var processed = 0;
        foreach (var remote in remoteProducts)
        {
            // İŞLEME fazı: ürün başına ilerleme + o anki ürünün adı (kullanıcı "ne çekiliyor" görsün).
            Progress.Report(new OperationProgress(
                L["TrendyolProduct:Import:Phase:Processing"].Value, ++processed, remoteProducts.Count, remote.Title));

            var validVariants = FilterImportableVariants(remote, seenBarcodes, report.SkippedRows);
            if (validVariants.Count == 0)
            {
                continue;   // grubun tüm kalemleri raporlanarak elendi
            }

            TrackUnmatchedCategory(remote, knownCategoryIds, unmatchedCategories, report);

            var existing = await DiscardOrphanedRecordAsync(
                FindExistingChannelRecord(remote, validVariants, existingRecords), existingRecords, report);

            var product = await ResolveOrCreateTemplateAsync(
                channel, remote, validVariants, existing, variantsByBarcode, tryCurrencyUnitId, report);

            // MEVCUT şablonda karşılığı olmayan barkodlu kalemler şablona OTOMATİK varyant olur (ekleme-only;
            // 2026-07-11 kullanıcı kararı — eski "Eksik Varyantları Tamamla" düğmesi import'a gömüldü).
            await EnsureTemplateVariantsAsync(remote, validVariants, product, variantsByBarcode, tryCurrencyUnitId, report);

            // Trendyol görseli KALEM (barkod) başına verir → varyanta özel görsel VARYANTIN kendi bağlamına iner.
            // Varyantlar bu satıra kadar tamamlandığı için eşleşme burada en eksiksizdir.
            await ImportVariantImagesAsync(product, validVariants, variantsByBarcode, report);

            var entity = await UpsertChannelRecordAsync(channel, remote, validVariants, existing, product, variantsByBarcode, report);
            if (existing is null)
            {
                existingRecords.Add(entity);   // aynı import içinde ikinci grup aynı kaydı bulabilsin
            }

            // ⚠ Plan ÜRÜN BAŞINA kurulur, import başına DEĞİL: komisyon oranı KATEGORİDEN kalıtımla gelir ve kategori
            // ürün-başı bir alandır. Döngü dışında tek plan kurulsaydı ilk ürünün oranı 103 ürünün tamamına
            // uygulanırdı — kozmetik oranıyla fiyatlanan bir ayakkabı ~2 puan yanlış olurdu ve hiçbir yerde
            // görünmezdi. Çözücü ağacı istek başına TEK kez okur; döngü içi çağrı ek sorgu üretmez.
            var sideCostPlan = SideCostPlan.From(
                channel.SideCosts,
                await _commissionResolver.ResolveAsync(entity.CategoryId),
                variantOptInEnabled: false);

            await UpsertStockItemsAsync(entity, product, validVariants, variantsByBarcode, tryCurrencyUnitId, sideCostPlan, report);
        }

        ReportSkippedImages(report);
        return report;
    }

    /// <summary>Görsel sınırına takılıp hiç bağlanmayan pazaryeri görsellerini RAPORA taşır (N11/Etsy ikizi).
    ///
    /// <para><b>Neden gerekli:</b> sınır aşımı indiricide yalnız server-log'a düşüyordu; kullanıcı "içe aktarım
    /// başarılı" raporunu görüp fotoğrafın neden gelmediğini hiçbir ekranda bulamıyordu. Sayı ürün-başı değil
    /// import-başı TEK satırda verilir — 103 ürünlük bir mağazada ürün başına uyarı raporu okunmaz hâle
    /// getirirdi.</para></summary>
    private void ReportSkippedImages(TrendyolImportResultDto report)
    {
        if (report.SkippedImages > 0)
        {
            report.Warnings.Add(
                L["TrendyolProduct:Import:ImagesSkippedForLimit", report.SkippedImages, ProductConsts.MaxImageCount].Value);
        }
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
        // ÇEKİM fazı: sayfa sayfa; toplam sayfa ilk yanıtta belli olur — sayaç "sayfa N / M · K kalem" (toplam
        // ürün sayısı gruplamadan önce bilinemez, faz bu yüzden sayfa-bazlı). İstemcinin sayfalama döngüsü
        // (FetchAllPagesAsync — güvenlik tavanı dahil) AYNEN kullanılır; yalnız sayfa delegesi raporlar.
        var credentials = CredentialsOf(channel);
        var fetchPhase = L["TrendyolProduct:Import:Phase:Fetching"].Value;
        var fetchedItems = 0;
        var flat = await TrendyolProductClient.FetchAllPagesAsync(async page =>
        {
            var result = await _client.GetSellerProductsAsync(credentials, page, 200);
            fetchedItems += result.Items.Count;
            Progress.Report(new OperationProgress(
                fetchPhase, page + 1, result.TotalPages > 0 ? result.TotalPages : null,
                L["TrendyolProduct:Import:FetchedItems", fetchedItems].Value));
            return result;
        });

        var remoteProducts = TrendyolProductClient.GroupByProductMainId(flat);
        return MergeGroupsSharingStockCode(remoteProducts);
    }

    /// <summary>stockCode kesişen uzak grupları birleştirir — ortak alanlar İLK gruptan (GroupByProductMainId ile aynı
    /// ilke), varyantlar geliş sırasıyla eklenir. Kod-çakışan kardeşlerin varyant kodları şablon kuruluşunda
    /// <see cref="BuildUniqueVariantCode"/> son-ekiyle ("-2", "-3"...) ayrışır. Bir grup birden fazla önceki gruba
    /// bağ kuruyorsa İLK eşleşen kazanır (deterministik); zaten eşlenmiş stockCode yeniden eşlenmez.</summary>
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

    /// <summary>İçe alınabilir kalemleri süzer: barcode'suz kalem, barcode uzunluk taşması ve import-içi duplike
    /// barcode ATLANIR + raporlanır (sessiz geçilmez). Atlanan satırlar <paramref name="skippedRows"/>'a yazılır.
    ///
    /// <para><b>"Başka şirketin barkodu" elemesi KALDIRILDI</b> (2026-08-04): unique index şirkete daraltıldığı
    /// için aynı tenant altındaki farklı şirketlerin aynı barkodu artık ÇAKIŞMA DEĞİLDİR. Eleme kalsaydı meşru
    /// kalemleri atlamaya devam ederdi.</para></summary>
    private List<TrendyolRemoteVariant> FilterImportableVariants(
        TrendyolRemoteProduct remote,
        HashSet<string> seenBarcodes,
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

    /// <summary>ÖKSÜZ kanal kaydını eler: şablon ürünü SİLİNMİŞ bir kanal kaydı kullanılamaz durumdadır
    /// (açılamaz, düzenlenemez, push edilemez) ve ürüne yalnız Guid ile bağlı olduğu için DB onu tutmaz.
    /// Böyle bir kayıt bulunursa bağımlılarıyla birlikte kaldırılır ve <c>null</c> dönülür → ürün mağazadan
    /// SIFIRDAN kurulur.
    ///
    /// <para><b>Neden fırlatmak yerine bu:</b> önceden <c>GetOwnedProductAsync</c> burada
    /// <c>ProductNotFound</c> fırlatıyordu ve TEK öksüz kayıt 103 ürünlük partinin TAMAMINI iptal ediyordu —
    /// kullanıcıya çıkan tek şey hangi kaydı işaret ettiği belirsiz bir "Ürün bulunamadı" bildirimiydi. Oysa bu
    /// içe aktarımın her yerinde kural "raporla ve devam et"tir (atlanan kalem, eşleşmeyen kategori); tek istisna
    /// buydu. Üstelik "yereli sil, mağazadan sıfırdan çek" bu düğmenin ilan edilmiş amacıdır — o akışı kilitleyen
    /// şey tam olarak buydu. Aynı gerekçeyle sonuç kullanıcıya RAPORLANMAZ da: istenen sonucu kayıt başına
    /// duyurmak bilgi değil gürültüdür.</para>
    ///
    /// <para>⚠ <b>Bedeli:</b> kayıt yeniden kurulunca bizim ürettiğimiz <c>ProductMainId</c> ("{Kod}-{Sıra}")
    /// yeni ürün koduna göre YENİDEN üretilir; Trendyol'un bildiği gruplama kimliği değişebilir. Uzak kimlikler
    /// (<c>RemoteProductMainId</c>, contentId, barkod) mağaza yükünden zaten geri gelir. Kaydın ürünü silinmişken
    /// alternatif "hiç içe aktarma"dır — bu bedel bilinçle kabul edildi.</para></summary>
    private async Task<SalesChannelTrTrendyolProduct?> DiscardOrphanedRecordAsync(
        SalesChannelTrTrendyolProduct? existing,
        List<SalesChannelTrTrendyolProduct> existingRecords,
        TrendyolImportResultDto report)
    {
        if (existing is null || await FindOwnedProductAsync(existing.ProductId) is not null)
        {
            return existing;
        }

        // Kullanıcıya RAPORLANMAZ (2026-08-06 Hakan kararı): "yereli sil, mağazadan sıfırdan çek" akışında bu
        // durum İSTENEN sonucun ta kendisidir ve kullanıcının yapabileceği bir şey yoktur. 18 kaydı silmiş
        // kullanıcıya 18 satır "kayıt yeniden kuruldu" yazmak bilgi değil gürültüdür. Adli iz sunucu logunda kalır.
        Logger.LogInformation(
            "Trendyol içe aktarım: {ProductMainId} kanal kaydının şablon ürünü ({ProductId}) silinmiş — kayıt kaldırıldı, ürün mağazadan yeniden kurulacak.",
            existing.ProductMainId,
            existing.ProductId);

        await DeleteChannelProductGraphAsync(existing);
        existingRecords.Remove(existing);
        return null;
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

        var product = await CreateTemplateProductAsync(channel, remote, variants, variantsByBarcode, tryCurrencyUnitId, report);
        report.CreatedProducts++;
        return product;
    }

    // ── Şablon Product + varyant üretimi (yalnız İLK import; sonrası dokunulmaz) ────────────────────

    /// <summary>Uzak üründen şablon <see cref="Product"/> üretir: Code stockCode'dan normalize (benzersizlik döngülü),
    /// Name = Trendyol başlığı CASING KORUNARAK (<c>SetName(name, normalizeTitle:false)</c> — TitleCase import'ta
    /// başlığı bozar), Description (şablon sınırına kırpılır), görseller URL-kaynaklı, para birimi TRY. Her uzak kalem
    /// için varyant üretilir (ilk kalem MAIN); ana-varyant değişmezi MERKEZİ metottan geçer
    /// (<see cref="EntityVariantManager.EnsureMainVariantAsync"/>).</summary>
    private async Task<Product> CreateTemplateProductAsync(
        SalesChannelTrTrendyol channel,
        TrendyolRemoteProduct remote,
        List<TrendyolRemoteVariant> variants,
        Dictionary<string, EntityVariant> variantsByBarcode,
        Guid? tryCurrencyUnitId,
        TrendyolImportResultDto report)
    {
        var first = variants[0];
        var code = await BuildUniqueProductCodeAsync(channel.CompanyId, first.StockCode ?? first.Barcode, report);

        // Ad TEK atamayla casing-korumalı yazılır: ctor'a geçici ad olarak KOD verilir (ctor SetName'i TitleCase
        // normalize eder ama hemen ezilir), gerçek başlık bir kez normalizeTitle:false ile set edilir.
        var product = new Product(channel.CompanyId, code, code);
        product.SetName(BuildSafeName(remote.Title, code), normalizeTitle: false);
        product.SetDescription(BuildTemplateDescription(remote.Description));
        product.SetCurrencyUnit(tryCurrencyUnitId);

        // ÜRÜNÜN KENDİ kategorisi kanal kategorisinden çözülür/kurulur (2026-08-06 Hakan kararı) — yalnız YENİ üründe:
        // mevcut ürünün kategorisi kullanıcı beyanıdır, import EZMEZ (minimal-güncelleme kuralı).
        product.SetProductCategory(await _categoryResolver.ResolveOrCreateAsync(
            channel.CompanyId, SalesChannelType.TrTrendyol, remote.CategoryId, remote.CategoryName));

        await _productRepository.InsertAsync(product, autoSave: true);

        // Görseller DAM'a — link ürün Id'sine bağlandığından INSERT'ten SONRA (dedup + ilk görsel cover).
        // YALNIZ kuruluşta (N11 ikizi): mevcut ürünün kayıt-geneli galerisi kullanıcı beyanıdır, içe aktarım onu
        // tazelemez. Kısıt POLİTİKA gereğidir — indirici eklemeli olduğundan teknik bir ezme riski yok.
        report.SkippedImages += (await _imageDownloader.ImportToProductAsync(product, remote.ImageUrls))
            .SkippedForCapacityCount;

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

        // Ana-varyant değişmezi merkezi EnsureMainVariantAsync'ten (tekil main garanti; idempotent) — agnostik EntityVariantManager.
        await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId, product.Code, product.Name);
        return product;
    }

    /// <summary>Uzakta olup YEREL şablonda karşılığı OLMAYAN barkodlu kalemleri şablona varyant olarak EKLER
    /// (2026-07-11 kullanıcı kararı: eski "Eksik Varyantları Tamamla" düğmesi geçici çözümdü — davranış import'a
    /// gömüldü). Minimal-güncelleme kuralının kalan kısmı: mevcut hiçbir varyant/şablon ALANI GÜNCELLENMEZ, yalnız
    /// varyant EKLENİR; ANA VARYANT DEĞİŞMEZ (yeni eklenen main doğmaz; tekil-main değişmezi merkezî metottan —
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
            // Ana-varyant değişmezi MERKEZÎ EnsureMainVariantAsync'ten (idempotent): mevcut main KORUNUR — yeni eklenenler main OLMAZ.
            await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId, product.Code, product.Name);
        }
    }

    /// <summary>Kalem-başına gelen uzak görselleri İLGİLİ ERP VARYANTININ kendi medya bağlamına indirir
    /// ("ProductVariant" + varyant Id'si).
    ///
    /// <para><b>Neden gerekti (2026-08-20):</b> Trendyol görselleri barkod (yani varyant) başına döndürür, ama
    /// içe aktarım yalnız İLK kalemin görsellerini alıp ürün-geneli bağlama yazıyordu; kalan kalemlerin
    /// görselleri sessizce DÜŞÜYORDU. Sonuç, "kırmızı kılıf"ın kendi fotoğrafının hiçbir ekranda
    /// bulunamamasıydı — push zinciri varyant→kayıt-geneli fallback'iyle okuduğu için hata da vermiyordu,
    /// yalnız yanlış görsel gidiyordu.</para>
    ///
    /// <para><b>İkinci bir eşleştirme YOK:</b> hangi uzak kalemin hangi ERP varyantı olduğu zaten
    /// <paramref name="variantsByBarcode"/> ile çözülmüştür (Sku kimliklerini yazan yolun ta kendisi). Eşleşmeyen
    /// kalem (kanal-only ya da barkodu BAŞKA şablona ait) ATLANIR: görseli yanlış varyanta bağlamaktansa hiç
    /// bağlamamak geri alınabilirdir.</para></summary>
    private async Task ImportVariantImagesAsync(
        Product product,
        List<TrendyolRemoteVariant> variants,
        Dictionary<string, EntityVariant> variantsByBarcode,
        TrendyolImportResultDto report)
    {
        foreach (var remoteVariant in variants)
        {
            if (remoteVariant.ImageUrls is not { Count: > 0 } imageUrls)
            {
                continue;
            }

            if (!variantsByBarcode.TryGetValue(remoteVariant.Barcode, out var localVariant)
                || localVariant.EntityId != product.Id)
            {
                continue;   // eşleşme yok (kanal-only kalem ya da başka şablonun barkodu) → görsel de yazılmaz
            }

            // İndirici kendi içinde URL-başına dayanıklıdır (bozuk görsel atlanır + loglanır) ve EKLEMELİDİR:
            // kullanıcının varyanta elle bağladığı görseller bu çağrıyla EZİLMEZ. Sınıra takılan görsel RAPORA
            // taşınır — varyant bağlamı her turda yazıldığı için sınır aşımının en olası yeri burasıdır.
            report.SkippedImages += (await _imageDownloader.ImportToVariantAsync(
                    localVariant.Id,
                    product.CompanyId,
                    product.Code,
                    localVariant.Code,
                    imageUrls))
                .SkippedForCapacityCount;
        }
    }

    /// <summary>Ürünün CANLI varyant kodları. Soft-delete filtresi AÇIK: varyant kodu indeksi 2026-08-07'de
    /// <c>IsDeleted = 0</c> filtresine kavuştu → silinmiş satır artık kodu İŞGAL ETMEZ ve kod yeniden kullanılabilir.
    /// Filtreyi burada kapatmak silinmiş kodları hâlâ "dolu" sayıp gereksiz "-2" son eki ürettirirdi.</summary>
    private async Task<HashSet<string>> LoadVariantCodesAsync(Guid productId)
    {
        var codes = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId)
                .Select(v => v.Code));
        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Şablon kodu: stockCode/barcode normalize edilir (UPPER + tek boşluk), kısa ise "TY-" ön eki, uzun ise
    /// kırpılır; şirket içinde benzersizlik "-2/-3..." son ekiyle döngülü sağlanır (Code unique index'i ham DB hatasına
    /// düşmesin).</summary>
    private async Task<string> BuildUniqueProductCodeAsync(Guid companyId, string rawCode, TrendyolImportResultDto report)
    {
        // Soft-delete filtresi AÇIK sorgulanır: Product unique index'i 2026-08-07'de "IsDeleted = 0" filtresine
        // kavuştu → SİLİNMİŞ ürünün kodu artık serbesttir. Öncesinde filtre kapatılıyordu ve silinen her ürün
        // kodunu KALICI olarak yakıyordu; "yereli sil, mağazadan sıfırdan çek" akışında ürünler orijinal stok
        // koduyla değil "-2" son ekiyle geri geliyordu. NextSequenceNoAsync'in soft-delete'i atlaması AYRI ve
        // meşru bir gerekçeye dayanır (silinen sıranın kodu pazaryerinde YAŞAYAN listelemeye ait) — o dokunulmadı.
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

        // Son ek gerekti = şirkette AYNI kodlu CANLI bir ürün zaten var. Kullanıcıya RAPORLANIR (2026-08-06
        // Hakan isteği): kod sessizce değişirse kullanıcı iki ürünü tek sanıp yanlışını aramakla vakit kaybeder.
        if (!string.Equals(candidate, baseCode, StringComparison.Ordinal))
        {
            report.Warnings.Add(L["TrendyolProduct:Import:CodeUniquified", baseCode, candidate].Value);
        }

        return candidate;
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

    /// <summary>Import kod normalizasyonu — Code konvansiyonuyla aynı taban (<c>NormalizeAsCode</c>: Trim + tek
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

        // Ürün seviyesine YALNIZ eksen-dışı (tüm kalemlerde aynı) nitelikler yazılır. Önceki davranış ilk
        // kalemin TÜM niteliklerini alıyordu — eksen varsa birinci varyantın değeri ("Kırmızı"/"50 ml") ürünün
        // beyanı sanılıyor, push body'si de ürün niteliklerini her item'a kopyaladığından tüm varyantlar aynı
        // eksen değeriyle gidiyordu. Eksen değerleri KALEM-BAŞINA fotoğraflanır (plan.ValuesByBarcode →
        // Sku.RemoteVariantAttributes) ve push body'si onları item-düzeyi attribute olarak GERİ gönderir.
        var axisPlan = TrendyolVariantAxisResolver.Resolve(variants);
        entity.SetAttributes(axisPlan.ProductLevelAttributes.Select(a => new SalesChannelTrTrendyolProductCategoryAttribute(
            a.AttributeId,
            a.AttributeValueId,
            TruncateOptional(a.CustomValue ?? (a.AttributeValueId is null ? a.AttributeValue : null), TrendyolProductConsts.CustomAttributeValueMaxLength))));

        entity.ApplyRemoteSnapshot(
            remote.ProductMainId,
            AggregateFlag(variants.Select(v => v.Approved)),
            AggregateFlag(variants.Select(v => v.OnSale)),
            first.ListPrice is >= 0 ? first.ListPrice : null);

        // Kanalın KENDİ görsel adresleri (CDN) + o anki YEREL görsel setinin RemoteImageMediaIds damgası — push'un yeniden-kullanım
        // dalını besler: bugünkü set damgayla aynıysa geçici link yerine bu adresler gönderilir (kanala aynı
        // görseli yeniden yutturma). Damga olmadan adres tek başına yanıltır: hangi sete ait olduğu bilinemez
        // ve bayat kanal adresi kullanıcının değiştirdiği görselleri geri alabilirdi (entity doc'u).
        // Adres emniyeti: sayı + uzunluk import sınırında (tek anomali kalem partiyi düşürmesin), şema http(s).
        // Adres kümesi HAVUZ + TÜM VARYANT setlerinin birleşimidir (CollectAllImageUrls) — damganın karşılığı
        // olan aday medya listesi de iki bağlamı birden kapsıyor. Yalnız havuzu yazsaydık eşleşme yine tutar,
        // ama varyant fotoğrafları adres listesinde bulunmadığı için sessizce düşer ve defter onları yine de
        // "gönderildi" diye yazardı.
        entity.SetRemoteImageUrls(
            SafeRemoteImageUrls(TrendyolProductClient.CollectAllImageUrls(remote)),
            await _pushImageResolver.ResolveCandidateMediaIdsAsync(product, ProductConsts.MaxImageCount));

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
            // PAZARYERİNİN BEYANI DA SAKLANIR (2026-08-10): adet/fiyat zaten bu yanıtta geliyordu ama
            // atılıyordu ve kanal-ürün listesindeki fiyat/stok kolonları bu yüzden BOŞTU. Bu değerler push
            // zincirine GİRMEZ (fiyat StockItem override'larından yürür) — yalnız "kanalda şu an ne var"
            // sorusunun cevabıdır ve o soru hiç push etmemiş kayıtlarda ancak buradan cevaplanabilir.
            entity.UpsertImportedSku(
                localVariant.Id,
                remoteVariant.Barcode,
                stockCode,
                remoteVariant.ProductContentId,
                BuildRemoteState(remoteVariant, axisPlan));
        }

        // IsActive = KANALDAKİ ARŞİV DURUMUNUN YANSIMASI (2026-08-17 Hakan kararı) — import ters yönü besler: kanal
        // "arşivde" diyorsa bizde pasif, "arşivde değil" diyorsa aktif; bildirmiyorsa DOKUNMA (üç durumlu, engel
        // bayraklarıyla aynı okuma). Kayıt seviyesi: kalemlerden herhangi biri arşivdeyse ürün arşivde sayılır
        // (Trendyol arşivi kalem-bazlı ama bizde bayrak kayıt-bazlı; kısmi arşiv "satışta" görünmesin — fail-closed).
        var archivedFlags = variants.Select(v => v.Flags?.Archived).ToList();
        if (archivedFlags.Any(f => f is not null))
        {
            entity.SetActive(!archivedFlags.Any(f => f == true));
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

    /// <summary>Pazaryerinin kalem beyanını kanal kaydına taşınacak tek pakete çevirir — adet/fiyat GÖRÜNTÜSÜ
    /// + ENGEL bayrakları. Hiçbiri push zincirine girmez (fiyat StockItem override'larından yürür); bunlar
    /// "kanalda şu an ne var ve neden satılamıyor" sorusunun cevabıdır ve o soru hiç push edilmemiş kayıtlarda
    /// ancak buradan cevaplanır.</summary>
    private static TrendyolRemoteListingState BuildRemoteState(
        TrendyolRemoteVariant remoteVariant, TrendyolVariantAxisPlan axisPlan)
    {
        var flags = remoteVariant.Flags;

        // Kalemin EKSEN değerleri (plan çözümünden): eksen yoksa BOŞ liste = "eksen yok" beyanı (mevcut
        // fotoğrafı temizler — grup tekilleşince bayat "Renk" kalmasın). Metin, kategori-attribute metniyle
        // aynı üst sınıra kırpılır (aynı onarım felsefesi).
        var axisValues = axisPlan.ValuesByBarcode.TryGetValue(remoteVariant.Barcode, out var values)
            ? values.Select(v => new SalesChannelTrTrendyolProductSkuRemoteAxisValue(
                v.AttributeId,
                v.AttributeValueId,
                TruncateOptional(v.ValueText, TrendyolProductConsts.CustomAttributeValueMaxLength),
                TruncateOptional(v.AttributeName, TrendyolProductConsts.AttributeNameMaxLength))).ToList()
            : new List<SalesChannelTrTrendyolProductSkuRemoteAxisValue>();

        // Gerekçe/URL uzunluk emniyeti: kanalın kendi cümlesi sınırı aşabilir (birleştirilen red gerekçeleri
        // gerçekçi biçimde 1000'i geçer) — entity guard fail-fast'i tek anomali kalemle TÜM importu
        // düşürmesin, stockCode'daki bilinçli onarım felsefesiyle aynı. Kırpılır ama yeniden yazılmaz.
        return new TrendyolRemoteListingState(
            Quantity: remoteVariant.Quantity,
            ListPrice: remoteVariant.ListPrice,
            SalePrice: remoteVariant.SalePrice,
            Archived: flags?.Archived,
            Locked: flags?.Locked,
            LockReason: TruncateOptional(flags?.LockReason, TrendyolProductConsts.RemoteReasonMaxLength),
            Blacklisted: flags?.Blacklisted,
            BlacklistReason: TruncateOptional(flags?.BlacklistReason, TrendyolProductConsts.RemoteReasonMaxLength),
            Rejected: flags?.Rejected,
            RejectReason: TruncateOptional(flags?.RejectReason, TrendyolProductConsts.RemoteReasonMaxLength),
            HasActiveCampaign: flags?.HasActiveCampaign,
            ProductUrl: SafeHttpUrl(TruncateOptional(flags?.ProductUrl, TrendyolProductConsts.RemoteProductUrlMaxLength)),
            CreatedAtUtc: flags?.CreatedAtUtc,
            UpdatedAtUtc: flags?.UpdatedAtUtc,
            AxisValues: axisValues);
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

    /// <summary>Dış kaynaklı adres UI'da href'e basılır — yalnız http(s) mutlak URL geçer; aksi (javascript:/data:/
    /// göreli) <c>null</c>'a düşer. Kaynak kimlik doğrulamalı API olsa da dış veri ham href'e girmez.</summary>
    private static string? SafeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;
    }

    /// <summary>Uzak görsel adresleri emniyeti: yalnız http(s) mutlak URL, en fazla ürün görsel sınırı kadar,
    /// her biri uzunluk sınırında (kolon nvarchar; taşan tek adres partiyi düşürmesin — stockCode felsefesi).
    /// Şema dışı adres (javascript:/data:) UI'da href'e basıldığından burada elenir.</summary>
    private static List<string> SafeRemoteImageUrls(IReadOnlyList<string>? urls)
    {
        if (urls is null)
        {
            return new List<string>();
        }

        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Where(u => u.Length <= TrendyolProductConsts.RemoteProductUrlMaxLength
                        && Uri.TryCreate(u, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .Take(ProductConsts.MaxImageCount)
            .ToList();
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
    /// <b>Stok — K12 politikası (2026-07-23 kesin karar):</b> core <c>StockQuantity</c> yalnız varyant BU importta
    /// doğarken seed'lenir (create yolu — <see cref="CreateTemplateProductAsync"/>/<see cref="EnsureTemplateVariantsAsync"/>);
    /// burada remote stok core'u ASLA EZMEZ. Core == remote → override null (fark yok, gürültü üretme);
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

            // OTORİTE DEVRİ (2026-08-05 Hakan kararı): ürün sınıflandırıldıysa (Calculated) pazaryerinde duran
            // stok ve fiyat GEÇERSİZDİR — ikisini de sistem belirler. Buraya remote değeri yazmak, her içe
            // aktarımda devri geri alırdı: push zinciri override'ı ÖNCELEDİĞİ için hesaplanan değer sessizce
            // gölgelenir ve kimse fark etmezdi.
            var authorityTransferred = product.StockPolicy == ProductStockPolicy.Calculated;

            var salePrice = authorityTransferred
                ? null
                : remoteVariant.SalePrice is >= 0 ? remoteVariant.SalePrice : null;
            var overrideStock = authorityTransferred
                ? null
                : ResolveOverrideStock(product, localVariant, remoteVariant.Quantity, report);

            if (headers.TryGetValue(localVariant.Id, out var header))
            {
                // TASARIM (kullanıcı onaylı yön — SalesChannelTrTrendyolProductImportTests'te pinli): CORE asla
                // ezilmez (ürün adı, ProductVariantDetail fiyatı korunur), uzak gerçek KANAL katmanına yazılır.
                // OverridePrice/OverrideStock kullanıcının rezerv alanı DEĞİL, pazaryerinin YANSIMASIDIR → her import'ta
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

    /// <summary>K12 stok politikasının karar noktası: remote stok (negatif → 0 clamp'li) core
    /// <see cref="EntityVariant.StockQuantity"/> ile AYNIYSA null döner (override yazılmaz — "fark yok" gürültüsüz);
    /// FARKLIYSA remote değer döner (kanal override'ı olur) + fark satır-bazında LogWarning + rapor sayacıyla
    /// görünür kılınır (sessiz geçilmez). BU importta doğan varyantın core'u remote'la seed'lendiğından
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

    /// <summary>Uzak barcode'ları yerel varyantlara TEK geçişte çözer (parça parça IN sorgusu).
    ///
    /// <para><b>Arama kapsamı unique index'le AYNI olmalıdır</b> — indeks <c>(TenantId, CompanyId, Barcode)</c>
    /// olduğundan burada da ŞİRKETE daraltılır. Kapsamlar ayrışırsa iki yönlü hata doğar: arama indeksten GENİŞSE
    /// meşru kalem "başkasının" sanılıp atlanır, DARSA barkod boş sanılıp insert ham unique ihlaliyle tüm içe
    /// aktarımı düşürür.</para>
    ///
    /// <para><b>Eskiden tenant genelinde arardı</b> (şirket filtresi bilerek kapalıydı) çünkü indeks de tenant
    /// genelindeydi ve başka şirketin barkodu ham DB ihlaline yol açıyordu; o yüzden "yabancı barkod" kümesi
    /// çıkarılıp kalem atlanıyordu. İndeks 2026-08-04'te şirkete daraltılınca (aynı tenant altındaki farklı
    /// şirketler aynı malı satabilmeli) o ağ ZARARLIYA dönüştü — artık meşru olan kalemleri atlardı — ve kaldırıldı.</para>
    ///
    /// <para><c>EntityName == "Product"</c> ZORUNLU: agnostik tablo TÜM entity'lerin (Good/Metal/…) varyantlarını
    /// tutar; barkod tekilliği yalnız ürün varyantlarını kapsar.</para></summary>
    private async Task<Dictionary<string, EntityVariant>> LoadVariantsByBarcodeAsync(Guid companyId, List<string> barcodes)
    {
        var owned = new Dictionary<string, EntityVariant>(StringComparer.OrdinalIgnoreCase);
        var distinct = barcodes
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        const int chunkSize = 500;
        foreach (var chunk in distinct.Chunk(chunkSize))
        {
            var variants = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName
                                && v.CompanyId == companyId
                                && v.Barcode != null
                                && chunk.Contains(v.Barcode)));
            foreach (var variant in variants)
            {
                owned[variant.Barcode!] = variant;
            }
        }

        return owned;
    }
}
