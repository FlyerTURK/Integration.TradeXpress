using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Substitutions;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol KATEGORİ attribute değeri (id-bazlı; attributeValueId ile listeden ya da customValue ile serbest).
/// Ad "CategoryAttribute" (N11 sözlüğüyle hizalı, S6 rename): kombinasyon üreten
/// <see cref="SalesChannelTrTrendyolProductAttributeDto"/>'dan tamamen ayrıdır.</summary>
public class SalesChannelTrTrendyolProductCategoryAttributeDto
{
    public int AttributeId { get; set; }
    public int? AttributeValueId { get; set; }
    public string? CustomValue { get; set; }
}

/// <summary>Varyant Trendyol SKU kimlik/durum satırı (read-only; push + reconcile sonrası dolar). Barcode DONDURULMUŞ;
/// AttributeSnapshot UI'a taşınmaz (yeniden-bağlama imzası sunucu-içi kalır).</summary>
public class SalesChannelTrTrendyolProductSkuDto
{
    public Guid ProductVariantId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public long? RemoteContentId { get; set; }
    public int? LastSentQuantity { get; set; }
    public decimal? LastSentListPrice { get; set; }
    public decimal? LastSentSalePrice { get; set; }
}

/// <summary>Trendyol kanal-özel varyant override graf düğümü — ERP varyantının (SSOT: kod/ad/ERP fiyat/stok) Trendyol-scope
/// özelleştirmesi. LEFT JOIN: ERP varyant seti ⋈ kaydedilmiş kanal override. null override alanı = ERP'den devralınır.
/// Reçete (<see cref="RecipeLines"/>) kaydedilmişse ondan, yoksa ERP reçetesinden KLONLANIR (Id boş = henüz persist yok).
/// <see cref="NetCost"/>/<see cref="DerivedPrice"/> SALT-OKUNUR (GetAsync canlı hesaplar; save yoksayar).</summary>
public class SalesChannelTrTrendyolProductStockItemGraphDto
{
    /// <summary>Override BAŞLIĞININ kendi id'si (anchor budur — N11 portu) — SALT-OKUNUR kimlik, round-trip bununla
    /// yapılır. Özellik-kaynaklı (kartezyen) satırlarda ZORUNLU dolu (reconcile server-side üretir, client yeni satır
    /// açamaz); henüz persist edilmemiş/legacy düğümde <c>Guid.Empty</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>ERP varyantı — id-only, OPSİYONEL. Özellik-kaynaklı satırlarda yalnız fiyat/stok FALLBACK kaynağı
    /// (reconcile anahtarı DEĞİL — bkz. <see cref="SalesChannelTrTrendyolProductAttributeDto"/>); null = Trendyol-only
    /// kombinasyon (ERP'de karşılığı yok — Trendyol kendi özelliğinde sonradan eklenen bir değerden doğdu).</summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Kombinasyonu oluşturan özellik değerlerinin SALT-OKUNUR görüntüsü (ör. "Renk: Kırmızı; Beden: M") —
    /// yalnız özellik-kaynaklı (kartezyen) satırlarda dolu; legacy ERP-doğrudan satırda boş (VariantCode/Name kullanılır).</summary>
    public string CombinationLabel { get; set; } = string.Empty;

    /// <summary>SALT-OKUNUR türetilmiş bayrak: <c>true</c> = ERP varyantından izleniyor, <c>false</c> = Trendyol-only
    /// (ERP karşılığı yok; <see cref="OverridePrice"/>/<see cref="OverrideStock"/> ZORUNLUdur).</summary>
    public bool IsErpBacked => ProductVariantId.HasValue;

    /// <summary>ERP varyant kodu (SALT-OKUNUR görüntü; ERP SSOT).</summary>
    public string VariantCode { get; set; } = string.Empty;

    /// <summary>ERP varyant adı (SALT-OKUNUR görüntü; ERP SSOT).</summary>
    public string VariantName { get; set; } = string.Empty;

    /// <summary>Kanal-özel mutlak fiyat (opsiyonel; null = ERP/türetilmiş devralınır).</summary>
    public decimal? OverridePrice { get; set; }

