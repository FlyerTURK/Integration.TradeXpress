using System;
using System.Collections.Generic;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜNÜN SATIŞA HAZIRLIK PANELİ VERİSİ (2026-08-19) — "bu ürün neden satışta değil, sıradaki adım ne, nereye tıklayacağım?"
/// sorusunun TEK DTO'da cevabı. UI hesap yapmaz: adımlar, issue'lar, sayılar ve kanal satırları sunucuda
/// <c>ProductSaleReadinessBuilder</c> tarafından kurulur; kural iki yerde yaşamaz.
/// </summary>
public class ProductSaleReadinessDto
{
    public Guid ProductId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    /// <summary>Stok kaynağı — formda HİÇ görünmüyordu (2026-08-19 harita §4); <c>ProductSaleReadinessPanel</c> salt-okunur gösterir.</summary>
    public ProductStockPolicy StockPolicy { get; set; }

    public ProductVariantMode VariantMode { get; set; }

    public bool HasCategory { get; set; }

    /// <summary>Ürün KDV'si (kanal ürünü boşsa devralınır). KDV eksikliği ASLA engel değildir (Hakan 2026-08-19).</summary>
    public int? VatRate { get; set; }

    // ── Varyant sayaçları (aktif varyantlar üzerinden) ────────────────────────────────────────────────

    public int ActiveVariantCount { get; set; }

    /// <summary>Satış fiyatı girilmiş aktif varyant — fiyatsız varyant push aday setinden SESSİZCE elenir.</summary>
    public int PricedVariantCount { get; set; }

    /// <summary>En az bir reçete satırı olan aktif varyant.</summary>
    public int RecipeVariantCount { get; set; }

    /// <summary><c>VariantSaleReadinessResolver</c>'dan okunan satılabilir varyant (Ready ∧ <c>VerifiedRecipeStamp</c> güncel) — rozetten DEĞİL.</summary>
    public int SellableVariantCount { get; set; }

    /// <summary>Ready ama <c>VerifiedRecipeStamp</c>'i BAYAT (reçete onaydan sonra değişti) — rozet "Hazır" der, guard yine de eler.</summary>
    public int StaleVerifiedVariantCount { get; set; }

    public int DraftVariantCount { get; set; }

    public int SuspendedVariantCount { get; set; }

    // ── Görsel ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Push'un GÖNDEREBİLECEĞİ görsel sayısı (MarketplacePushImageResolver: varyant → kayıt
    /// fallback'li aday küme, en çok ProductConsts.MaxImageCount). 2026-08-21'e kadar yalnız kayıt-geneli
    /// bağlam sayılıyordu ve görseli varyantta olan ürün panelde "görselsiz" görünüyordu.</summary>
    public int ImageCount { get; set; }

    public bool HasPoster { get; set; }

    // ── Kontrol listesi + issue'lar + kanallar ───────────────────────────────────────────────────────

    /// <summary>Sıralı adımlar: kategori → varyant/fiyat → reçete → görsel → doğrulama → kanal ürünü → gönderim.</summary>
    public List<SaleReadinessStepDto> Steps { get; set; } = new();

    /// <summary>Tüm issue'lar (Error/Warning/Info), en ağırı önde.</summary>
    public List<SaleReadinessIssueDto> Issues { get; set; } = new();

    /// <summary>Kanal ürünü başına bir satır (ürünün hiç kanal ürünü yoksa boş).</summary>
    public List<ChannelReadinessRowDto> Channels { get; set; } = new();

    /// <summary>En az bir aktif varyant doğrulanabilir mi (Error taşımayan aktif varyant var).</summary>
    public bool CanVerify { get; set; }
}

/// <summary>Satışa hazırlık paneli kontrol listesi satırı — sunucu kurar, UI çizer.</summary>
public class SaleReadinessStepDto
{
    /// <summary>Sabit anahtar: <c>Category</c> · <c>Variants</c> · <c>Recipe</c> · <c>Images</c> · <c>Verification</c> ·
    /// <c>ChannelProducts</c> · <c>Push</c>. UI ikon/sıra için kullanabilir; metin <see cref="Title"/>'dadır.</summary>
    public string Key { get; set; } = string.Empty;

    public SaleReadinessStepState State { get; set; }

    /// <summary>Lokalize başlık ("Varyantlar fiyatlandı").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Lokalize detay ("3 aktif varyant · 1'i fiyatsız").</summary>
    public string? Detail { get; set; }

