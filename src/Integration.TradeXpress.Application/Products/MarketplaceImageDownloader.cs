using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Pazaryeri (Etsy/Trendyol/N11) importunda uzak görselleri merkezi DAM'a İNDİREN ortak boru hattı (DRY):
/// her URL <see cref="IMediaAppService.ImportFromUrlAsync"/> ile kütüphaneye alınır (self-contained blob;
/// ContentHash dedup — aynı görsel ikinci kez İNDİRİLMEZ, mevcut medyaya link'lenir) ve hedef kaydın medya
/// bağlamına link seti olarak yazılır.
///
/// <para><b>İKİ BAĞLAM, TEK <c>ImportAsync</c></b> (CLAUDE.md §6 "her medya tipi İKİ bağlamı da taşır"): kayıt geneli
/// <see cref="MediaEntityNames.Product"/> (<see cref="ImportToProductAsync"/>), varyant farkı
/// <see cref="MediaEntityNames.ProductVariant"/> (<see cref="ImportToVariantAsync"/>). İkisi de AYNI
/// indir-dedup-bağla akışını (<c>ImportAsync</c>) paylaşır — kopya akış iki bağlamın zamanla ayrışmasına ve
/// yalnız birinin düzeltilmesine yol açardı.</para>
///
/// <para><b>EKLEMELİ — MEVCUT BAĞLAR EZİLMEZ</b> (2026-08-20 Hakan talimatı): hedefin mevcut bağları okunur,
/// pazaryerinden gelenler ÜSTÜNE eklenir ve birleşik liste tek
/// <see cref="IEntityMediaAppService.ReplaceForAsync"/> çağrısıyla yazılır. Zaten bağlı medya (aynı
/// <c>MediaId</c>) İKİNCİ kez eklenmez → tekrar eden içe aktarım idempotenttir.</para>
///
/// <para><b>Bu sözleşme FİİLEN varyant bağlamında yük taşır:</b> varyant görselleri HER içe aktarım turunda
/// yazılır, yani hedefte kullanıcının elle bağladığı görsel BULUNABİLİR — eklemeli olmasaydı o bağ her turda
/// kopardı. Kayıt geneli bağlam ise yalnız ürün KURULURKEN (bir de Etsy'nin "galeri tamamen boşsa doldur"
/// yolunda) yazılır; orada set tanım gereği boştur. Yani kayıt genelinde eklemeli davranış YAŞANMIŞ bir hatayı
/// düzeltmez — çağrı yolları (N11/Trendyol yalnız kuruluş, Etsy yalnız boşsa) galeriyi ezmeye zaten izin
/// vermiyordu; buradaki eklemelilik, çağıran ileride her tura açılırsa kullanıcı bağını koruyan emniyettir.</para>
///
/// <para><b>Cover (<c>EntityMediaLink.IsDefault</c>):</b> hedefte cover varsa DEĞİŞMEZ (kullanıcının seçimi
/// pazaryerinin sırasına feda edilmez); hiç bağ yoksa ilk BAŞARILI görsel cover olur.</para>
///
/// <para><b>Dayanıklılık:</b> indirme/guard BAŞARISIZSA o görsel ATLANIR + warning loglanır (import DURMAZ —
/// tek bozuk görsel tüm importu öldürmesin). Legacy'deki "URL-kaynağına düş" davranışı bilinçli KALKTI:
/// DAM'da içerik daima blob'dadır (URL saklanmaz) ve push yalnız DAM'dan okur — indirilemeyen görselin URL'ini
/// taşımak onu hiçbir ekranda görünmez kılardı (sahte başarı).</para>
///
/// <para><b>Neden <c>sealed</c> değil:</b> indirme adımı (<see cref="TryImportAsync"/>) türetilebilir olsun ki
/// testler ağa çıkmadan boru hattının tamamını koşturabilsin. Üretimde türeten yoktur.</para>
/// </summary>
public class MarketplaceImageDownloader : ITransientDependency
{
    private readonly IMediaAppService _media;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly ILogger<MarketplaceImageDownloader> _logger;