    /// <summary>Override fiyatı para birimi (id-only; fiyat null ise yoksayılır).</summary>
    public Guid? OverridePriceCurrencyUnitId { get; set; }

    /// <summary>Kanal-özel stok (opsiyonel; null = ERP StockQuantity devralınır).</summary>
    public int? OverrideStock { get; set; }

    /// <summary>Varyant-başı marj (markup yüzdesi; null = marj yok). Türetilmiş fiyat = NetCost × (1 + Margin/100).</summary>
    public decimal? Margin { get; set; }

    /// <summary>Sigortalı gönderim (Loomis deseni) bu varyantta açık mı — kanal gider ayarı tanımlı olsa bile
    /// VARSAYILAN kapalı; açılınca composer InsuredShipping reçete satırı üretir (yeni klon/yeniden-uygula'da).</summary>
    public bool InsuredShippingEnabled { get; set; }

    /// <summary>Kanal-özel reçete satırları (ERP reçetesinden klonlanır, sonra bağımsız) — Product reçetesiyle AYNI
    /// DTO tipi (ProductRecipePanel bunu tüketir). Id + IsDeleted diff; save'de kanal reçete tablosuna yazılır.</summary>
    public List<ProductRecipeLineGraphDto> RecipeLines { get; set; } = new();

    /// <summary>Reçetenin CANLI net maliyeti — ülke birimine rebase (SALT-OKUNUR; GetAsync hesaplar, save yoksayar).</summary>
    public decimal? NetCost { get; set; }

    /// <summary>Net maliyet para birimi kodu (ülke birimi; SALT-OKUNUR).</summary>
    public string NetCostCurrency { get; set; } = string.Empty;

    /// <summary>Net maliyet satırlarından biri kur/birim-eksik mi (SALT-OKUNUR UI uyarısı).</summary>
    public bool NetCostMissingRate { get; set; }

    /// <summary>Türetilmiş fiyat = NetCost × (1 + (Margin ?? 0)/100) [MARKUP] (SALT-OKUNUR; NetCost null ise null).</summary>
    public decimal? DerivedPrice { get; set; }
}

/// <summary>Trendyol kanal-özel varyant ÖZELLİĞİ (ör. "Renk", "Beden") — ERP <c>ProductAttributeGraphDto</c> deseninin
/// Trendyol-scope klonu (klon-sonra-ayrış; N11 paritesi). Id boş = yeni özellik; <see cref="ClientKey"/> in-memory graf
/// diff kimliği. <see cref="IsDeleted"/> = save'de silinecek.</summary>
public class SalesChannelTrTrendyolProductAttributeDto
{
    /// <summary>İstemci-taraflı graf kimliği (yeni özellikte Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
    public List<SalesChannelTrTrendyolProductAttributeValueDto> Values { get; set; } = new();
}

/// <summary>Trendyol kanal-özel varyant özellik DEĞERİ (ör. "Kırmızı") — ERP <c>ProductAttributeValueGraphDto</c>
/// deseninin Trendyol-scope klonu.</summary>
public class SalesChannelTrTrendyolProductAttributeValueDto
{
    /// <summary>İstemci-taraflı graf kimliği (yeni değerde Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Trendyol push ÖNİZLEMESİ (read-only, T6) — bu listelemede Trendyol'a GİDECEK ürün-seviyesi özet +
/// barcode başına kalemler + fail-fast/eksik-zorunlu-alan UYARILARI (lokalize). GERÇEK PUSH YOK: <c>BuildProductData</c>
/// read-only çalıştırılır, Trendyol'a submit EDİLMEZ. N11PushPreviewDto ile simetrik (Trendyol id-bazlı alanlar).</summary>
public class TrendyolPushPreviewDto
{
    /// <summary>Ürün-seviyesi gönderilecek özet (tüm kalemler için ortak alanlar).</summary>
    public TrendyolPreviewProductDto Product { get; set; } = new();

    /// <summary>Barcode başına gidecek kalemler (varyant = SKU satırı).</summary>
    public List<TrendyolPreviewItemDto> Items { get; set; } = new();

