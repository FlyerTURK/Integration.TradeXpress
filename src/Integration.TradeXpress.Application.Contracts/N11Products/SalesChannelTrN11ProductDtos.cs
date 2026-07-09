using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Products;

/// <summary>N11 kategori attribute değeri (name/value).</summary>
public class SalesChannelTrN11ProductAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>N11 ürün özel bilgi (serbest key/value; her kategoride kullanılabilir). <see cref="ClientKey"/> yalnız
/// in-memory DrillList satır kimliği (persist edilmez; entity Key/Value tutar).</summary>
public class SalesChannelTrN11ProductSpecialInfoDto
{
    /// <summary>İstemci-taraflı satır kimliği (DrillList grid identity) — persist edilmez.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Varyant SKU kimlik/durum satırı (read-only; push + stok/fiyat senkronunda dolar). UI görünürlük +
/// senkron durumu; AttributeSnapshot UI'a taşınmaz (sipariş eşleme sunucu-içi kalır).</summary>
public class SalesChannelTrN11ProductSkuDto
{
    public Guid ProductVariantId { get; set; }
    public string SellerStockCode { get; set; } = string.Empty;
    public long? N11SkuId { get; set; }
    public long? N11Version { get; set; }
    public int? LastSentQuantity { get; set; }
    public decimal? LastSentOptionPrice { get; set; }
}

/// <summary>N11 kanal-özel varyant override graf düğümü — ERP varyantının (SSOT: kod/ad/ERP fiyat/stok) N11-scope
/// özelleştirmesi. LEFT JOIN: ERP varyant seti ⋈ kaydedilmiş kanal override. null override alanı = ERP'den devralınır.
/// Reçete (<see cref="RecipeLines"/>) kaydedilmişse ondan, yoksa ERP reçetesinden KLONLANIR (Id boş = henüz persist yok).
/// <see cref="NetCost"/>/<see cref="DerivedPrice"/> SALT-OKUNUR (GetAsync canlı hesaplar; save yoksayar).</summary>
public class SalesChannelTrN11ProductVariantGraphDto
{
    /// <summary>Override BAŞLIĞININ kendi id'si (2026-07-09 kararı: anchor budur) — SALT-OKUNUR kimlik, round-trip
    /// bununla yapılır. Axis-kaynaklı (kartezyen) satırlarda ZORUNLU dolu (reconcile server-side üretir, client
    /// yeni satır açamaz); henüz persist edilmemiş/legacy düğümde <c>Guid.Empty</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>ERP varyantı — id-only, OPSİYONEL. Axis-kaynaklı satırlarda yalnız fiyat/stok FALLBACK kaynağı
    /// (reconcile anahtarı DEĞİL — bkz. <see cref="SalesChannelTrN11ProductAttributeAxisDto"/>); null = N11-only
    /// kombinasyon (ERP'de karşılığı yok — N11 kendi ekseninde sonradan eklenen bir değerden doğdu).</summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Kombinasyonu oluşturan eksen değerlerinin SALT-OKUNUR görüntüsü (ör. "Renk: Kırmızı; Beden: M") —
    /// yalnız axis-kaynaklı (kartezyen) satırlarda dolu; legacy ERP-doğrudan satırda boş (VariantCode/Name kullanılır).</summary>
    public string CombinationLabel { get; set; } = string.Empty;

