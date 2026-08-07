using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Goods;

public class GoodListRequestDto : ListRequestDto
{
    /// <summary>Çalışılan şirket — görünür kayıtlar host/holding-host(null) + bu şirkete-özel olanlar.</summary>
    public Guid? CompanyId { get; set; }
}

public class GoodListDto : CatalogListDtoBase, IPricedCommodityListDto
{
    public string? Brand { get; set; }
    public string? Category { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; }
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    public Guid? CompanyId { get; set; }

    /// <summary>Grid önizlemesi — ana varyantın varsayılan medyasının poster URL'i (sunucu doldurur).</summary>
    public string? ImagePreviewUrl { get; set; }
}

/// <summary>Bir mamülün TEDARİKÇİSİ (drill satırı) — hangi cari, hangi fiyatla, kaç günde. Display alanları
/// (kod/ad) AppService enrich eder.</summary>
public class GoodSupplierDto
{
    /// <summary>İstemci-tarafı satır anahtarı (DrillList @key) — kalıcı değil; yeni satırlar Id=Empty olduğundan
    /// grid anahtarı buna dayanır.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }
    /// <summary>Cari hesap — ZORUNLU (tedarikçi tek başına cari hesapla tanımlanabilir).</summary>
    public Guid AccountId { get; set; }
    /// <summary>Alt hesap — OPSİYONEL (boşsa cari hesap seviyesinde tedarikçi).</summary>
    public Guid? SubAccountId { get; set; }
    public decimal Price { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public bool TaxIncluded { get; set; }
    public int LeadDays { get; set; }

    // Enrich (salt-okuma görüntü) — drill grid.
    public string? AccountCode { get; set; }
    public string? SubAccountCode { get; set; }
    public string? CurrencyCode { get; set; }
}

public class GoodGetDto : CatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(GoodConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(GoodConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    // ── Kimlik + sınıflandırma (Barkod düz; diğerleri SpecialCode picker; Beden düz) ──
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Brand { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Model { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Kind { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Type { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Color { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Size { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? Category { get; set; }
    [StringLength(GoodConsts.AttributeMaxLength)] public string? GroupCode { get; set; }

    [StringLength(GoodConsts.StockUnitMaxLength)] public string? StockUnitCode { get; set; }

    // ── Vergi (% — katalog bilgisi) ──
    public decimal VatPurchaseRate { get; set; }
    public decimal VatSaleRate { get; set; }
    public decimal OtvRate { get; set; }
    public decimal WithholdingRate { get; set; }

    // ── Fiyat tipi bayrakları (adet/miktar) — mal doğası; fiyat DEĞERLERİ artık varyantta (GoodVariantDetail) ──
    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;

    [StringLength(GoodConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public Guid? CompanyId { get; set; }

    // ── Graf (in-memory; kayıtta AppService persist eder) ──
    public List<GoodSupplierDto> Suppliers { get; set; } = new();
    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<GoodVariantGraphDto> Variants { get; set; } = new();

    /// <summary>KAYIT-GENELİ medya (2026-08-06 Hakan kuralı: her medya tipi İKİ bağlamı da taşır).
    /// Varyanta özel görseller <c>GoodVariantGraphDto.Media</c>'da ayrı durur; bu liste markanın/ürünün
    /// genel görselleridir. İkisi ayrı depodur, biri diğerinden TÜRETİLMEZ — push zinciri varyant→kayıt
    /// fallback'iyle okur.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();
}

public class GoodCreateDto : CatalogCreateDtoBase
{
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(GoodConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(GoodConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Category { get; set; }
    public string? GroupCode { get; set; }
    public string? StockUnitCode { get; set; }

    public decimal VatPurchaseRate { get; set; }
    public decimal VatSaleRate { get; set; }
    public decimal OtvRate { get; set; }
    public decimal WithholdingRate { get; set; }

    // Fiyat DEĞERLERİ (alış/kâr/satış) + Min/Max artık varyantta (GoodVariantGraphDto). Burada yalnız fiyat-tipi bayrakları.
    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;

    [StringLength(GoodConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<GoodSupplierDto> Suppliers { get; set; } = new();
    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<GoodVariantGraphDto> Variants { get; set; } = new();

    /// <summary>KAYIT-GENELİ medya (2026-08-06 Hakan kuralı: her medya tipi İKİ bağlamı da taşır).
    /// Varyanta özel görseller <c>GoodVariantGraphDto.Media</c>'da ayrı durur; bu liste markanın/ürünün
    /// genel görselleridir. İkisi ayrı depodur, biri diğerinden TÜRETİLMEZ — push zinciri varyant→kayıt
    /// fallback'iyle okur.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();
}

public class GoodUpdateDto : CatalogUpdateDtoBase
{
    [Required]
    [StringLength(GoodConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(GoodConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Category { get; set; }
    public string? GroupCode { get; set; }
    public string? StockUnitCode { get; set; }

    public decimal VatPurchaseRate { get; set; }
    public decimal VatSaleRate { get; set; }
    public decimal OtvRate { get; set; }
    public decimal WithholdingRate { get; set; }

    // Fiyat DEĞERLERİ (alış/kâr/satış) + Min/Max artık varyantta (GoodVariantGraphDto). Burada yalnız fiyat-tipi bayrakları.
    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;

    [StringLength(GoodConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<GoodSupplierDto> Suppliers { get; set; } = new();
    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<GoodVariantGraphDto> Variants { get; set; } = new();

    /// <summary>KAYIT-GENELİ medya (2026-08-06 Hakan kuralı: her medya tipi İKİ bağlamı da taşır).
    /// Varyanta özel görseller <c>GoodVariantGraphDto.Media</c>'da ayrı durur; bu liste markanın/ürünün
    /// genel görselleridir. İkisi ayrı depodur, biri diğerinden TÜRETİLMEZ — push zinciri varyant→kayıt
    /// fallback'iyle okur.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();
}