    /// <summary>Fail-fast / eksik zorunlu alan uyarıları (LOKALİZE) — push'u engelleyebilecek durumlar (exception değil).</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Önizleme ürün-seviyesi özeti — Trendyol'a gidecek ortak alanlar (tek kayıt, tüm kalemler için).</summary>
public class TrendyolPreviewProductDto
{
    public string ProductMainId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>Kategori OPSİYONEL (2026-07-11 gevşek kategori) — boşsa önizleme uyarısı zaten üretilir.</summary>
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public int? VatRate { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }

    /// <summary>Açıklama gönderilecek mi (kanal ya da ürün açıklaması dolu mu) — metin değil, var/yok.</summary>
    public bool HasDescription { get; set; }

    /// <summary>Gönderilecek görsel adedi (URL + geçici-linke çevrilmiş blob).</summary>
    public int ImageCount { get; set; }

    /// <summary>Ürün-seviyesi kategori attribute özeti ("Renk: Gri; Materyal: Pamuk"; ad çözülemezse "#id: değer").</summary>
    public string Attributes { get; set; } = string.Empty;
}

/// <summary>Önizleme kalem satırı — Trendyol'a bir barcode (varyant SKU) olarak gidecek (kod/barkod/stok/fiyat + eksen özeti).</summary>
public class TrendyolPreviewItemDto
{
    public string Barcode { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ListPrice { get; set; }
    public decimal SalePrice { get; set; }

    /// <summary>Varyant eksen özeti ("Renk: Kırmızı; Beden: M") — ERP varyant nitelikleri.</summary>
    public string Options { get; set; } = string.Empty;
}

/// <summary>Trendyol ürün listelemesi — tam okuma modeli (edit + durum görüntüsü). Ürün grafının parçası olarak da
/// kullanılır (ürün 'Kaydet'inde birlikte kaydedilir): <see cref="ClientKey"/> in-memory kimlik, <see cref="IsDeleted"/>
/// soft-delete işareti (graf diff). Kaydedilmiş kayıtta <see cref="Id"/> dolu; yeni satırda boş (N11 DTO paritesi).</summary>
public class SalesChannelTrTrendyolProductDto
{
    public Guid Id { get; set; }

    /// <summary>İstemci-taraflı graf kimliği (yeni satırda Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    /// <summary>Graf soft-delete işareti — ürün save'inde silinecek satır.</summary>
    public bool IsDeleted { get; set; }

    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }

    /// <summary>Varyant grup anahtarı (productMainId — "{ÜrünKodu}-{SequenceNo}", read-only/frozen).</summary>
    public string ProductMainId { get; set; } = string.Empty;

    /// <summary>Kayıt sırası (read-only; soft-delete dahil max+1).</summary>
    public int SequenceNo { get; set; }

    /// <summary>Trendyol kategori id'si — OPSİYONEL (2026-07-11 gevşek kategori kararı; push'ta fail-fast aranır).</summary>
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }

    /// <summary>Seçilen markanın Trendyol "luxury" bayrağı — YALNIZ K3 write-through cache beslemesi için taşınır
    /// (kanal-ürün entity'sinde SAKLANMAZ); null = bilinmiyor (picker'a dokunulmadı) → cache'te luxury EZİLMEZ.</summary>
    public bool? BrandIsLuxury { get; set; }
    public int? VatRate { get; set; }
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public string? Description { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }
    public List<SalesChannelTrTrendyolProductCategoryAttributeDto> Attributes { get; set; } = new();

    /// <summary>Varyant SKU kimlik/durum satırları (read-only; push + reconcile sonrası dolar).</summary>
    public List<SalesChannelTrTrendyolProductSkuDto> Skus { get; set; } = new();

    /// <summary>Kanal-özel varyant override'ları (fiyat/stok/marj + reçete graf düğümleri) — ERP varyant seti ⋈
    /// kaydedilmiş override (LEFT JOIN) ya da özellik-kaynaklı kartezyen kombinasyonlar. Ürün 'Kaydet'inde birlikte
    /// kaydedilir. NetCost/DerivedPrice SALT-OKUNUR.</summary>
    public List<SalesChannelTrTrendyolProductStockItemGraphDto> StockItems { get; set; } = new();