    /// <summary>SALT-OKUNUR türetilmiş bayrak: <c>true</c> = ERP varyantından izleniyor, <c>false</c> = N11-only
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

/// <summary>N11 kanal-özel varyant EKSENİ (ör. "Renk", "Beden") — ERP <c>ProductAttributeGraphDto</c> deseninin
/// N11-scope klonu (klon-sonra-ayrış). Id boş = yeni eksen; <see cref="ClientKey"/> in-memory graf diff kimliği
/// (Product ProductAttributeGraphDto ile aynı desen). <see cref="IsDeleted"/> = save'de silinecek.</summary>
public class SalesChannelTrN11ProductAttributeAxisDto
{
    /// <summary>İstemci-taraflı graf kimliği (yeni eksende Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
    public List<SalesChannelTrN11ProductAttributeAxisValueDto> Values { get; set; } = new();
}

/// <summary>N11 kanal-özel varyant eksen DEĞERİ (ör. "Kırmızı") — ERP <c>ProductAttributeValueGraphDto</c>
/// deseninin N11-scope klonu.</summary>
public class SalesChannelTrN11ProductAttributeAxisValueDto
{
    /// <summary>İstemci-taraflı graf kimliği (yeni değerde Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>N11 push ÖNİZLEMESİ (read-only) — bu listelemede N11'e GİDECEK varyantlar + görseller. Kaynak ERP
/// ürünü (SSOT); N11 formunda yalnız görünürlük (tanım ERP ürün formunda yapılır). Push anındaki fiili veri.</summary>
public class N11PushPreviewDto
{
    public List<N11PreviewVariantDto> Variants { get; set; } = new();
    public List<N11PreviewImageDto> Images { get; set; } = new();
}

/// <summary>Önizleme varyant satırı — N11'e stockItem olarak gidecek (kod/ad/stok/fiyat + seçenek özeti).</summary>
public class N11PreviewVariantDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal? SalePrice { get; set; }

    /// <summary>Seçenek özeti ("Renk: Kırmızı; Beden: M") — varyant eksenleri (name/value).</summary>
    public string Options { get; set; } = string.Empty;
}

/// <summary>Önizleme görsel satırı — N11'e gidecek görsel (kaynak metni + ana bayrağı + varsa küçük önizleme).</summary>
public class N11PreviewImageDto
{
    public string Source { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>Yüklenmiş (blob) görsel için thumbnail data-URL'i; URL kaynaklı görselde boş (dış link).</summary>
    public string? PreviewDataUrl { get; set; }
}

/// <summary>N11 ürün listelemesi — tam okuma modeli (edit + durum görüntüsü). Ürün grafının parçası olarak da
/// kullanılır (ürün 'Kaydet'inde birlikte kaydedilir): <see cref="ClientKey"/> in-memory kimlik, <see cref="IsDeleted"/>
/// soft-delete işareti (graf diff). Kaydedilmiş kayıtta <see cref="Id"/> dolu; yeni satırda boş.</summary>
public class SalesChannelTrN11ProductDto
{
    public Guid Id { get; set; }

    /// <summary>İstemci-taraflı graf kimliği (yeni satırda Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    /// <summary>Graf soft-delete işareti — ürün save'inde silinecek satır.</summary>
    public bool IsDeleted { get; set; }

    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }

    /// <summary>N11 upsert kimliği ("{ÜrünKodu}-{Sıra}") — sunucu üretir (read-only; create/update input'unda YOK).</summary>
    public string SellerCode { get; set; } = string.Empty;

    /// <summary>Kayıt sırası (read-only) — varyant stok kodu eklerinde kullanılır.</summary>
    public int SequenceNo { get; set; }
    public string CategoryExternalId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public N11ProductCondition Condition { get; set; }
    public string ShipmentTemplateName { get; set; } = string.Empty;
    public bool Domestic { get; set; }
    public int PreparingDay { get; set; }
    public int? MaxPurchaseQuantity { get; set; }

    /// <summary>N11 para birimi (opsiyonel; push'ta currencyType bundan çözülür).</summary>
    public Guid? CurrencyUnitId { get; set; }

    /// <summary>Kanal-özel üretim tarihi (opsiyonel).</summary>
    public DateTime? ProductionDate { get; set; }

    /// <summary>Kanal-özel son kullanma tarihi (opsiyonel).</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>N11 unitInfo/unitType (opsiyonel).</summary>
    public int? UnitType { get; set; }

    /// <summary>N11 unitInfo/unitWeight (opsiyonel).</summary>
    public int? UnitWeight { get; set; }

    /// <summary>N11 satıcı notu (kanal-özel kısa düz not; opsiyonel).</summary>
    public string? SellerNote { get; set; }

    /// <summary>N11 kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public string? Description { get; set; }

    /// <summary>N11 grup ürün kodu (opsiyonel; aynı grup üyeleri eşleşir).</summary>
    public string? GroupItemCode { get; set; }

    /// <summary>N11 grubu ayıran özellik adı (opsiyonel, ör. "Renk").</summary>
    public string? GroupAttribute { get; set; }