    /// <summary>Adımın düzeltme hedefi.</summary>
    public SaleReadinessFixTarget FixTarget { get; set; }

    /// <summary>Bu adıma bağlı issue sayısı (Error+Warning).</summary>
    public int IssueCount { get; set; }
}

/// <summary>Tek issue: ne, ne kadar ağır, hangi kayıtta, nereden düzeltilir.</summary>
public class SaleReadinessIssueDto
{
    public SaleReadinessSeverity Severity { get; set; }

    /// <summary>Makine kodu (ör. <c>Variant:NoSalePrice</c>, <c>Recipe:ZeroQuantity</c>, <c>Product:VatMissing</c>) —
    /// testler ve UI ikon eşlemesi bunu okur; metin lokalizedir.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Lokalize, kullanıcıya okunur mesaj.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Issue hangi kontrol listesi adımına ait (<see cref="SaleReadinessStepDto.Key"/>).</summary>
    public string StepKey { get; set; } = string.Empty;

    /// <summary>KAPSAM YOLU (<see cref="SaleReadinessScope"/>) — issue'nun İÇİNDE bulunduğu her seviyeyi taşır
    /// (ör. <c>channels/{kanalÜrünü}/variants/{varyant}/recipe</c>). Her sekme/panel "benim yolumla başlayan issue'ların
    /// en yüksek ağırlığı" ile renklenir; hangi sekmenin hangi issue'yu göstereceği UI'da KURALLAŞTIRILMAZ.</summary>
    public string Path { get; set; } = string.Empty;

    public SaleReadinessFixTarget FixTarget { get; set; }

    /// <summary>Hedef kaydın id'si (varyant / kanal ürünü). Ürün-düzeyi issue'da null.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Hedef kaydın etiketi (varyant kodu / kanal kodu) — listede "RED: fiyat yok" gibi okunsun.</summary>
    public string? TargetLabel { get; set; }

    /// <summary>Kanal ürünü issue'sunda kanal türü; diğerlerinde null.</summary>
    public SalesChannelType? ChannelType { get; set; }
}

/// <summary>Kanal ürünü satırı — <c>ProductSaleReadinessPanel</c> ve kanal aksiyon bileşeni bunu okur.</summary>
public class ChannelReadinessRowDto
{
    public SalesChannelType ChannelType { get; set; }

    public Guid ChannelProductId { get; set; }

    public Guid SalesChannelId { get; set; }

    public string SalesChannelCode { get; set; } = string.Empty;

    public string SalesChannelName { get; set; } = string.Empty;

    /// <summary>Kanal ürünü aktif mi (Trendyol'da = arşivde DEĞİL).</summary>
    public bool IsActive { get; set; }

    /// <summary>Ürün hiç kanala ulaştı mı (Trendyol: SKU var / N11: SKU ya da N11ProductId / Etsy: ListingId).</summary>
    public bool IsListed { get; set; }

    /// <summary>Gönderim/senkron hâlâ işleniyor (Trendyol PROCESSING · N11 bekleyen task).</summary>
    public bool IsPending { get; set; }

    /// <summary>Kanalın ham durum metni (Trendyol batch Status/STALE, N11 "Satış/Onay", Etsy listing state).</summary>
    public string? StatusText { get; set; }

    /// <summary>N11'de çözülmemiş kuyruk task'ı (varsa) — "Kuyruk sonucunu sorgula" bunu ister.</summary>
    public string? PendingTaskId { get; set; }

    /// <summary>Trendyol batch id (varsa) — "Durumu Yenile" bunu ister.</summary>
    public string? BatchRequestId { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>PushHistory'deki son BAŞARILI gönderim anı (yoksa null).</summary>
    public DateTime? LastPushedAt { get; set; }

    /// <summary>Kanal ürününün satışa-hazırlık kademesi (reçete var mı · satılabilir varyant var mı).</summary>
    public ChannelProductReadiness Readiness { get; set; }

    /// <summary>Pazaryeri engeli (karaliste/kilit) — kanalın kendi cümlesi; yoksa null.</summary>
    public string? Obstacle { get; set; }

    // ── Aksiyon uygunluğu — UI düğmeleri bunlara göre açar/kapatır, kuralı kendisi türetmez ──

    public bool CanPush { get; set; }

    public bool CanSyncStockPrice { get; set; }

    public bool CanRefreshStatus { get; set; }

    public bool CanResolveQueue { get; set; }

    public bool CanToggleArchive { get; set; }
}