    /// <summary>Trendyol'un kendi varyant özellikleri (ör. "Renk"/"Beden") — İLK açılışta ERP nitelik/değerlerinden bir
    /// kez KLONLANIR, sonrasında ERP'den bağımsız yaşar. <see cref="StockItems"/> bu özelliklerin kartezyen
    /// kombinasyonundan üretilir (kaydet'te sunucu reconcile eder).</summary>
    public List<SalesChannelTrTrendyolProductAttributeDto> ProductAttributes { get; set; } = new();

    // Trendyol senkron durumu (read-only; submit/refresh sonrası dolar).
    public string? BatchRequestId { get; set; }
    public string? LastBatchRequestType { get; set; }
    public string? Status { get; set; }
    public int? FailedItemCount { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Create/Update ortak düzenlenebilir alanları. Kategori OPSİYONEL (2026-07-11).</summary>
public interface ISalesChannelTrTrendyolProductInput
{
    string? CategoryId { get; }
    string? CategoryName { get; }
    string BrandId { get; }
    string? BrandName { get; }

    /// <summary>Seçilen markanın "luxury" bayrağı — K3 cache besleme hint'i (entity'ye yazılmaz; null = bilinmiyor).</summary>
    bool? BrandIsLuxury { get; }
    int? VatRate { get; }
    int? CargoCompanyId { get; }
    decimal? DimensionalWeight { get; }
    string? Description { get; }
    int? DeliveryDuration { get; }
    TrendyolFastDeliveryType? FastDeliveryType { get; }
    bool IsActive { get; }
    List<SalesChannelTrTrendyolProductCategoryAttributeDto> Attributes { get; }

    /// <summary>Kanal-özel varyant override grafı (fiyat/stok/marj + reçete) — kanal-ürünle birlikte kaydedilir.</summary>
    List<SalesChannelTrTrendyolProductStockItemGraphDto> StockItems { get; }

    /// <summary>Trendyol'un kendi varyant özellikleri — kanal-ürünle birlikte kaydedilir (kartezyen reconcile tetikler).</summary>
    List<SalesChannelTrTrendyolProductAttributeDto> ProductAttributes { get; }
}

/// <summary>Listeleme oluşturma — ürün + kanal (create-only; şirket sunucuda zorlanır).</summary>
public class SalesChannelTrTrendyolProductCreateDto : ISalesChannelTrTrendyolProductInput
{
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public bool? BrandIsLuxury { get; set; }
    public int? VatRate { get; set; }
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public string? Description { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SalesChannelTrTrendyolProductCategoryAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrTrendyolProductStockItemGraphDto> StockItems { get; set; } = new();
    public List<SalesChannelTrTrendyolProductAttributeDto> ProductAttributes { get; set; } = new();
}

/// <summary>Listeleme güncelleme — ürün/kanal set-once (route'taki id kimliktir).</summary>
public class SalesChannelTrTrendyolProductUpdateDto : ISalesChannelTrTrendyolProductInput
{
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public bool? BrandIsLuxury { get; set; }
    public int? VatRate { get; set; }
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public string? Description { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SalesChannelTrTrendyolProductCategoryAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrTrendyolProductStockItemGraphDto> StockItems { get; set; } = new();
    public List<SalesChannelTrTrendyolProductAttributeDto> ProductAttributes { get; set; } = new();
}

/// <summary>Pazaryerinden içe aktarma SONUÇ RAPORU — sessiz geçilmez: toplam çekilen / üretilen şablon / kanal
/// kaydı sayıları + atlanan satırlar (nedenli) + eşleşmeyen kategoriler ekranda gösterilir (N11 komisyon import
/// raporu deseni).</summary>
public class TrendyolImportResultDto
{
    /// <summary>Trendyol'dan çekilen toplam KALEM (barcode) sayısı.</summary>
    public int TotalFetchedItems { get; set; }

    /// <summary>productMainId gruplaması + stockCode birleştirmesi (kardeş varyant kuruluşu) sonrası uzak ÜRÜN sayısı.</summary>
    public int TotalRemoteProducts { get; set; }

    /// <summary>Bu import'ta üretilen YENİ şablon Product sayısı.</summary>
    public int CreatedProducts { get; set; }

    /// <summary>Bu import'ta üretilen YENİ kanal ürünü (SalesChannelTrTrendyolProduct) sayısı.</summary>
    public int CreatedChannelProducts { get; set; }

    /// <summary>Mevcut olup GÜNCELLENEN kanal ürünü sayısı (idempotent ikinci geçiş).</summary>
    public int UpdatedChannelProducts { get; set; }

    /// <summary>Mevcut şablonlara bu import'ta EKLENEN eksik varyant sayısı (2026-07-11: eski "Eksik Varyantları
    /// Tamamla" akışı import'a gömüldü — remote'ta olup yerelde olmayan barkodlu kalem otomatik varyant olur).</summary>
    public int AddedVariants { get; set; }

    /// <summary>Eklenen varyantların barkodları (kullanıcı doğrulaması için; sessiz geçilmez).</summary>
    public List<string> AddedBarcodes { get; set; } = new();

    /// <summary>Uzak stoğu çekirdek (ERP) stoktan FARKLI olan kalem sayısı (K12 stok politikası, 2026-07-23):
    /// sonraki importlar çekirdek StockQuantity'yi EZMEZ — remote değer kanal OverrideStock'una yazılır (kanal
    /// gerçeği) + satır-bazında LogWarning. 0 = tüm kalemler çekirdekle uyumlu (override gürültüsü üretilmedi).</summary>
    public int StockDifferenceCount { get; set; }

    /// <summary>Atlanan satırlar + nedenleri (LOKALİZE) — barcode'suz/duplike/geçersiz kalemler.</summary>
    public List<TrendyolImportIssueDto> SkippedRows { get; set; } = new();

    /// <summary>Yerel kategori ağacında karşılığı OLMAYAN uzak kategoriler ("id — ad") — ürün ATLANMAZ; geçerli
    /// uzak id ham yazılır (kullanıcı sonradan eşler), eksik/taşan kategori NULL kalır (gevşek kategori).</summary>
    public List<string> UnmatchedCategories { get; set; } = new();

    /// <summary>Import-geneli uyarılar (LOKALİZE) — kalem-bazlı olmayan riskli fallback'ler (ör. TRY para birimi
    /// çözülemedi → fiyatlar para-birimsiz yazıldı). Sessiz geçilmez.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Import'ta atlanan tek satır — kimlik ipuçları + lokalize neden.</summary>
public class TrendyolImportIssueDto
{
    public string? Barcode { get; set; }
    public string? StockCode { get; set; }
    public string Reason { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Barcode ?? StockCode}: {Reason}";
    }
}

/// <summary>
/// Trendyol ürün listeleme — bir ERP ürününü bir Trendyol kanalında listeler + Trendyol'a ASENKRON push eder.
/// Yapılandırma (kategori/marka/KDV/kargo/attribute) bizde tutulur; <see cref="PushToTrendyolAsync"/> ürünü +
/// varyantlarını gönderir (batch id döner), <see cref="RefreshStatusAsync"/> batch durumunu çeker. Company-owned.
/// </summary>
public interface ISalesChannelTrTrendyolProductAppService : IApplicationService
{
    /// <summary>Bir ÜRÜNE ait tüm Trendyol kanal ürünleri (ürün-merkezli drill). Aynı kanalda birden fazla kayıt
    /// OLABİLİR (N11 ile aynı 2026-07-07 kararı); kanal set-once (değiştirilemez).</summary>
    Task<List<SalesChannelTrTrendyolProductDto>> GetListForProductAsync(Guid productId);

    /// <summary>Bir KANALA ait tüm ürün listelemeleri (kanal-merkezli yönetim görünümü — N11 paritesi).</summary>
    Task<List<SalesChannelTrTrendyolProductDto>> GetListForChannelAsync(Guid salesChannelId);

    Task<SalesChannelTrTrendyolProductDto> GetAsync(Guid id);

    Task<SalesChannelTrTrendyolProductDto> CreateAsync(SalesChannelTrTrendyolProductCreateDto input);

    Task<SalesChannelTrTrendyolProductDto> UpdateAsync(Guid id, SalesChannelTrTrendyolProductUpdateDto input);

    /// <summary>Yalnız yerel siler (Trendyol'da pasifleştirme ayrı; ürün Trendyol'da kalır).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Listelemeyi Trendyol'a gönderir (async create). Batch id kaydedilir; durum PROCESSING olur.</summary>
    Task<SalesChannelTrTrendyolProductDto> PushToTrendyolAsync(Guid id);

    /// <summary>Kaydedilmiş batch id ile Trendyol'dan işlem durumunu çeker + günceller (COMPLETED/FAILED).</summary>
    Task<SalesChannelTrTrendyolProductDto> RefreshStatusAsync(Guid id);

    /// <summary>Trendyol'a NE gideceğinin READ-ONLY önizlemesi (T6): <c>BuildProductData</c> read-only çalıştırılır,
    /// Trendyol'a SUBMIT EDİLMEZ. Fail-fast/eksik zorunlu alanlar exception yerine
    /// <see cref="TrendyolPushPreviewDto.Warnings"/>'e (lokalize) yazılır — önizleme yine döner.</summary>
    Task<TrendyolPushPreviewDto> GetPushPreviewAsync(Guid id);

    /// <summary>Özellik/değer grafını PERSIST EDER + kartezyen reconcile'ı hemen tetikler — TÜM ürünü kaydetmeden
    /// yalnız bu Trendyol kaydının kombinasyon setini yeniler. Full Update ile aynı reconcile mekanizmasını kullanır
    /// (SaveAttributesAndReconcileAsync).</summary>
    Task<SalesChannelTrTrendyolProductDto> RegenerateStockItemsAsync(Guid id, List<SalesChannelTrTrendyolProductAttributeDto> productAttributes);

    /// <summary>Yan-maliyet satırlarını KAYDEDİLMİŞ varyant reçetelerinde kanal gider ayarlarından TAZELER
    /// ("yeniden uygula"): otomatik (SideCostKind işaretli) satırlar düşürülüp yeniden üretilir; kullanıcı
    /// satırlarına dokunulmaz. Kanal ayarı değişince / silinen otomatik satırı geri getirmek için. İdempotent.</summary>
    Task<SalesChannelTrTrendyolProductDto> ReapplySideCostsAsync(Guid id);

    /// <summary>Muadil M4 köprüsü: Top-N başarılı kombinasyonu bu ürünün "Kombinasyon" özelliği + StockItem'ları
    /// (reçete + paket stoğu) olarak uygular — tek motor zinciri; yeniden uygulama imza-bazlı reconcile'dır.
    /// N11 adaptörüyle AYNI nötr planı (SubstitutionStockItemPlanner) tüketir.</summary>
    Task<SubstitutionApplyResultDto> ApplySubstitutionAsync(Guid id, SubstitutionApplyInput input);

    /// <summary>Pazaryerindeki MEVCUT satıcı ürünlerini içeri alır (salt GET — Trendyol'a SIFIR yazma): her uzak
    /// ürün için TAM ZİNCİR yazılır — şablon <c>Product</c> + varyant(lar) (yoksa otomatik üretilir) + bağlı kanal
    /// ürünü grafı (kategori/marka/attribute + Sku + StockItem fiyat/stok override). Remote'ta olup MEVCUT şablonda
    /// karşılığı OLMAYAN barkodlu kalemler şablona OTOMATİK varyant olarak eklenir (2026-07-11: eski
    /// "Eksik Varyantları Tamamla" ucu import'a gömüldü) — mevcut varyant/şablon ALANLARI GÜNCELLENMEZ, ANA VARYANT
    /// DEĞİŞMEZ (yeni eklenen main olmaz), kod çakışması son-ekle ("-2", "-3"...) çözülür. İDEMPOTENT: barcode
    /// (varyant) ve RemoteProductMainId ?? stockCode (kanal kaydı) anahtarlarıyla ikinci çağrı dublike/ekleme
    /// üretmez, yalnız kanal grafını günceller. Sonuç raporu sessiz geçilmez.</summary>
    Task<TrendyolImportResultDto> ImportFromMarketplaceAsync(Guid salesChannelId);
}