    /// <summary>N11 grup içindeki öğe adı (opsiyonel).</summary>
    public string? ItemName { get; set; }
    public List<SalesChannelTrN11ProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; set; } = new();

    /// <summary>Kanal-özel varyant override'ları (fiyat/stok/marj + reçete graf düğümleri) — ERP varyant seti ⋈
    /// kaydedilmiş override (LEFT JOIN). Ürün 'Kaydet'inde birlikte kaydedilir. NetCost/DerivedPrice SALT-OKUNUR.</summary>
    public List<SalesChannelTrN11ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>N11 kendi varyant eksenleri (ör. "Renk"/"Beden") — İLK açılışta ERP nitelik/değerlerinden bir kez
    /// KLONLANIR, sonrasında ERP'den bağımsız yaşar. <see cref="Variants"/> bu eksenlerin kartezyen kombinasyonundan
    /// üretilir (kaydet'te sunucu reconcile eder).</summary>
    public List<SalesChannelTrN11ProductAttributeAxisDto> AttributeAxes { get; set; } = new();

    /// <summary>Varyant SKU kimlik/durum satırları (read-only; push + stok/fiyat senkronunda dolar).</summary>
    public List<SalesChannelTrN11ProductSkuDto> Skus { get; set; } = new();

    // N11 senkron durumu (read-only; push sonrası dolar).
    public long? N11ProductId { get; set; }
    public string? SaleStatus { get; set; }
    public string? ApprovalStatus { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Push sonrası eşitleme uyarıları (LOKALİZE; ör. N11 kategoriyi değiştirdi) — SALT anlık görüntü,
    /// persist edilmez; yalnız PushToN11Async dönüşünde dolar (UI uyarı toast'ları gösterir).</summary>
    public List<string> SyncWarnings { get; set; } = new();
}

/// <summary>Create/Update ortak düzenlenebilir alanları.</summary>
public interface ISalesChannelTrN11ProductInput
{
    string CategoryExternalId { get; }
    string? CategoryName { get; }
    N11ProductCondition Condition { get; }
    string ShipmentTemplateName { get; }
    bool Domestic { get; }
    int PreparingDay { get; }
    int? MaxPurchaseQuantity { get; }
    Guid? CurrencyUnitId { get; }
    DateTime? ProductionDate { get; }
    DateTime? ExpirationDate { get; }
    int? UnitType { get; }
    int? UnitWeight { get; }
    bool IsActive { get; }
    string? SellerNote { get; }
    string? Description { get; }
    string? GroupItemCode { get; }
    string? GroupAttribute { get; }
    string? ItemName { get; }
    List<SalesChannelTrN11ProductAttributeDto> Attributes { get; }
    List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; }

    /// <summary>Kanal-özel varyant override grafı (fiyat/stok/marj + reçete) — kanal-ürünle birlikte kaydedilir.</summary>
    List<SalesChannelTrN11ProductVariantGraphDto> Variants { get; }

