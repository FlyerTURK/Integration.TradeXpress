using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Products;

/// <summary>Product liste sorgusu (per-tenant). Company-scoped: sunucu <see cref="ICurrentCompany"/> ile daraltır
/// (client CompanyId GÖNDERMEZ — AssayOffice deseni). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class ProductListRequestDto : ListRequestDto
{
}

public class ProductListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>Ürüne bağlı (silinmemiş) varyant sayısı — grid göstergesi.</summary>
    public int VariantCount { get; set; }

    /// <summary>Varsayılan (ana) görselin küçük önizlemesi — grid thumbnail'i. Url kaynağında direkt bağlantı,
    /// Upload kaynağında THUMBNAIL data-URL'i (tam çözünürlük DTO'ya gömülmez). Görsel yoksa null.</summary>
    public string? ImagePreviewUrl { get; set; }
}

public class ProductGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Çekirdek ürün kategorisi (id-only; <c>null</c> = sınıflandırılmamış). Kanal kategorisi,
    /// kanal nitelikleri ve kategori komisyonu bu bağdan çözülür.</summary>
    public Guid? ProductCategoryId { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Marketplace indirimi (ürün-seviyesi; tüm varyant + kanallar). None = indirim yok.</summary>
    public ProductDiscountType DiscountType { get; set; } = ProductDiscountType.None;

    /// <summary>İndirim değeri — Amount'ta tutar, Percentage'ta yüzde (0–100). None ise yoksayılır.</summary>
    public decimal? DiscountValue { get; set; }

    /// <summary>İndirim başlangıcı (iş tarihi, date-only). None ise yoksayılır.</summary>
    public DateTime? DiscountStartDate { get; set; }

    /// <summary>İndirim bitişi (iş tarihi, date-only). None ise yoksayılır.</summary>
    public DateTime? DiscountEndDate { get; set; }

    /// <summary>Üretim tarihi (iş tarihi, date-only; N11 productionDate). Opsiyonel.</summary>
    public DateTime? ProductionDate { get; set; }

    /// <summary>Son kullanma tarihi (iş tarihi, date-only; N11 expirationDate). Opsiyonel.</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Pazaryeri-genel varsayılanlar (kanal-ürünü devralır + override eder).</summary>
    /// <summary>Ürünün MENŞEİ ÜLKESİ (id-only) — yeni üründe şirketin ülkesiyle dolar. N11'in "yerli ürün"
    /// bayrağı bundan türetilir (bkz. <see cref="IsDomestic"/>).</summary>
    public Guid? OriginCountryId { get; set; }

    /// <summary>SALT-OKUNUR: menşei ülke şirketin ülkesiyle aynı mı (N11 <c>domestic</c> bayrağının kaynağı).
    /// Sunucu hesaplar; kaydetmede yok sayılır. Menşei belirtilmemişse <c>null</c>.</summary>
    public bool? IsDomestic { get; set; }
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public int PreparingDay { get; set; } = ProductConsts.DefaultPreparingDay;



    /// <summary>Reçete şablonu ("orta reçete") — ürüne KALICI bağ; ara masraf satırlarının (paketleme/kargo/
    /// sigorta) kaynağı. Muadil yeniden üretimde reçete buradan tazelenir.</summary>
    public Guid? RecipeTemplateId { get; set; }

    /// <summary>Paket desisi — kargo tarifesinin girdisi. Boş = varyantın ya da kanalın varsayılanı.</summary>
    public int? PackageDesi { get; set; } = 0;

    public int? MaxPurchaseQuantity { get; set; }

    [StringLength(ProductConsts.SellerNoteMaxLength)]
    public string? SellerNote { get; set; }

    /// <summary>Varsayılan para birimi (id-only; kanal-ürünü boşsa devralır).</summary>
    public Guid? CurrencyUnitId { get; set; }

    /// <summary>Ürünün GENEL ÖZELLİKLERİ — kategorinin spesifikasyon niteliklerinin bu üründeki değerleri
    /// ("Ayar: 22K", "Gramaj: 8.4"). Hangi özelliklerin sorulacağını KATEGORİ belirler (kalıtım dahil), değeri
    /// kullanıcı ürün formunda girer. Pazaryeri push'unda kanal nitelik değerlerini besler.
    /// <para>Varyant niteliklerinden AYRIDIR: bunlar kartezyene girmez, ürünün tamamını niteler.</para></summary>
    public List<ProductSpecificationDto> Specifications { get; set; } = new();

    /// <summary>Ürün özelleştirme alanları (key zorunlu / value opsiyonel; in-memory drill).</summary>
    public List<ProductSpecialInfoDto> SpecialInfo { get; set; } = new();

    /// <summary>Ürüne atanan eklentiler (katalogdan seçim + satır override; in-memory drill).</summary>
    public List<ProductAddOnDto> AddOns { get; set; } = new();

    // Kişiselleştirme alanları 2026-07-28'de kaldırıldı — kişiselleştirmenin taşıyıcısı artık SpecialInfo.

    /// <summary>Varyant üretim tercihi — varsayılan MultiVariant (statüko). SingleVariant/Substitution'da
    /// nitelik-tabanlı üretim BYPASS (sunucu kapısı nitelik grafını boşaltır → tek ana varyant).</summary>
    public ProductVariantMode VariantMode { get; set; } = ProductVariantMode.MultiVariant;

    /// <summary>Muadil grubu referansı (id-only) — yalnız Substitution modunda anlamlı (zorunlu; sunucu fail-fast).</summary>
    public Guid? SubstitutionGroupId { get; set; }

    /// <summary>Kombinasyon hedef miktarı (gram) — Substitution modunda zorunlu, &gt; 0.</summary>
    public decimal? SubstitutionTargetQuantity { get; set; }

    /// <summary>Tolerans türü override'ı — boş = grubun tolerans politikası (değerle çift dolar).</summary>
    public ToleranceType? SubstitutionToleranceType { get; set; }

    /// <summary>Tolerans değeri override'ı — boş = grup ayarı; negatif olamaz (sunucu fail-fast).</summary>
    public decimal? SubstitutionToleranceValue { get; set; }

    /// <summary>Ürün-düzeyi varyant OVERRIDE kümesi — BOŞ = gruptan devral (resolver: override ?? included ?? ana);
    /// dolu ise grup kalemlerinin varyant kapsamını tamamen ezer. Override ağacı in-memory düzenler.</summary>
    public List<Guid> SubstitutionOverrideVariantIds { get; set; } = new();

    /// <summary>Muadil kombinasyon→varyant biçimi (Tek/Çoklu) — yalnız Substitution modunda anlamlı.</summary>
    public SubstitutionVariantMode SubstitutionVariantMode { get; set; }

    /// <summary>Kanal stok kaynağı — Fixed (statüko) / Calculated / Unlimited. Muadilde sunucu Calculated'ı zorlar.</summary>
    public ProductStockPolicy StockPolicy { get; set; }

    /// <summary>Ürün MEDYA linkleri (merkezi kütüphane; görsel + video birlikte — <see cref="IEntityMediaAppService"/>).
    /// Pazaryeri push görselleri de BURADAN gider (legacy Images 2026-07-31'de emekli): pasif elenir, kapak önce.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();

    /// <summary>N11 satış kanalı ürünleri (graf düğümleri; ClientKey/Id + IsDeleted diff) — ürün 'Kaydet'inde
    /// birlikte kaydedilir (yeni üründe de eklenebilir). Panel in-memory yönetir; sunucu SellerCode/Sıra üretir.</summary>
    public List<SalesChannelTrN11ProductDto> SalesChannelProducts { get; set; } = new();

    /// <summary>Trendyol satış kanalı ürünleri (graf düğümleri; ClientKey/Id + IsDeleted diff) — N11'den AYRI ikinci
    /// liste (eleştiri F1: iki ayrı liste). Ürün 'Kaydet'inde birlikte kaydedilir; sunucu ProductMainId/Sıra üretir.</summary>
    public List<SalesChannelTrTrendyolProductDto> SalesChannelTrendyolProducts { get; set; } = new();

    /// <summary>Etsy satış kanalı ürünleri (graf düğümleri; ClientKey/Id + IsDeleted diff) — N11/Trendyol'dan AYRI
    /// üçüncü liste. Ürün 'Kaydet'inde birlikte kaydedilir; sunucu SellerSkuBase/Sıra üretir.</summary>
    public List<SalesChannelEtsyProductDto> SalesChannelEtsyProducts { get; set; } = new();

    /// <summary>Varyantlar (graf düğümleri; Id + IsDeleted ile diff). Product edit formundaki drill yönetir.</summary>
    public List<ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>Nitelikler (varyant eksenleri; değerleriyle birlikte graf). Varyantlar bunların
    /// kartezyeninden sunucuda ÜRETİLİR (ProductVariantSynchronizer).</summary>
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
}