    public MarketplaceImageDownloader(
        IMediaAppService media,
        IEntityMediaAppService entityMedia,
        ILogger<MarketplaceImageDownloader> logger)
    {
        _media = media;
        _entityMedia = entityMedia;
        _logger = logger;
    }

    /// <summary>Uzak görselleri ürünün KAYIT GENELİ ("Product") medya bağlamına ekler. Mevcut bağlar korunur,
    /// yalnız yeni gelenler eklenir (ortak <c>ImportAsync</c> sözleşmesi).</summary>
    public virtual async Task<MarketplaceImageImportResult> ImportToProductAsync(
        Product product, IReadOnlyList<string> imageUrls)
    {
        return await ImportAsync(
            MediaEntityNames.Product,
            product.Id,
            product.CompanyId,
            product.Code,
            imageUrls);
    }

    /// <summary>Uzak görselleri VARYANTIN kendi ("ProductVariant") medya bağlamına ekler — pazaryeri görseli
    /// kalem başına verdiğinde (Trendyol barkod-başına, N11 satır-başına) görselin doğru yeri burasıdır.
    ///
    /// <para>Varyant eşleşmesi ÇAĞIRANIN işidir (import zaten uzak kalemi ERP varyantına bağlıyor); burada
    /// ikinci bir eşleştirme mantığı YOKTUR — iki ayrı eşleştirme zamanla ayrışır ve görsel yanlış varyanta
    /// düşerdi.</para>
    ///
    /// <para>Kütüphane adı ürün + varyant kodundan türetilir ("{ÜrünKodu}-{VaryantKodu}-{sıra}") — kayıt geneli
    /// desenin varyant karşılığı; kütüphanede hangi görselin hangi varyanta ait olduğu ADINDAN okunur.</para></summary>
    public virtual async Task<MarketplaceImageImportResult> ImportToVariantAsync(
        Guid variantId,
        Guid companyId,
        string productCode,
        string variantCode,
        IReadOnlyList<string> imageUrls)
    {
        return await ImportAsync(
            MediaEntityNames.ProductVariant,
            variantId,
            companyId,
            $"{productCode}-{variantCode}",
            imageUrls);
    }

