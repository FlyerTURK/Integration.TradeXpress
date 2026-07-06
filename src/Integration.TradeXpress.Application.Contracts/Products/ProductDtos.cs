using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
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

    public bool IsActive { get; set; }

    /// <summary>Varyantlar (graf düğümleri; Id + IsDeleted ile diff). Product edit formundaki drill yönetir.</summary>
    public List<ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>Nitelikler (varyant eksenleri; değerleriyle birlikte graf). Varyantlar bunların
    /// kartezyeninden sunucuda ÜRETİLİR (ProductVariantSynchronizer).</summary>
    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
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

    public List<ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>Nitelik grafı — bkz. <see cref="ProductGetDto.Attributes"/>.</summary>
    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
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

    public bool IsActive { get; set; }

    public List<ProductVariantGraphDto> Variants { get; set; } = new();

    /// <summary>Nitelik grafı — bkz. <see cref="ProductGetDto.Attributes"/>.</summary>
    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
}

/// <summary>
/// Product grafının varyant DÜĞÜMÜ — Product edit'inde in-memory drill + Product save'i içindir (SubAccountGraphDto
/// deseni). Durum = <see cref="Id"/> + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil, aksi → güncelle.
/// <see cref="IsMain"/> DISPLAY-ONLY (ana varyant değişmezi <c>ProductVariantManager</c>'da; Adım 1'de UI'dan seçilmez).
/// </summary>
public class ProductVariantGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    /// <summary>Ana (main) varyant mı — DISPLAY-ONLY göstergesi (manager yönetir; drill'de düzenlenmez).</summary>
    public bool IsMain { get; set; }

    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Varyantın nitelik-değer KOMBİNASYON özeti (ör. "Kırmızı / M") — SALT-OKUNUR görüntü alanı.
    /// GetAsync projeksiyonunda doldurulur (attribute DisplayOrder sırasıyla " / " join); save'de YOKSAYILIR.</summary>
    public string AttributeSummary { get; set; } = string.Empty;

    /// <summary>Kombinasyonun İSTEMCİ-taraflı kimliği — ilgili DEĞERLERİN <see cref="ProductAttributeValueGraphDto.ClientKey"/>'lerinin
    /// sıralı "|" join'i. <c>GenerateVariantsAsync</c> doldurur, client round-trip eder; kayıtta Id'siz (henüz DB'de olmayan)
    /// üretilmiş satırın özelleştirmelerini (Code/Name/Description/IsActive) senkron sonrası DB varyantına EŞLEMEK içindir.</summary>
    public string CombinationKey { get; set; } = string.Empty;

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

    public int LineOrder { get; set; }

    /// <summary>Bileşen türü — toolbar butonu belirler (Maden/Hurda/Vadeli/Mücevher/Taş → CatalogCommodity;
    /// Hizmet → Service; Manuel → ManualCost).</summary>
    public RecipeComponentType ComponentType { get; set; } = RecipeComponentType.CatalogCommodity;

    /// <summary>Katalog emtia ailesi (Metal/Scrap/Future/Jewelry/Stone) — yalnız CatalogCommodity'de dolu.</summary>
    public ProcessType? CommodityProcessType { get; set; }

    /// <summary>Seçili katalog kaydı (snapshot ref) ya da hizmet referansı. Manuelde boş.</summary>
    public Guid? CommodityId { get; set; }

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
}

/// <summary>Persistsiz varyant üretim isteği (önizleme): nitelik grafı + ad türetmesi için ürün adı.
/// DB'ye YAZMAZ — kartezyen hesaplanır, varyant graf satırları döner (kalıcılaşma Product save'inde).</summary>
public class ProductVariantGenerateRequestDto
{
    /// <summary>Varyant AD türetmesi için ürün adı ("Ürün Kırmızı M") — synchronizer paritesi. Boşsa yalnız değer adları.</summary>
    public string? ProductName { get; set; }

    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
}

/// <summary>
/// Product grafının NİTELİK düğümü — varyant ekseni (ör. "Renk", "Beden"), değerleriyle birlikte.
/// Durum = <see cref="Id"/> + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil (değerleriyle), aksi → güncelle.
/// Ürün başına en fazla <see cref="ProductAttributeConsts.MaxAttributesPerProduct"/> (AppService zorlar).
/// </summary>
public class ProductAttributeGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(ProductAttributeConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    /// <summary>Niteliğin değerleri (ör. Renk → Kırmızı/Mavi) — kendi in-memory drill'iyle yönetilir.</summary>
    public List<ProductAttributeValueGraphDto> Values { get; set; } = new();
}

/// <summary>Nitelik DEĞERİ düğümü (ör. "Kırmızı") — attribute grafının çocuğu; aynı Id+IsDeleted diff'i.</summary>
public class ProductAttributeValueGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(ProductAttributeConsts.ValueMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
