using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol ürün istemcisi — Trendyol Marketplace API v2 (REST/JSON, apigw.trendyol.com). Ürün oluşturma ASENKRON:
/// <see cref="SubmitProductAsync"/> bir <c>batchRequestId</c> döner; sonuç ayrıca <see cref="GetBatchStatusAsync"/>
/// ile sorgulanır. Model ÇÖZÜLMÜŞ gelir; client yalnız JSON serialize/parse eder. Kimlik tipi TEK kaynak:
/// <see cref="TrendyolCredentials"/> (Integration.TradeXpress.Trendyol — kategori/marka istemcileriyle AYNI record;
/// eski yerel kopya kaldırıldı, çift-record tuzağı kapandı).
/// <para><b>Canlı doğrulama (2026-08-16):</b> create (<see cref="SubmitProductAsync"/>) + batch sorgusu
/// (<see cref="GetBatchStatusAsync"/>) + satıcı ürünleri GET gerçek hesapla uçtan uca KANITLANDI (ilk listeleme
/// COMPLETED). Aynı testte "currencyType/cargoCompanyId şemada yok" varsayımı çürüdü (bkz. <see cref="TrendyolProductData"/>).
/// <see cref="DeleteProductsAsync"/> resmî dokümana göre yazıldı, ilk canlı çağrısı bekliyor.</para>
/// </summary>
public interface ITrendyolProductClient
{
    /// <summary>Ürünü Trendyol'a gönderir (async create). Batch id döner; başarısızsa BusinessException fırlatır.</summary>
    Task<TrendyolSubmitResult> SubmitProductAsync(TrendyolProductData product, TrendyolCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Fiyat + stok HAFİF güncellemesi — satırlar yalnız <c>barcode</c> ile adreslenir, ürün İÇERİĞİNE
    /// (başlık/görsel/attribute) DOKUNMAZ. Ürün oluşturma gibi ASENKRON: <c>batchRequestId</c> döner, sonuç
    /// <see cref="GetBatchStatusAsync"/> ile sorgulanır.
    ///
    /// <para><b>null alan = "bu alana dokunma"</b> (JSON'a hiç yazılmaz, uzak değer korunur); <b><c>0</c> meşru bir
    /// değerdir</b> (stok sıfırlama = satışı durdurma yolu). İkisini karıştırmak sessizce ya stoğu sıfırlar ya da
    /// sıfırlamayı yutar.</para></summary>
    Task<TrendyolSubmitResult> UpdatePriceAndInventoryAsync(IReadOnlyList<TrendyolPriceInventoryItem> items, TrendyolCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Bir batch isteğinin durumunu sorgular (COMPLETED/FAILED + başarısız kalem gerekçeleri).</summary>
    Task<TrendyolBatchStatus> GetBatchStatusAsync(string batchRequestId, TrendyolCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Ürünleri Trendyol'dan SİLER (barcode listesiyle; <c>DELETE /integration/product/sellers/{sellerId}/products</c>,
    /// gövde <c>{items:[{barcode}]}</c>). ASENKRON: batch id döner. Trendyol yalnız ONAY BEKLEYEN ürünleri ve bir günden
    /// eski ARŞİVLENMİŞ ürünleri siler — onaylı/satıştaki ürünü doğrudan silmez (önce arşiv); red gerekçesi batch
    /// sonucundan okunur. HTTP başarısızsa BusinessException (kanalın gövdesiyle).</summary>
    Task<TrendyolSubmitResult> DeleteProductsAsync(IReadOnlyList<string> barcodes, TrendyolCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Satıcının Trendyol'daki ürünlerinin BİR SAYFASINI çeker (salt GET — pazaryerine SIFIR yazma).
    /// Sayfa öğeleri DÜZ kalemlerdir (barcode başına bir kayıt); productMainId gruplaması
    /// <see cref="GetAllSellerProductsAsync"/>'te yapılır.</summary>
    Task<TrendyolSellerProductsPage> GetSellerProductsAsync(TrendyolCredentials credentials, int page, int size, CancellationToken cancellationToken = default);

    /// <summary>TÜM satıcı ürünlerini sayfa sayfa çekip (totalPages döngüsü) <c>productMainId</c>'ye göre GRUPLAR:
    /// aynı productMainId'li kalemler tek <see cref="TrendyolRemoteProduct"/>'ın varyantları olur (productMainId boş
    /// kalem kendi başına ürün sayılır). Salt GET.</summary>
    Task<IReadOnlyList<TrendyolRemoteProduct>> GetAllSellerProductsAsync(TrendyolCredentials credentials, int pageSize = 200, CancellationToken cancellationToken = default);
}

/// <summary>Trendyol ürün verisi (ÇÖZÜLMÜŞ) — ürün başlığı/kategori/marka + varyantlar (Items) + kategori attribute'ları.
///
/// <para><b>DÜZELTME (2026-08-16, ilk CANLI gönderim):</b> bu özet daha önce "<c>currencyType</c>/<c>cargoCompanyId</c>
/// V2 create şemasında YOKTUR" diyordu — YANLIŞTI. Trendyol ilk gerçek create'i
/// <c>productRequest.currencyType.null — "Para Birimi alanı boş olamaz"</c> ile REDDETTİ. Alan zorunludur ve gövdeye
/// <c>currencyType: "TRY"</c> yazılır (Trendyol yalnız TRY kabul eder — para birimi karışımı zaten fail-fast).
/// Kargo firması da gövde alanıdır (<c>cargoCompanyId</c>): kanalın varsayılan kargo firması gönderilir
/// (<see cref="CargoCompanyId"/>; null ise alan yazılmaz — Trendyol'un satıcı varsayılanına düşer).</para></summary>
public sealed record TrendyolProductData(
    string ProductMainId,     // varyantları gruplar ("{ÜrünKodu}-{SequenceNo}", frozen)
    string Title,
    string Description,
    string CategoryId,        // numerik
    string BrandId,           // numerik
    // KDV — kanal→ürün devralma zincirinden çözülmüş EFEKTİF oran. Önizlemede null olabilir; PUSH yolunda
    // yukarıda fail-fast atılır (sessiz %20 varsayımı 2026-08-03'te kaldırıldı: kıymetli maden %0'dır).
    int? VatRate,
    decimal? DimensionalWeight,
    int? DeliveryDuration,
    TrendyolFastDeliveryType? FastDeliveryType,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<TrendyolAttributeValue> Attributes,   // kategori attribute (id-bazlı)
    IReadOnlyList<TrendyolProductItem> Items,           // varyantlar (barcode başına)
    // ImageUrls'e FİİLEN giren görsellerin DAM kimlikleri (aynı sıra) — delil defteri "ne gönderdim"i BUNDAN
    // yazar; adayları yeniden çözerek değil (yüklenemeyen görsel deftere "gönderildi" düşerdi). Gövdeye girmez.
    IReadOnlyList<Guid> SentMediaIds,
    // Kanalın varsayılan kargo firması (Trendyol cargoCompanyId) — null ise gövdeye yazılmaz.
    int? CargoCompanyId = null);

/// <summary>Trendyol satılabilir kalem (= ERP varyantı) — barcode + stok + fiyat (para birimi TRY zımnî).
/// <para><see cref="Attributes"/> = kalemin KENDİ (varianter/eksen) attribute'ları — gövdede ürün-seviyesi
/// niteliklerin ÜZERİNE yazılır (aynı attributeId'de kalem kazanır: özgül olan geneli yener). Kaynak gerçek
/// push'ta doğrulayıcı çıktısıdır: import fotoğrafı (<c>RemoteVariantAttributes</c>) öncelikli, yoksa ERP/kanal
/// çiftlerinden kategori tanımına karşı ad→id türetimi (T6/T8, 2026-08-14); önizlemede yalnız fotoğraf.</para></summary>
public sealed record TrendyolProductItem(
    string Barcode,
    string StockCode,
    int Quantity,
    decimal ListPrice,
    decimal SalePrice,
    IReadOnlyList<TrendyolAttributeValue>? Attributes = null,
    IReadOnlyList<(string Name, string Value)>? OptionLabels = null);
// OptionLabels GÖVDEYE GİRMEZ — delil defterinin okunur "Ad=Değer" çiftleri (N11'de payload'un kendisi ad
// taşıdığı için ayrı alan gerekmez; Trendyol id-bazlı olduğundan okunur biçim burada yanında taşınır).

/// <summary>Trendyol HAFİF fiyat/stok satırı — kimlik <c>barcode</c> (stok kodu DEĞİL; ikisi Trendyol'da
/// farklı olabilir ve karıştırmak başka bir SKU'nun stoğunu ezer).
///
/// <para>Dört alan da BAĞIMSIZ gönderilebilir: <c>null</c> = gövdeye yazılmaz (uzak değer korunur), dolu = yazılır.
/// Nullable'lık burada süs değil, kuralın MEKANİK karşılığıdır — alanlar non-nullable olsaydı varsayılan
/// <c>0</c>/<c>0,00</c> sessizce stoğu ve fiyatı sıfırlardı (N11 tarafında aynı gerekçe).</para></summary>
public sealed record TrendyolPriceInventoryItem(string Barcode, int? Quantity, decimal? ListPrice, decimal? SalePrice);

/// <summary>Trendyol attribute değeri (id-bazlı) — value id ile listeden ya da customValue ile serbest.</summary>
public sealed record TrendyolAttributeValue(int AttributeId, int? AttributeValueId, string? CustomValue);

/// <summary>Submit yanıtı — Trendyol'un döndürdüğü batch istek kimliği (durum bununla sorgulanır).</summary>
public sealed record TrendyolSubmitResult(string? BatchRequestId);

/// <summary>Batch durum sorgusu sonucu — durum + kalem sayısı + başarısız kalem gerekçeleri (birleştirilmiş).</summary>
public sealed record TrendyolBatchStatus(string? Status, int ItemCount, int FailedCount, string? FailureReasons);

// ── Satıcı ürün OKUMA modeli (import; salt GET) ─────────────────────────────────────────────────────

/// <summary>Uzak (Trendyol'daki) ürün kaydının kategori attribute değeri — id-bazlı; value id yoksa serbest metin
/// <see cref="CustomValue"/>'da taşınır. Ad/değer metinleri yalnız rapor/görüntü kolaylığı.</summary>
public sealed record TrendyolRemoteAttribute(
    int AttributeId,
    string? AttributeName,
    int? AttributeValueId,
    string? AttributeValue,
    string? CustomValue);

/// <summary>Uzak ürünün BİR satılabilir kalemi (barcode başına) — import'un idempotency anahtarı
/// <see cref="Barcode"/>'dur. <see cref="ProductContentId"/> = Trendyol içerik kimliği (content-bulk-update).</summary>
public sealed record TrendyolRemoteVariant(
    string Barcode,
    string? StockCode,
    int Quantity,
    decimal? ListPrice,
    decimal? SalePrice,
    long? ProductContentId,
    bool? Approved,
    bool? OnSale,
    IReadOnlyList<TrendyolRemoteAttribute> Attributes,
    TrendyolRemoteListingFlags? Flags = null);

/// <summary>
/// Pazaryerinin kalem hakkındaki ENGEL/DURUM beyanı — <c>approved</c>/<c>onSale</c>'in söylemediği kısım.
///
/// <para><b>Neden ayrı bir kayıt:</b> bu alanlar aylarca yanıtta GELİYOR ama okunmuyordu. Sonuç sessizdi ve
/// pahalıydı: karalisteye alınmış ya da kilitlenmiş bir kalem bizde "onaylı ve satışta" görünüyor, gönderim
/// denemesi karşı tarafta reddediliyor, sebebi ancak hata metninden — o da defter kurulduktan sonra —
/// anlaşılıyordu. Canlı ölçüm bunun teorik olmadığını gösterdi: 19 kalemlik grubun TAMAMI <c>blacklisted</c>,
/// dördü ayrıca <c>locked</c>.</para>
///
/// <para><b>Üç durumlu okunur:</b> <c>null</c> = "pazaryeri bu alanı bildirmedi", <c>false</c> = "engel yok"
/// beyanı. İkisini birleştirmek, bildirilmeyen bir engeli "engel yok" diye yazmak olurdu.</para>
/// </summary>
public sealed record TrendyolRemoteListingFlags(
    bool? Archived,
    bool? Locked,
    string? LockReason,
    bool? Blacklisted,
    string? BlacklistReason,
    bool? Rejected,
    string? RejectReason,
    bool? HasActiveCampaign,
    string? ProductUrl,
    DateTime? CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>Uzak (Trendyol'daki) satıcı ürünü — <c>productMainId</c> ile gruplanmış varyant seti + ortak alanlar.
/// <see cref="ProductMainId"/> TRENDYOL'un grup anahtarıdır (satıcının kendi girdiği değer olabilir) — bizim
/// ürettiğimiz kayıt-bazlı <c>SalesChannelTrTrendyolProduct.ProductMainId</c>'den AYRI kavram; import'ta
/// <c>RemoteProductMainId</c> alanına yazılır.</summary>
public sealed record TrendyolRemoteProduct(
    string? ProductMainId,
    string Title,
    string? Description,
    string? CategoryId,
    string? CategoryName,
    string? BrandId,
    string? BrandName,
    int? VatRate,
    decimal? DimensionalWeight,
    int? DeliveryDuration,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<TrendyolRemoteVariant> Variants);

/// <summary>Satıcı ürün listeleme sayfası — sayfalama zarfı + DÜZ kalemler (her öğe tek-varyantlı
/// <see cref="TrendyolRemoteProduct"/>; gruplama <see cref="ITrendyolProductClient.GetAllSellerProductsAsync"/>'te).</summary>
public sealed record TrendyolSellerProductsPage(
    int Page,
    int Size,
    int TotalPages,
    long TotalElements,
    IReadOnlyList<TrendyolRemoteProduct> Items);