    /// <summary>İki bağlamın ORTAK akışı: URL setini tekilleştir → kapasite kadarını indir → hedefte zaten
    /// bağlı OLMAYANLARI mevcutların sonuna ekle → birleşik listeyi tek çağrıda yaz.
    ///
    /// <para><b>Sınır birleşik listeye uygulanır</b> (<see cref="ProductConsts.MaxImageCount"/>): taşma olursa
    /// YENİ gelenler kırpılır, mevcutlar durur — kullanıcının görseli pazaryeri görseline yer açmak için
    /// SİLİNMEZ. Kırpma SESSİZ değildir: warning loglanır, dönüş değerinde sayılır ve ÜÇ çağıranın da içe aktarım
    /// raporuna taşınır (<c>SkippedImages</c> + tek satırlık lokalize uyarı). Sayıyı yalnız log'a bırakmak,
    /// kullanıcıya "import başarılı" deyip fotoğrafın neden gelmediğini söylememek olurdu.</para>
    ///
    /// <para>Hiç yeni görsel eklenmiyorsa <c>ReplaceForAsync</c> HİÇ çağrılmaz — gereksiz sil+yaz turu (link
    /// Id'lerinin dönmesi dahil) yalnız gerçek bir değişiklik varken göze alınır.</para></summary>
    private async Task<MarketplaceImageImportResult> ImportAsync(
        string entityName,
        Guid entityId,
        Guid? companyId,
        string libraryNamePrefix,
        IReadOnlyList<string> imageUrls)
    {
        var urls = NormalizeUrls(imageUrls);
        if (urls.Count == 0)
        {
            return MarketplaceImageImportResult.Empty;
        }

        // Korunacak TABAN: mevcut bağların sırası ve cover (IsDefault) seçimi olduğu gibi taşınır. (Kütüphaneden silinmiş
        // medyaya işaret eden yetim bağlar GetFor'da zaten elenir — ölü bağı geri yazmanın anlamı yok.)
        var existing = await _entityMedia.GetForAsync(entityName, entityId);
        var linkedMediaIds = existing.Select(l => l.MediaId).ToHashSet();

        var added = new List<EntityMediaLinkEditDto>();
        var alreadyLinked = 0;
        var skippedForCapacity = 0;

        foreach (var url in urls)
        {
            if (existing.Count + added.Count >= ProductConsts.MaxImageCount)
            {
                // Kapasite dolu → İNDİRME BİLE YAPILMAZ (ağ/blob maliyeti boşa gitmesin).
                skippedForCapacity++;
                continue;
            }

            // Kütüphane adı HEDEFTEN türetilir ("{ön-ek}-{sıra}"). Ad verilmezse URL'nin son parçası kalıyordu ve
            // N11/Trendyol CDN adları ("1_org_zoom.jpg") kütüphaneyi anlamsız dolduruyordu (2026-08-07 Hakan
            // tespiti). Uzantı URL'den korunur. NOT: içerik dedup'ı ContentHash'ledir — aynı görsel zaten
            // kütüphanedeyse MEVCUT kayıt (eski adıyla) kullanılır, ad yeniden yazılmaz.
            var media = await TryImportAsync(
                url,
                BuildLibraryFileName(libraryNamePrefix, existing.Count + added.Count + 1, url));
            if (media is null)
            {
                continue;
            }

            if (!linkedMediaIds.Add(media.Id))
            {
                // Aynı içerik bu kayda ZATEN bağlı (ContentHash dedup'ı aynı MediaId'yi döndürdü) → ikinci bağ
                // açılmaz. Tekrarlanan içe aktarımın idempotent olmasının tek sebebi budur.
                alreadyLinked++;
                continue;
            }

            added.Add(new EntityMediaLinkEditDto
            {
                MediaId = media.Id,
                DisplayOrder = existing.Count + added.Count,
                IsDefault = false,   // cover kararı birleşik listede verilir (mevcut cover korunur)
                IsActive = true,
            });
        }

        if (skippedForCapacity > 0)
        {
            _logger.LogWarning(
                "Pazaryeri görselleri sınıra takıldı ({Limit}) — {SkippedCount} yeni görsel bağlanmadı: {EntityName}/{EntityId}. Mevcut bağlar korundu.",
                ProductConsts.MaxImageCount,
                skippedForCapacity,
                entityName,
                entityId);
        }

        if (added.Count == 0)
        {
            return new MarketplaceImageImportResult(0, alreadyLinked, skippedForCapacity);
        }

        await _entityMedia.ReplaceForAsync(entityName, entityId, companyId, BuildCombinedLinks(existing, added));
        return new MarketplaceImageImportResult(added.Count, alreadyLinked, skippedForCapacity);
    }

    /// <summary>Mevcut + yeni bağların birleşik listesi: mevcutlar ÖNCE (sıraları korunur), yeniler sonda;
    /// <c>DisplayOrder</c> 0..n-1 yeniden numaralanır. Cover KURALI: hedefte cover (<c>IsDefault</c>) varsa aynen
    /// korunur — yoksa (ilk içe aktarım) ilk sıradaki bağ cover olur.</summary>
    private static List<EntityMediaLinkEditDto> BuildCombinedLinks(
        List<EntityMediaLinkEditDto> existing, List<EntityMediaLinkEditDto> added)
    {
        var combined = new List<EntityMediaLinkEditDto>(existing.Count + added.Count);
        foreach (var link in existing.Concat(added))
        {
            combined.Add(new EntityMediaLinkEditDto
            {
                MediaId = link.MediaId,
                DisplayOrder = combined.Count,
                IsDefault = link.IsDefault,
                IsActive = link.IsActive,
            });
        }

        if (!combined.Any(l => l.IsDefault))
        {
            combined[0].IsDefault = true;
            combined[0].IsActive = true;   // VARSAYILAN medya pasif OLAMAZ
        }

        return combined;
    }