    /// <summary>N11 kendi varyant eksenleri — kanal-ürünle birlikte kaydedilir (kartezyen reconcile tetikler).</summary>
    List<SalesChannelTrN11ProductAttributeAxisDto> AttributeAxes { get; }
}

/// <summary>Listeleme oluşturma — ürün + kanal (create-only; şirket sunucuda zorlanır).</summary>
public class SalesChannelTrN11ProductCreateDto : ISalesChannelTrN11ProductInput
{
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public string CategoryExternalId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public N11ProductCondition Condition { get; set; } = N11ProductCondition.New;
    public string ShipmentTemplateName { get; set; } = string.Empty;
    public bool Domestic { get; set; } = true;
    public int PreparingDay { get; set; } = 1;
    public int? MaxPurchaseQuantity { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public int? UnitType { get; set; }
    public int? UnitWeight { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SellerNote { get; set; }
    public string? Description { get; set; }
    public string? GroupItemCode { get; set; }
    public string? GroupAttribute { get; set; }
    public string? ItemName { get; set; }
    public List<SalesChannelTrN11ProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; set; } = new();
    public List<SalesChannelTrN11ProductVariantGraphDto> Variants { get; set; } = new();
    public List<SalesChannelTrN11ProductAttributeAxisDto> AttributeAxes { get; set; } = new();
}

/// <summary>Listeleme güncelleme — ürün/kanal set-once (route'taki id kimliktir).</summary>
public class SalesChannelTrN11ProductUpdateDto : ISalesChannelTrN11ProductInput
{
    public string CategoryExternalId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public N11ProductCondition Condition { get; set; } = N11ProductCondition.New;
    public string ShipmentTemplateName { get; set; } = string.Empty;
    public bool Domestic { get; set; } = true;
    public int PreparingDay { get; set; } = 1;
    public int? MaxPurchaseQuantity { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public int? UnitType { get; set; }
    public int? UnitWeight { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SellerNote { get; set; }
    public string? Description { get; set; }
    public string? GroupItemCode { get; set; }
    public string? GroupAttribute { get; set; }
    public string? ItemName { get; set; }
    public List<SalesChannelTrN11ProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; set; } = new();
    public List<SalesChannelTrN11ProductVariantGraphDto> Variants { get; set; } = new();
    public List<SalesChannelTrN11ProductAttributeAxisDto> AttributeAxes { get; set; } = new();
}

/// <summary>
/// N11 ürün listeleme — bir ERP ürününü bir N11 kanalında listeler + N11'e push eder (SaveProduct). Company-owned.
/// Listeleme yapılandırması (kategori/attribute/kargo şablonu/condition/özel bilgi) bizde tutulur; push ürünün
/// varyantları (stockItems) + fiyat/stok/görseliyle birlikte N11'e gider.
/// </summary>
public interface ISalesChannelTrN11ProductAppService : IApplicationService
{
    /// <summary>Bir ÜRÜNE ait tüm N11 kanal ürünleri (ürün-merkezli drill). Aynı kanalda birden fazla kayıt
    /// OLABİLİR (2026-07-07 kullanıcı kararı); kanal set-once (değiştirilemez).</summary>
    Task<List<SalesChannelTrN11ProductDto>> GetListForProductAsync(Guid productId);

    /// <summary>Bir KANALA ait tüm ürün listelemeleri (kanal-merkezli yönetim görünümü).</summary>
    Task<List<SalesChannelTrN11ProductDto>> GetListForChannelAsync(Guid salesChannelId);

    Task<SalesChannelTrN11ProductDto> GetAsync(Guid id);

    Task<SalesChannelTrN11ProductDto> CreateAsync(SalesChannelTrN11ProductCreateDto input);

    Task<SalesChannelTrN11ProductDto> UpdateAsync(Guid id, SalesChannelTrN11ProductUpdateDto input);

    /// <summary>Yalnız yerel siler (N11'de pasifleştirme ayrı; ürün N11'de kalır).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Listelemeyi N11'e gönderir (SaveProduct): ürün + varyant + fiyat/stok/görsel. Durumu günceller + döner.</summary>
    Task<SalesChannelTrN11ProductDto> PushToN11Async(Guid id);

    /// <summary>Yalnız stok+fiyatı N11'e gönderir (UpdateProductBasic — Faz 2, hafif): tam SaveProduct'a gerek
    /// olmadan değişen varyantların adet/fiyatını günceller. Önce N11'den okur (eksik SKU id doldurma + version
    /// drift uyarısı), yalnız DEĞİŞEN varyantları gönderir; değişiklik yoksa no-op. Yapı/varyant-seti değiştiyse
    /// bu YETMEZ — tam <see cref="PushToN11Async"/> gerekir.</summary>
    Task<SalesChannelTrN11ProductDto> SyncStockAndPriceAsync(Guid id);

    /// <summary>Push önizlemesi (read-only): bu listelemede N11'e GİDECEK varyantlar + görseller (kaynak ERP ürünü).</summary>
    Task<N11PushPreviewDto> GetPushPreviewAsync(Guid id);

    /// <summary>Eksen/değer grafını PERSIST EDER + kartezyen reconcile'ı hemen tetikler — TÜM ürünü kaydetmeden
    /// yalnız bu N11 kaydının varyant setini yeniler. Full Update ile aynı reconcile mekanizmasını kullanır
    /// (SaveAxesAndReconcileAsync).</summary>
    Task<SalesChannelTrN11ProductDto> RegenerateVariantsAsync(Guid id, List<SalesChannelTrN11ProductAttributeAxisDto> attributeAxes);
}