public class ProductCreateDto : ICreateDto
{
    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Çekirdek ürün kategorisi — ZORUNLU. Kanal kategorisi, kanal nitelikleri ve kategori komisyonu
    /// bu bağdan çözülür; kategorisiz ürün pazaryerine listelenemez ve fiyatı komisyonsuz hesaplanır.
    /// Tip <c>Guid?</c> kalır ki "seçilmedi" hâli sunucuya ULAŞSIN ve lokalize iş hatası dönebilsin —
    /// <c>Guid</c> olsaydı boş seçim sessizce <c>Guid.Empty</c>'ye düşerdi.</summary>
    public Guid? ProductCategoryId { get; set; }

    /// <summary>Marketplace indirimi — bkz. <see cref="ProductGetDto.DiscountType"/>.</summary>
    public ProductDiscountType DiscountType { get; set; } = ProductDiscountType.None;
    public decimal? DiscountValue { get; set; }
    public DateTime? DiscountStartDate { get; set; }
    public DateTime? DiscountEndDate { get; set; }

    /// <summary>Üretim/son kullanma tarihleri — bkz. <see cref="ProductGetDto.ProductionDate"/>.</summary>
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Pazaryeri-genel varsayılanlar — bkz. <see cref="ProductGetDto.OriginCountryId"/>.</summary>
    public Guid? OriginCountryId { get; set; }
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public int PreparingDay { get; set; } = ProductConsts.DefaultPreparingDay;