    /// <summary>URL setini işlenebilir hâle getirir: boşlar elenir, kırpılır, OrdinalIgnoreCase tekilleştirilir.
    /// Sayı KIRPMASI burada YAPILMAZ — sınır mevcut bağlarla birlikte, birleşik listede uygulanır.</summary>
    private static List<string> NormalizeUrls(IReadOnlyList<string> imageUrls)
    {
        return imageUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Tek URL'yi kütüphaneye import eder. Herhangi bir hatada (ağ/timeout/SSRF guard/bozuk içerik)
    /// null döner + warning loglar — çağıran görseli atlar, import devam eder.</summary>
    protected virtual async Task<MediaDto?> TryImportAsync(string url, string fileName)
    {
        try
        {
            return await _media.ImportFromUrlAsync(new MediaImportUrlDto { Url = url, FileName = fileName });
        }
        catch (Exception ex)
        {
            // Tek bozuk/erişilemez görsel TÜM importu düşürmesin — atla, uyarı server-log'a (Blazor Server).
            _logger.LogWarning(ex, "Pazaryeri görseli DAM'a import edilemedi, atlanıyor: {ImageUrl}", url);
            return null;
        }
    }

    /// <summary>Kütüphane görünen adı: "{ön-ek}-{sıra}{uzantı}" (ön-ek ürün kodu ya da "{ÜrünKodu}-{VaryantKodu}").
    /// Uzantı URL'den korunur (tip algısı uzantıdan çalışır); URL uzantısızsa ".jpg" varsayılır (pazaryeri
    /// CDN'leri fiilen JPEG servis eder).</summary>
    private static string BuildLibraryFileName(string namePrefix, int order, string url)
    {
        var extension = ".jpg";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var fromUrl = System.IO.Path.GetExtension(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fromUrl))
            {
                extension = fromUrl;
            }
        }

        return $"{namePrefix}-{order}{extension}";
    }
}

/// <summary>
/// Pazaryeri görsel içe aktarımının SONUCU — eskiden yalnız "kaç tane indi" (int) dönüyordu ve sınır kırpması
/// SESSİZDİ: kullanıcının bağları doluyken gelen pazaryeri görselleri hiçbir yerde görünmeden düşerdi.
/// </summary>
/// <param name="ImportedCount">Bu turda YENİ bağlanan görsel sayısı (0 = hedefin link seti değişmedi).</param>
/// <param name="AlreadyLinkedCount">İnen ama hedefe ZATEN bağlı olduğu için tekrar bağlanmayan görsel sayısı.</param>
/// <param name="SkippedForCapacityCount"><see cref="ProductConsts.MaxImageCount"/> dolduğu için hiç indirilmeyen
/// görsel sayısı. Mevcut (kullanıcı) bağlarını korumak uğruna ödenen bedel. ÇAĞIRANIN SORUMLULUĞU: bu sayı içe
/// aktarım raporunun <c>SkippedImages</c> alanına eklenir ve rapor kapanırken tek satırlık lokalize uyarıya
/// dönüşür (N11/Trendyol/Etsy içe aktarımlarında kurulu) — yutulursa kırpma yeniden sessizleşir.</param>
public sealed record MarketplaceImageImportResult(
    int ImportedCount,
    int AlreadyLinkedCount,
    int SkippedForCapacityCount)
{
    public static readonly MarketplaceImageImportResult Empty = new(0, 0, 0);

    public override string ToString()
    {
        return $"Imported={ImportedCount}, AlreadyLinked={AlreadyLinkedCount}, SkippedForCapacity={SkippedForCapacityCount}";
    }
}
