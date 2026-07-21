using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>Etsy taksonomi varyasyon-DIŞI listeleme attribute değeri (name/value). N11
/// <c>SalesChannelTrN11ProductCategoryAttributeDto</c> ikizi (owned → JSON'a serialize edilir).</summary>
public class SalesChannelEtsyProductListingAttributeDto
{
    [StringLength(SalesChannelEtsyProductConsts.ListingAttributeNameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelEtsyProductConsts.ListingAttributeValueMaxLength)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>Etsy kişiselleştirme özel bilgi (serbest key/value). <see cref="ClientKey"/> yalnız in-memory DrillList
/// satır kimliği (persist edilmez; entity Key/Value tutar). Key zorunlu, Value opsiyonel. N11
/// <c>SalesChannelTrN11ProductSpecialInfoDto</c> ikizi.</summary>
public class SalesChannelEtsyProductSpecialInfoDto
{
    /// <summary>İstemci-taraflı satır kimliği (DrillList grid identity) — persist edilmez.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [StringLength(SalesChannelEtsyProductConsts.SpecialInfoKeyMaxLength)]
    public string Key { get; set; } = string.Empty;

    [StringLength(SalesChannelEtsyProductConsts.SpecialInfoValueMaxLength)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>Varyant SKU kimlik/durum satırı (read-only; push + stok/fiyat senkronunda dolar). UI görünürlük +
/// senkron durumu; PropertySnapshot UI'a taşınmaz (sipariş eşleme sunucu-içi kalır). N11
/// <c>SalesChannelTrN11ProductSkuDto</c> ikizi (SellerStockCode→FrozenSku, N11SkuId→EtsyProductId,
/// N11Version→EtsyOfferingVersion).</summary>
public class SalesChannelEtsyProductSkuDto
{
    public Guid ProductVariantId { get; set; }
    public string FrozenSku { get; set; } = string.Empty;
    public long? EtsyProductId { get; set; }
    public long? EtsyOfferingVersion { get; set; }
    public int? LastSentQuantity { get; set; }
    public decimal? LastSentPrice { get; set; }
}

/// <summary>Etsy mağaza kargo profili (picker seçeneği) — <c>getShopShippingProfiles</c>'tan on-demand çekilir; KALICI
/// TABLO YOK (mağazada genelde 1-birkaç profil). Gevşek referans: yerelde yalnız <c>ShippingProfileId</c> saklanır,
/// profil Etsy'de tanımlıdır. Taksonomi picker beslemesi (<c>EtsyLeafCategoryDto</c>) deseninin ikizi.</summary>
public class EtsyShippingProfileDto
{
    /// <summary>Etsy <c>shipping_profile_id</c> — <c>SalesChannelEtsyProductDto.ShippingProfileId</c> ile eşleşen değer.</summary>
    public long Id { get; set; }

    /// <summary>Satıcının profile verdiği ad (ör. "Free Worldwide - PTT") — picker görüntü metni.</summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>Etsy mağaza iade politikası (picker seçeneği) — <c>getShopReturnPolicies</c>'tan on-demand çekilir; KALICI
/// TABLO YOK. Etsy iade politikasının BAŞLIĞI YOKTUR → <see cref="Label"/> iade/değişim + süre alanlarından AppService'te
/// (lokalize) türetilir. Gevşek referans: yerelde yalnız <c>ReturnPolicyId</c> saklanır. Kargo profili picker'ının
/// (<see cref="EtsyShippingProfileDto"/>) ikizi.</summary>
public class EtsyReturnPolicyDto
{
    /// <summary>Etsy <c>return_policy_id</c> — <c>SalesChannelEtsyProductDto.ReturnPolicyId</c> ile eşleşen değer.</summary>
    public long Id { get; set; }

    /// <summary>Türetilmiş görüntü etiketi (ör. "#123 · iade + değişim · 30 gün") — picker görüntü metni.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>İade kabul ediliyor mu — düzenle popup'ının ön-doldurması için ham alan.</summary>
    public bool AcceptsReturns { get; set; }

    /// <summary>Değişim kabul ediliyor mu — düzenle popup'ının ön-doldurması için ham alan.</summary>
    public bool AcceptsExchanges { get; set; }

    /// <summary>İade süresi (gün; kabul yoksa null) — düzenle popup'ının ön-doldurması için ham alan.</summary>
    public int? ReturnDeadlineDays { get; set; }
}

/// <summary>Dükkân bölümü OLUŞTUR/DÜZENLE girişi (Etsy'ye yazma) — yalnız başlık. Etsy'ye
/// <c>POST/PUT .../shops/{shopId}/sections</c> ile yansır.</summary>
public class EtsyShopSectionInputDto
{
    [Required]
    [StringLength(SalesChannelEtsyProductConsts.ShopSectionTitleMaxLength, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;
}

/// <summary>İade politikası OLUŞTUR/DÜZENLE girişi (Etsy'ye yazma) — iade/değişim kabulü + (kabul varsa) iade süresi.
/// Etsy'ye <c>POST/PUT .../shops/{shopId}/policies/return</c> ile yansır.</summary>
public class EtsyReturnPolicyInputDto
{
    public bool AcceptsReturns { get; set; }
    public bool AcceptsExchanges { get; set; }

    /// <summary>İade süresi (gün) — Etsy kabul (iade/değişim) varken zorunlu; kabul yoksa yoksayılır.</summary>
    [Range(1, SalesChannelEtsyProductConsts.ReturnDeadlineMaxDays)]
    public int? ReturnDeadlineDays { get; set; }
}

/// <summary>Etsy mağaza dükkân bölümü (picker seçeneği) — <c>getShopSections</c>'tan on-demand çekilir; KALICI TABLO YOK.
/// Gevşek referans: yerelde yalnız <c>ShopSectionId</c> saklanır. Kargo profili picker'ının
/// (<see cref="EtsyShippingProfileDto"/>) ikizi.</summary>
public class EtsyShopSectionDto
{
    /// <summary>Etsy <c>shop_section_id</c> — <c>SalesChannelEtsyProductDto.ShopSectionId</c> ile eşleşen değer.</summary>
    public long Id { get; set; }

    /// <summary>Bölümün adı (ör. "Necklaces") — picker görüntü metni.</summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>Etsy kanal-özel varyant override graf düğümü — ERP varyantının (SSOT: kod/ad/ERP fiyat/stok) Etsy-scope
/// özelleştirmesi. LEFT JOIN: ERP varyant seti ⋈ kaydedilmiş kanal override. null override alanı = ERP'den devralınır.
/// Reçete (<see cref="RecipeLines"/>) kaydedilmişse ondan, yoksa ERP reçetesinden KLONLANIR (Id boş = henüz persist yok).
/// <see cref="NetCost"/>/<see cref="DerivedPrice"/> SALT-OKUNUR (GetAsync canlı hesaplar; save yoksayar). N11
/// <c>SalesChannelTrN11ProductStockItemGraphDto</c> ikizi.</summary>
public class SalesChannelEtsyProductStockItemGraphDto
{
    /// <summary>Override BAŞLIĞININ kendi id'si (anchor budur) — SALT-OKUNUR kimlik, round-trip bununla yapılır.
    /// Özellik-kaynaklı (kartezyen) satırlarda ZORUNLU dolu (reconcile server-side üretir, client yeni satır açamaz);
    /// henüz persist edilmemiş/legacy düğümde <c>Guid.Empty</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>ERP varyantı — id-only, OPSİYONEL. Özellik-kaynaklı satırlarda yalnız fiyat/stok FALLBACK kaynağı
    /// (reconcile anahtarı DEĞİL — bkz. <see cref="SalesChannelEtsyProductAttributeDto"/>); null = Etsy-only
    /// kombinasyon (ERP'de karşılığı yok).</summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Kombinasyonu oluşturan özellik değerlerinin SALT-OKUNUR görüntüsü (ör. "Renk: Kırmızı; Beden: M") —
    /// yalnız özellik-kaynaklı satırlarda dolu; legacy ERP-doğrudan satırda boş (VariantCode/Name kullanılır).</summary>
    public string CombinationLabel { get; set; } = string.Empty;

    /// <summary>SALT-OKUNUR türetilmiş bayrak: <c>true</c> = ERP varyantından izleniyor, <c>false</c> = Etsy-only
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

    /// <summary>Sigortalı gönderim bu varyantta açık mı — kanal gider ayarı tanımlı olsa bile VARSAYILAN kapalı;
    /// açılınca composer InsuredShipping reçete satırı üretir (yeni klon/yeniden-uygula'da).</summary>
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

/// <summary>Etsy kanal-özel varyant ÖZELLİĞİ (ör. "Renk", "Beden") — ERP <c>ProductAttributeGraphDto</c> deseninin
/// Etsy-scope klonu (klon-sonra-ayrış). Id boş = yeni özellik; <see cref="ClientKey"/> in-memory graf diff kimliği.
/// <see cref="IsDeleted"/> = save'de silinecek. N11 <c>SalesChannelTrN11ProductAttributeDto</c> ikizi.</summary>
public class SalesChannelEtsyProductAttributeDto
{
    /// <summary>İstemci-taraflı graf kimliği (yeni özellikte Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.AttributeNameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
    public List<SalesChannelEtsyProductAttributeValueDto> Values { get; set; } = new();
}

/// <summary>Etsy kanal-özel varyant özellik DEĞERİ (ör. "Kırmızı") — ERP <c>ProductAttributeValueGraphDto</c>
/// deseninin Etsy-scope klonu. N11 <c>SalesChannelTrN11ProductAttributeValueDto</c> ikizi.</summary>
public class SalesChannelEtsyProductAttributeValueDto
{
    /// <summary>İstemci-taraflı graf kimliği (yeni değerde Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.AttributeValueMaxLength)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Etsy ürün listelemesi — tam okuma modeli (edit + durum görüntüsü). Ürün grafının parçası olarak da
/// kullanılır (ürün 'Kaydet'inde birlikte kaydedilir): <see cref="ClientKey"/> in-memory kimlik, <see cref="IsDeleted"/>
/// soft-delete işareti (graf diff). Kaydedilmiş kayıtta <see cref="Id"/> dolu; yeni satırda boş. N11
/// <c>SalesChannelTrN11ProductDto</c> ikizi (Etsy alan delta'sıyla).</summary>
public class SalesChannelEtsyProductDto
{
    public Guid Id { get; set; }

    /// <summary>İstemci-taraflı graf kimliği (yeni satırda Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    /// <summary>Graf soft-delete işareti — ürün save'inde silinecek satır.</summary>
    public bool IsDeleted { get; set; }

    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }

    /// <summary>Etsy satıcı SKU tabanı ("{ÜrünKodu}-{Sıra}") — sunucu üretir (read-only; create/update input'unda YOK).</summary>
    public string SellerSkuBase { get; set; } = string.Empty;

    /// <summary>Kayıt sırası (read-only) — varyant stok kodu eklerinde kullanılır.</summary>
    public int SequenceNo { get; set; }

    // ── Etsy listing config (düzenlenebilir) ──

    /// <summary>Etsy taksonomi yaprağı (opsiyonel; yayın için zorunlu, taslakta boş olabilir).</summary>
    public long? TaxonomyId { get; set; }

    /// <summary>Taksonominin CANLI çözülmüş tam yol adı ("Jewelry &gt; Necklaces &gt; Pendant Necklaces") — SALT-OKUNUR
    /// (GetAsync okuma anında synced taxonomy tablosundan çözer; save YOKSAYAR, entity'de saklanmaz). Kategori adı KALICI
    /// tutulmaz → snapshot bayatlamaz; <see cref="TaxonomyId"/> tabloda yoksa (reconcile sildi) null döner + <see cref="TaxonomyIsStale"/> true.</summary>
    public string? TaxonomyName { get; set; }

    /// <summary>SALT-OKUNUR bayrak: <see cref="TaxonomyId"/> DOLU ama synced taxonomy tablosunda karşılığı YOK (günlük
    /// reconcile sildi/değişti) → true = "bayat kategori, yeniden seç". Id null ise false. Save YOKSAYAR (canlı hesap).</summary>
    public bool TaxonomyIsStale { get; set; }

    /// <summary>Etsy listeleme türü (fiziksel/dijital). Varsayılan Physical.</summary>
    public EtsyListingType ListingType { get; set; } = EtsyListingType.Physical;

    /// <summary>Etsy kargo profili id'si (Etsy'de önceden oluşturulmuş; yayın için gerekli).</summary>
    public long? ShippingProfileId { get; set; }

    /// <summary>Etsy iade politikası id'si (bazı bölgelerde yayın için gerekli).</summary>
    public long? ReturnPolicyId { get; set; }

    /// <summary>Etsy dükkân bölümü id'si (opsiyonel).</summary>
    public long? ShopSectionId { get; set; }

    /// <summary>Etsy minimum işleme süresi (gün; opsiyonel).</summary>
    public int? ProcessingMin { get; set; }

    /// <summary>Etsy maksimum işleme süresi (gün; opsiyonel).</summary>
    public int? ProcessingMax { get; set; }

    /// <summary>Etsy başlık override (boşsa push'ta ürün adı devralınır).</summary>
    [StringLength(SalesChannelEtsyProductConsts.TitleOverrideMaxLength)]
    public string? TitleOverride { get; set; }

    /// <summary>Etsy açıklama override (boşsa push'ta ürün açıklaması devralınır).</summary>
    [StringLength(SalesChannelEtsyProductConsts.DescriptionOverrideMaxLength)]
    public string? DescriptionOverride { get; set; }

    /// <summary>Etsy kişiselleştirme açık mı. Varsayılan false.</summary>
    public bool IsPersonalizable { get; set; }

    /// <summary>Etsy kişiselleştirme talimatı (personalizable ise anlamlı).</summary>
    [StringLength(SalesChannelEtsyProductConsts.PersonalizationInstructionsMaxLength)]
    public string? PersonalizationInstructions { get; set; }

    /// <summary>Etsy kişiselleştirme zorunlu mu (personalizable ise anlamlı). Varsayılan false.</summary>
    public bool PersonalizationIsRequired { get; set; }

    /// <summary>Müşteri girişinin maksimum karakter sayısı (opsiyonel; personalizable ise anlamlı).</summary>
    public int? PersonalizationCharCountMax { get; set; }

    /// <summary>Listeleme süresi dolunca Etsy'de otomatik yenilensin mi (should_auto_renew). Varsayılan true.</summary>
    public bool ShouldAutoRenew { get; set; } = true;

    /// <summary>Kargoya verilme süresi (gün) — varsayılan 1.</summary>
    public int PreparingDay { get; set; } = 1;

    /// <summary>Etsy para birimi (opsiyonel; boşsa varyant para birimi devralınır).</summary>
    public Guid? CurrencyUnitId { get; set; }

    /// <summary>Satıcı notu (kanal-özel kısa düz not; opsiyonel).</summary>
    [StringLength(SalesChannelEtsyProductConsts.SellerNoteMaxLength)]
    public string? SellerNote { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Owned alt-graf koleksiyonları ──

    /// <summary>Etsy taksonomi varyasyon-DIŞI attribute değerleri (N11 CategoryAttributes deseni).</summary>
    public List<SalesChannelEtsyProductListingAttributeDto> ListingAttributes { get; set; } = new();

    /// <summary>Etsy etiketleri (≤13; sunucu kırpar). Tek-alanlı owned tip → düz string listesi.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Etsy malzemeleri (≤13; sunucu kırpar). Tek-alanlı owned tip → düz string listesi.</summary>
    public List<string> Materials { get; set; } = new();

    /// <summary>Kişiselleştirme özel bilgi (müşteri giriş etiketleri; key zorunlu / value opsiyonel).</summary>
    public List<SalesChannelEtsyProductSpecialInfoDto> SpecialInfo { get; set; } = new();

    /// <summary>Kanal-özel varyant override'ları (fiyat/stok/marj + reçete graf düğümleri) — ERP varyant seti ⋈
    /// kaydedilmiş override (LEFT JOIN). Ürün 'Kaydet'inde birlikte kaydedilir. NetCost/DerivedPrice SALT-OKUNUR.</summary>
    public List<SalesChannelEtsyProductStockItemGraphDto> StockItems { get; set; } = new();

    /// <summary>Etsy kendi varyant özellikleri (ör. "Renk"/"Beden") — İLK açılışta ERP nitelik/değerlerinden bir kez
    /// KLONLANIR, sonrasında ERP'den bağımsız yaşar. <see cref="StockItems"/> bu özelliklerin kartezyen
    /// kombinasyonundan üretilir (kaydet'te sunucu reconcile eder).</summary>
    public List<SalesChannelEtsyProductAttributeDto> ProductAttributes { get; set; } = new();

    /// <summary>Varyant SKU kimlik/durum satırları (read-only; push + stok/fiyat senkronunda dolar).</summary>
    public List<SalesChannelEtsyProductSkuDto> Skus { get; set; } = new();

    // ── Etsy senkron durumu (read-only; push sonrası dolar) ──

    public long? EtsyListingId { get; set; }
    public string? ListingState { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>Push sonrası eşitleme uyarıları (LOKALİZE) — SALT anlık görüntü, persist edilmez; yalnız push
    /// dönüşünde dolar (UI uyarı toast'ları gösterir).</summary>
    public List<string> SyncWarnings { get; set; } = new();
}

/// <summary>Create/Update ortak düzenlenebilir alanları.</summary>
public interface ISalesChannelEtsyProductInput
{
    long? TaxonomyId { get; }
    EtsyListingType ListingType { get; }
    long? ShippingProfileId { get; }
    long? ReturnPolicyId { get; }
    long? ShopSectionId { get; }
    int? ProcessingMin { get; }
    int? ProcessingMax { get; }
    string? TitleOverride { get; }
    string? DescriptionOverride { get; }
    bool IsPersonalizable { get; }
    string? PersonalizationInstructions { get; }
    bool PersonalizationIsRequired { get; }
    int? PersonalizationCharCountMax { get; }
    bool ShouldAutoRenew { get; }
    int PreparingDay { get; }
    Guid? CurrencyUnitId { get; }
    string? SellerNote { get; }
    bool IsActive { get; }
    List<SalesChannelEtsyProductListingAttributeDto> ListingAttributes { get; }
    List<string> Tags { get; }
    List<string> Materials { get; }
    List<SalesChannelEtsyProductSpecialInfoDto> SpecialInfo { get; }

    /// <summary>Kanal-özel varyant override grafı (fiyat/stok/marj + reçete) — kanal-ürünle birlikte kaydedilir.</summary>
    List<SalesChannelEtsyProductStockItemGraphDto> StockItems { get; }

    /// <summary>Etsy kendi varyant özellikleri — kanal-ürünle birlikte kaydedilir (kartezyen reconcile tetikler).</summary>
    List<SalesChannelEtsyProductAttributeDto> ProductAttributes { get; }
}

/// <summary>Listeleme oluşturma — ürün + kanal (create-only; şirket sunucuda zorlanır).</summary>
public class SalesChannelEtsyProductCreateDto : ISalesChannelEtsyProductInput
{
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public long? TaxonomyId { get; set; }
    public EtsyListingType ListingType { get; set; } = EtsyListingType.Physical;
    public long? ShippingProfileId { get; set; }
    public long? ReturnPolicyId { get; set; }
    public long? ShopSectionId { get; set; }
    public int? ProcessingMin { get; set; }
    public int? ProcessingMax { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.TitleOverrideMaxLength)]
    public string? TitleOverride { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.DescriptionOverrideMaxLength)]
    public string? DescriptionOverride { get; set; }

    public bool IsPersonalizable { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.PersonalizationInstructionsMaxLength)]
    public string? PersonalizationInstructions { get; set; }

    public bool PersonalizationIsRequired { get; set; }
    public int? PersonalizationCharCountMax { get; set; }
    public bool ShouldAutoRenew { get; set; } = true;

    public int PreparingDay { get; set; } = 1;
    public Guid? CurrencyUnitId { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.SellerNoteMaxLength)]
    public string? SellerNote { get; set; }

    public bool IsActive { get; set; } = true;
    public List<SalesChannelEtsyProductListingAttributeDto> ListingAttributes { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Materials { get; set; } = new();
    public List<SalesChannelEtsyProductSpecialInfoDto> SpecialInfo { get; set; } = new();
    public List<SalesChannelEtsyProductStockItemGraphDto> StockItems { get; set; } = new();
    public List<SalesChannelEtsyProductAttributeDto> ProductAttributes { get; set; } = new();
}

/// <summary>Listeleme güncelleme — ürün/kanal set-once (route'taki id kimliktir).</summary>
public class SalesChannelEtsyProductUpdateDto : ISalesChannelEtsyProductInput
{
    public long? TaxonomyId { get; set; }
    public EtsyListingType ListingType { get; set; } = EtsyListingType.Physical;
    public long? ShippingProfileId { get; set; }
    public long? ReturnPolicyId { get; set; }
    public long? ShopSectionId { get; set; }
    public int? ProcessingMin { get; set; }
    public int? ProcessingMax { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.TitleOverrideMaxLength)]
    public string? TitleOverride { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.DescriptionOverrideMaxLength)]
    public string? DescriptionOverride { get; set; }

    public bool IsPersonalizable { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.PersonalizationInstructionsMaxLength)]
    public string? PersonalizationInstructions { get; set; }

    public bool PersonalizationIsRequired { get; set; }
    public int? PersonalizationCharCountMax { get; set; }
    public bool ShouldAutoRenew { get; set; } = true;

    public int PreparingDay { get; set; } = 1;
    public Guid? CurrencyUnitId { get; set; }

    [StringLength(SalesChannelEtsyProductConsts.SellerNoteMaxLength)]
    public string? SellerNote { get; set; }

    public bool IsActive { get; set; } = true;
    public List<SalesChannelEtsyProductListingAttributeDto> ListingAttributes { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Materials { get; set; } = new();
    public List<SalesChannelEtsyProductSpecialInfoDto> SpecialInfo { get; set; } = new();
    public List<SalesChannelEtsyProductStockItemGraphDto> StockItems { get; set; } = new();
    public List<SalesChannelEtsyProductAttributeDto> ProductAttributes { get; set; } = new();
}