    /// <summary>Reçete şablonu ("orta reçete") — ürüne KALICI bağ; ara masraf satırlarının (paketleme/kargo/
    /// sigorta) kaynağı. Muadil yeniden üretimde reçete buradan tazelenir.</summary>
    public Guid? RecipeTemplateId { get; set; }

    /// <summary>Paket desisi — kargo tarifesinin girdisi. Boş = varyantın ya da kanalın varsayılanı.</summary>
    public int? PackageDesi { get; set; } = 0;

    public int? MaxPurchaseQuantity { get; set; }

    [StringLength(ProductConsts.SellerNoteMaxLength)]
    public string? SellerNote { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    /// <summary>Ürünün GENEL ÖZELLİKLERİ — kategorinin spesifikasyon niteliklerinin bu üründeki değerleri
    /// ("Ayar: 22K", "Gramaj: 8.4"). Hangi özelliklerin sorulacağını KATEGORİ belirler (kalıtım dahil), değeri
    /// kullanıcı ürün formunda girer. Pazaryeri push'unda kanal nitelik değerlerini besler.
    /// <para>Varyant niteliklerinden AYRIDIR: bunlar kartezyene girmez, ürünün tamamını niteler.</para></summary>
    public List<ProductSpecificationDto> Specifications { get; set; } = new();

    public List<ProductSpecialInfoDto> SpecialInfo { get; set; } = new();

    /// <summary>Ürüne atanan eklentiler (katalogdan seçim + satır override; in-memory drill).</summary>
    public List<ProductAddOnDto> AddOns { get; set; } = new();

    /// <summary>Varyant modu + Muadil konfigürasyonu — bkz. <see cref="ProductGetDto.VariantMode"/>.</summary>
    public ProductVariantMode VariantMode { get; set; } = ProductVariantMode.MultiVariant;
    public Guid? SubstitutionGroupId { get; set; }
    public decimal? SubstitutionTargetQuantity { get; set; }
    public ToleranceType? SubstitutionToleranceType { get; set; }
    public decimal? SubstitutionToleranceValue { get; set; }
    public List<Guid> SubstitutionOverrideVariantIds { get; set; } = new();

    /// <summary>Muadil kombinasyon→varyant biçimi (Tek/Çoklu) — yalnız Substitution modunda anlamlı.</summary>
    public SubstitutionVariantMode SubstitutionVariantMode { get; set; }

    /// <summary>Kanal stok kaynağı — Fixed (statüko) / Calculated / Unlimited. Muadilde sunucu Calculated'ı zorlar.</summary>
    public ProductStockPolicy StockPolicy { get; set; }

    /// <summary>Ürün MEDYA linkleri (görsel + video kütüphanesi) — bkz. <see cref="ProductGetDto.Media"/>.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();

    /// <summary>N11 satış kanalı ürünleri grafı — bkz. <see cref="ProductGetDto.SalesChannelProducts"/>.</summary>
    public List<SalesChannelTrN11ProductDto> SalesChannelProducts { get; set; } = new();

    /// <summary>Trendyol satış kanalı ürünleri grafı — bkz. <see cref="ProductGetDto.SalesChannelTrendyolProducts"/>.</summary>
    public List<SalesChannelTrTrendyolProductDto> SalesChannelTrendyolProducts { get; set; } = new();

    /// <summary>Etsy satış kanalı ürünleri grafı — bkz. <see cref="ProductGetDto.SalesChannelEtsyProducts"/>.</summary>
    public List<SalesChannelEtsyProductDto> SalesChannelEtsyProducts { get; set; } = new();

    public List<ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>Nitelik grafı — bkz. <see cref="ProductGetDto.Attributes"/>.</summary>
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
}

public class ProductUpdateDto : IUpdateDto
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04). Scope'lu benzersizlik AppService'te.
    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Çekirdek ürün kategorisi (id-only; <c>null</c> = sınıflandırılmamış). Kanal kategorisi,
    /// kanal nitelikleri ve kategori komisyonu bu bağdan çözülür.</summary>
    public Guid? ProductCategoryId { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Marketplace indirimi — bkz. <see cref="ProductGetDto.DiscountType"/>.</summary>
    public ProductDiscountType DiscountType { get; set; } = ProductDiscountType.None;
    public decimal? DiscountValue { get; set; }
    public DateTime? DiscountStartDate { get; set; }
    public DateTime? DiscountEndDate { get; set; }

    /// <summary>Üretim/son kullanma tarihleri — bkz. <see cref="ProductGetDto.ProductionDate"/>.</summary>
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Pazaryeri-genel varsayılanlar — bkz. <see cref="ProductGetDto.OriginCountryId"/>.</summary>
    public Guid? OriginCountryId { get; set; }
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public int PreparingDay { get; set; } = ProductConsts.DefaultPreparingDay;



    /// <summary>Reçete şablonu ("orta reçete") — ürüne KALICI bağ; ara masraf satırlarının (paketleme/kargo/
    /// sigorta) kaynağı. Muadil yeniden üretimde reçete buradan tazelenir.</summary>
    public Guid? RecipeTemplateId { get; set; }

    /// <summary>Paket desisi — kargo tarifesinin girdisi. Boş = varyantın ya da kanalın varsayılanı.</summary>
    public int? PackageDesi { get; set; } = 0;

    public int? MaxPurchaseQuantity { get; set; }

    [StringLength(ProductConsts.SellerNoteMaxLength)]
    public string? SellerNote { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    /// <summary>Ürünün GENEL ÖZELLİKLERİ — kategorinin spesifikasyon niteliklerinin bu üründeki değerleri
    /// ("Ayar: 22K", "Gramaj: 8.4"). Hangi özelliklerin sorulacağını KATEGORİ belirler (kalıtım dahil), değeri
    /// kullanıcı ürün formunda girer. Pazaryeri push'unda kanal nitelik değerlerini besler.
    /// <para>Varyant niteliklerinden AYRIDIR: bunlar kartezyene girmez, ürünün tamamını niteler.</para></summary>
    public List<ProductSpecificationDto> Specifications { get; set; } = new();

    public List<ProductSpecialInfoDto> SpecialInfo { get; set; } = new();

    /// <summary>Ürüne atanan eklentiler (katalogdan seçim + satır override; in-memory drill).</summary>
    public List<ProductAddOnDto> AddOns { get; set; } = new();

    /// <summary>Varyant modu + Muadil konfigürasyonu — bkz. <see cref="ProductGetDto.VariantMode"/>.</summary>
    public ProductVariantMode VariantMode { get; set; } = ProductVariantMode.MultiVariant;
    public Guid? SubstitutionGroupId { get; set; }
    public decimal? SubstitutionTargetQuantity { get; set; }
    public ToleranceType? SubstitutionToleranceType { get; set; }
    public decimal? SubstitutionToleranceValue { get; set; }
    public List<Guid> SubstitutionOverrideVariantIds { get; set; } = new();

    /// <summary>Muadil kombinasyon→varyant biçimi (Tek/Çoklu) — yalnız Substitution modunda anlamlı.</summary>
    public SubstitutionVariantMode SubstitutionVariantMode { get; set; }

    /// <summary>Kanal stok kaynağı — Fixed (statüko) / Calculated / Unlimited. Muadilde sunucu Calculated'ı zorlar.</summary>
    public ProductStockPolicy StockPolicy { get; set; }

    /// <summary>Ürün MEDYA linkleri (görsel + video kütüphanesi) — bkz. <see cref="ProductGetDto.Media"/>.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();

    /// <summary>N11 satış kanalı ürünleri grafı — bkz. <see cref="ProductGetDto.SalesChannelProducts"/>.</summary>
    public List<SalesChannelTrN11ProductDto> SalesChannelProducts { get; set; } = new();

    /// <summary>Trendyol satış kanalı ürünleri grafı — bkz. <see cref="ProductGetDto.SalesChannelTrendyolProducts"/>.</summary>
    public List<SalesChannelTrTrendyolProductDto> SalesChannelTrendyolProducts { get; set; } = new();

    /// <summary>Etsy satış kanalı ürünleri grafı — bkz. <see cref="ProductGetDto.SalesChannelEtsyProducts"/>.</summary>
    public List<SalesChannelEtsyProductDto> SalesChannelEtsyProducts { get; set; } = new();

    public List<ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>Nitelik grafı — bkz. <see cref="ProductGetDto.Attributes"/>.</summary>
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
}

/// <summary>Ürün özelleştirme alanı (serbest key/value; her pazaryerine varsayılan). <see cref="ClientKey"/> yalnız
/// in-memory DrillList satır kimliği (persist edilmez; entity Key/Value tutar). Key zorunlu, Value opsiyonel.</summary>
public class ProductSpecialInfoDto
{
    /// <summary>İstemci-taraflı satır kimliği (DrillList grid identity) — persist edilmez.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [StringLength(ProductConsts.SpecialInfoKeyMaxLength)]
    public string Key { get; set; } = string.Empty;

    [StringLength(ProductConsts.SpecialInfoValueMaxLength)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>Ürüne atanan eklenti satırı (katalog referansı + override). <see cref="ClientKey"/> yalnız in-memory
/// DrillList satır kimliği (persist edilmez). <see cref="AddOnId"/> zorunlu (boş satır elenir); fiyat/para birimi
/// override null ise katalog varsayılanı devralınır.</summary>
public class ProductAddOnDto
{
    /// <summary>İstemci-taraflı satır kimliği (DrillList grid identity) — persist edilmez.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid AddOnId { get; set; }

    public decimal? PriceOverride { get; set; }

    public Guid? CurrencyUnitOverrideId { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    [StringLength(ProductConsts.AddOnNoteMaxLength)]
    public string? Note { get; set; }
}

/// <summary>
/// Product grafının varyant DÜĞÜMÜ — jenerik <see cref="EntityVariantGraphDto"/> (çekirdek: Kod/Ad/Barkod/Stok/…)
/// + Product-ÖZEL satış fiyatı + reçete UZANTISI. Satış fiyatı VARYANT seviyesinde (ProductVariantDetail tablosu),
/// reçete satırları EntityVariantId'ye bağlı. <c>EntityVariantsPanel&lt;ProductVariantGraphDto&gt;</c>'ın ExtraFields
/// slot'unda bu alanlar bind edilir; ProductAppService jenerik çekirdeği kaydettikten sonra bu alanları
/// ProductVariantDetail + reçete satırlarına (EntityVariantId ile) saklar/yükler. GoodVariantGraphDto deseni.
/// </summary>
public class ProductVariantGraphDto : EntityVariantGraphDto
{
    /// <summary>Satış/liste fiyatı (marketplace price/optionPrice). Null = fiyatlanmamış. Negatif geçersiz (sunucu zorlar).</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>Satış fiyatı para birimi (CurrencyUnit id-only; N11'de currencyType'a eşlenir). Fiyat null ise yoksayılır.</summary>
    public Guid? SalePriceCurrencyUnitId { get; set; }

    /// <summary>Varyantın REÇETE satırları (design-time maliyet bileşenleri; graf düğümleri, Id + IsDeleted diff).
    /// Product edit formundaki reçete drill'i yönetir; Product save'inde varyant-scope kalıcılaşır.</summary>
    public List<ProductRecipeLineGraphDto> RecipeLines { get; set; } = new();

    /// <summary>Varyantın CANLI net maliyeti — ülke para birimine rebase'li toplam (SALT-OKUNUR, GetAsync projeksiyonunda
    /// hesaplanır; save'de YOKSAYILIR). Ülke birimi ya da kur çözülemezse null.</summary>
    public decimal? NetCost { get; set; }

    /// <summary>Net maliyetin para birimi kodu (ülke birimi; ör. "TRY"). SALT-OKUNUR görüntü.</summary>
    public string NetCostCurrency { get; set; } = string.Empty;

    /// <summary>Net maliyetin en az bir satırında kur/birim eksik mi — UI uyarısı (SALT-OKUNUR).</summary>
    public bool NetCostMissingRate { get; set; }
}

/// <summary>
/// Bir varyant reçetesinin tek satırı (design-time maliyet bileşeni) — in-memory drill düğümü + Product save'i içindir
/// (ProductVariantGraphDto deseni). Durum = <see cref="Id"/> + <see cref="IsDeleted"/>. <b>Net/tutar dondurulmaz</b>:
/// <see cref="LineCost"/> SALT-OKUNUR, GetAsync projeksiyonunda canlı hesaplanır (kur değişince güncellenir).
/// </summary>
public class ProductRecipeLineGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    /// <summary>Satırın KAYNAĞI — SALT GÖSTERİM/ayırt etme için (sunucu doldurur; kaydetmede istemciden
    /// GELEN değer kullanılmaz, kaynak sunucuda belirlenir). Muadil kombinasyonu uygulanırken şablondan
    /// gelen satırların korunması bu bilgiye dayanır.</summary>
    public RecipeLineOrigin Origin { get; set; }

    public int LineOrder { get; set; }

    /// <summary>Bileşen türü — toolbar butonu belirler (Maden/Hurda/Vadeli/Mücevher/Taş → CatalogCommodity;
    /// Hizmet → Service; Manuel → ManualCost).</summary>
    public RecipeComponentType ComponentType { get; set; } = RecipeComponentType.CatalogCommodity;

    /// <summary>Katalog emtia ailesi (Metal/Scrap/Future/Jewelry/Stone) — yalnız CatalogCommodity'de dolu.</summary>
    public ProcessType? CommodityProcessType { get; set; }

    /// <summary>Seçili katalog kaydı (snapshot ref) ya da hizmet referansı. Manuelde boş.</summary>
    public Guid? CommodityId { get; set; }

    /// <summary>Seçili katalog varyantı (snapshot ref) — Çoklu varyantı olan emtialarda seçili varyant.</summary>
    public Guid? CommodityVariantId { get; set; }

    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal Factor { get; set; }

    /// <summary>Doğal-birim snapshot'ı (rebase kaynağı; VoucherLine.MainUnitId rolü) — metal-bacaklıda
    /// FollowingUnit, parasalda EntryPrice birimi.</summary>
    public Guid? ValuationUnitId { get; set; }

    /// <summary>Ana (doğal) birimin kodu (ör. "HAS") — SALT-OKUNUR görüntü (GetAsync projeksiyonu).</summary>
    public string MainUnitCode { get; set; } = string.Empty;

    /// <summary>Ödeme tipi — reçetede yalnız Normal (metal + işçilik bacağı) ve WithCurrency/Bedelli (sabit bedel = tek bacak).</summary>
    public ProcessPaymentType PaymentType { get; set; } = ProcessPaymentType.Normal;

    /// <summary>Ana bacak toplamı (Amount×Factor, doğal birimde) — TÜRETİLMİŞ, SALT-OKUNUR (persist yok).</summary>
    public decimal Total { get; set; }

    /// <summary>Karşı bacak birim fiyatı (N5) — Normal'de işçilik rate'i (adet/miktar başına), Bedelli'de 1 ana-birim başına bedel.</summary>
    public decimal PayFactor { get; set; }

    /// <summary>Karşı bacak toplamı — TÜRETİLMİŞ, SALT-OKUNUR: Normal'de PayFactor×(adet|miktar), Bedelli'de Total×PayFactor.</summary>
    public decimal PayTotal { get; set; }

    /// <summary>Karşı bacak birimi (işçilik/bedel birimi) — snapshot.</summary>
    public Guid? PayUnitId { get; set; }

    /// <summary>Karşı bacak biriminin kodu — SALT-OKUNUR görüntü (GetAsync projeksiyonu).</summary>
    public string PayUnitCode { get; set; } = string.Empty;

    /// <summary>Hizmet/manuel sabit tutar (non-null → NumericSpinEdit ValueExpression için 0m default).</summary>
    public decimal ManualAmount { get; set; }

    /// <summary>Hizmet/manuel tutar birimi.</summary>
    public Guid? ManualUnitId { get; set; }

    [StringLength(ProductRecipeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Satırın CANLI maliyeti — ülke birimine rebase'li (SALT-OKUNUR, GetAsync projeksiyonu; save'de yoksayılır).</summary>
    public decimal? LineCost { get; set; }

    /// <summary>Satırın doğal-birim kuru çözülemedi mi (kur/birim eksik) — UI göstergesi (SALT-OKUNUR).</summary>
    public bool LineCostMissingRate { get; set; }

    /// <summary>Uygulanacak Bedel — Hizmet satırının türev işlemi uyguladığı taban (devralınan toplam ya da seçili
    /// satırlar toplamı), ülke birimi. Fiziki satırda null. SALT-OKUNUR (GetAsync projeksiyonu).</summary>
    public decimal? AppliedBase { get; set; }

    /// <summary>Ara Toplam — o satır DAHİL koşan toplam, Company.Country.CurrencyUnit'e rebase'li. SALT-OKUNUR.</summary>
    public decimal? RunningSubtotal { get; set; }

    // ── Hizmet satırının türevsel bedel kuralı (pilot) — yalnız ComponentType == Service'de dolu ──

    /// <summary>Devralınan taban SWITCH'i (tüm üst satırlar / seçili kalemler). Türev-dışı satırda null.</summary>
    public RecipeDerivedBaseMode? DerivedBaseMode { get; set; }

    /// <summary>Devralınan tabana uygulanan işlem (ekle/çarp/yüzde/brütleştir). Türev-dışı satırda null.</summary>
    public RecipeDerivedOperation? DerivedOperation { get; set; }

    /// <summary>İşlem operand'ı — Add: mutlak tutar; Multiply: çarpan; Percent/GrossUp: yüzde.</summary>
    public decimal DerivedOperand { get; set; }

    /// <summary>SelectedLines modunda seçili kaynak satırların <see cref="ClientKey"/>'leri (aynı reçetenin KARDEŞ
    /// satırları). Client düzenler + round-trip eder; save'de Id'lere çözülür, GetAsync'te o oturumun taze
    /// ClientKey'lerine geri çevrilir. AllAbove/türev-dışı satırda boş.</summary>
    public List<Guid> DerivedSourceKeys { get; set; } = new();

    /// <summary>Yan-maliyet türü — kanal gider ayarlarından OTOMATİK üretilen satır işareti (SideCostRecipeComposer
    /// idempotent reconcile anahtarı). Null = kullanıcı satırı. UI'da görsel ayırt için de kullanılır.</summary>
    public SideCostKind? SideCostKind { get; set; }
}

/// <summary>Persistsiz varyant üretim isteği (önizleme): nitelik grafı + ad türetmesi için ürün adı.
/// DB'ye YAZMAZ — kartezyen hesaplanır, varyant graf satırları döner (kalıcılaşma Product save'inde).</summary>
public class ProductVariantGenerateRequestDto
{
    /// <summary>Varyant AD türetmesi için ürün adı ("Ürün Kırmızı M") — synchronizer paritesi. Boşsa yalnız değer adları.</summary>
    public string? ProductName { get; set; }

    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
}

/// <summary>Persistsiz reçete maliyeti hesap isteği — bir varyantın in-memory reçete satırları (TAM KAYIT gerekmez;
/// canlı maliyet için). DB'ye YAZMAZ.</summary>
public class ProductRecipeCostRequestDto
{
    public List<ProductRecipeLineGraphDto> Lines { get; set; } = new();
}

/// <summary>Persistsiz reçete maliyeti sonucu — varyant net'i + satır-başı maliyet alanları (Uygulanacak Bedel /
/// Satır Maliyeti / Ara Toplam, ClientKey ile eşlenir). GetAsync projeksiyonuyla AYNI motor.</summary>
public class ProductRecipeCostResultDto
{
    public decimal? NetCost { get; set; }
    public string NetCostCurrency { get; set; } = string.Empty;
    public bool NetCostMissingRate { get; set; }
    public List<ProductRecipeLineGraphDto> Lines { get; set; } = new();
}

/// <summary>
/// Ürünün bir genel özelliğinin değeri. <see cref="ProductCategoryAttributeId"/> kategorideki niteliğin KALICI
/// kimliğidir (ada değil kimliğe bağlanır → nitelik yeniden adlandırılınca değer kopmaz).
///
/// <para><see cref="Name"/> salt GÖSTERİM (sunucu kategoriden doldurur); kaydetmede yok sayılır — tek doğru
/// kaynak kategori tanımıdır, istemcinin gönderdiği ad değil.</para>
/// </summary>
public class ProductSpecificationDto
{
    public Guid Id { get; set; }

    public Guid ProductCategoryAttributeId { get; set; }

    /// <summary>Nitelik adı — salt gösterim ("Ayar").</summary>
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.SpecificationValueMaxLength)]
    public string? Value { get; set; }

    public override string ToString()
    {
        return Name + ": " + Value;
    }
}

/// <summary>
/// Ürünün genel özelliklerinin bir KANALA çevrilmiş hâli — kanal ürünü kurulurken nitelik alanlarını doldurur.
/// <para>N11 (ve benzerleri) nitelikleri AD + DEĞER METNİ ile alır; kimlikler eşleştirme katmanında kalır.</para>
/// </summary>
public class ProductChannelAttributeDto
{
    /// <summary>Kanaldaki nitelik adı ("Maden Ayarı").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Kanala gidecek değer ("22 Ayar" — eşleştirme varsa kanal karşılığı, yoksa ürünün kendi metni).</summary>
    public string Value { get; set; } = string.Empty;

    public override string ToString()
    {
        return Name + ": " + Value;
    }
}

/// <summary>Kanal nitelik çözümü girdisi — ürün HENÜZ KAYDEDİLMEMİŞ olabileceğinden özellik değerleri
/// istemciden gelir (kaydedilmiş ürün için de aynı yol kullanılır; tek kod yolu).</summary>
public class ProductChannelAttributeResolveDto
{
    public Guid ProductCategoryId { get; set; }

    public SalesChannelType Channel { get; set; }

    public List<ProductSpecificationDto> Specifications { get; set; } = new();
}
