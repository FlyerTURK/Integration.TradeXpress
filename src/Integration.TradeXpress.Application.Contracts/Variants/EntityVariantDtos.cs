using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.TradeXpress.Attachments;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik varyant grafının NİTELİK düğümü — varyant ekseni (ör. "Renk", "Beden"), değerleriyle. Durum =
/// <see cref="Id"/> + <see cref="IsDeleted"/>. Sahip entity başına en fazla <see cref="EntityVariantConsts.MaxAttributesPerEntity"/>.
/// </summary>
public class EntityAttributeGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(EntityVariantConsts.AttributeNameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public List<EntityAttributeValueGraphDto> Values { get; set; } = new();
}

/// <summary>Nitelik DEĞERİ düğümü (ör. "Kırmızı", "XL") — min 1 (perakende "M"/"S") + case-korur.</summary>
public class EntityAttributeValueGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(EntityVariantConsts.AttributeValueMaxLength, MinimumLength = 1)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}

/// <summary>
/// Agnostik varyant düğümü — nitelik-değer kombinasyonundan doğar. Kod/ad OTOMATİK (senkron üretir); drill'de yalnız
/// çekirdek alanlar (Barkod/Stok/Açıklama/Aktif) düzenlenir (varyant elle eklenmez/silinmez). Entity-özel ZENGİN
/// alanlar (ör. Product SalePrice) UZANTI DTO'sunda taşınır (bu çekirdek onları bilmez). <see cref="IsMain"/> DISPLAY-ONLY.
/// </summary>
public class EntityVariantGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    /// <summary>Ana (main) varyant mı — DISPLAY-ONLY (manager yönetir; drill'de düzenlenmez).</summary>
    public bool IsMain { get; set; }

    [Required]
    [StringLength(EntityVariantConsts.VariantCodeMaxLength, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(EntityVariantConsts.VariantNameMaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(EntityVariantConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Barkod (varyant-başı; EAN/UPC) — opsiyonel.</summary>
    [StringLength(EntityVariantConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }

    /// <summary>GTIN — opsiyonel per-SKU ticari kimliği.</summary>
    [StringLength(EntityVariantConsts.TradeIdentifierMaxLength)]
    public string? Gtin { get; set; }

    /// <summary>MPN (Manufacturer Part Number) — opsiyonel.</summary>
    [StringLength(EntityVariantConsts.TradeIdentifierMaxLength)]
    public string? Mpn { get; set; }

    /// <summary>OEM kodu — opsiyonel.</summary>
    [StringLength(EntityVariantConsts.TradeIdentifierMaxLength)]
    public string? Oem { get; set; }

    /// <summary>Stok miktarı — varsayılan 0; negatif geçersiz (sunucu zorlar).</summary>
    public int StockQuantity { get; set; }

    /// <summary>Varyantın nitelik-değer KOMBİNASYON özeti (ör. "Kırmızı / 42") — SALT-OKUNUR görüntü (save yoksayar).</summary>
    public string AttributeSummary { get; set; } = string.Empty;

    /// <summary>Kombinasyonun İSTEMCİ-taraflı kimliği — değer ClientKey'lerinin sıralı "|" join'i. GenerateVariants
    /// doldurur; kayıtta Id'siz üretilmiş satırın özelleştirmelerini senkron sonrası DB varyantına EŞLEMEK içindir.</summary>
    public string CombinationKey { get; set; } = string.Empty;

    /// <summary>Varyant-özel MEDYA link'leri (merkezi kütüphaneye referans — görsel+video birlikte; yeni DAM). Sahip
    /// AppService varyant DB Id'si çözülünce EntityMediaAppService ReplaceFor/GetFor ("{Entity}Variant" bağlamı) ile
    /// bağlar/yükler. Panel <c>ShowImages</c> ile medya panelini + grid poster önizlemesini açar.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();

    /// <summary>Varyant-özel dokümanlar (agnostik EntityDocument; in-memory graf). Sahip AppService varyant DB Id'si
    /// çözülünce ReplaceForAsync ile bağlar/yükler (medya ile AYNI varyant bağlamı/desen).</summary>
    public List<EntityDocumentEditDto> Documents { get; set; } = new();

    /// <summary>Varyant-özel notlar (agnostik EntityNote; in-memory graf). Sahip AppService ReplaceForAsync ile bağlar/yükler.</summary>
    public List<EntityNoteEditDto> Notes { get; set; } = new();
}

/// <summary>Persistsiz varyant üretim isteği (önizleme): nitelik grafı + ad türetmesi için sahip adı. DB'ye YAZMAZ.</summary>
public class EntityVariantGenerateRequestDto
{
    /// <summary>Varyant AD türetmesi için sahip entity adı (ör. Good.Name). Boşsa yalnız değer adları.</summary>
    public string? OwnerName { get; set; }

    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
}

/// <summary>Bir sahip entity'nin varyant grafı okuma sonucu (GetAsync projeksiyonu) — nitelikler + varyantlar.</summary>
public class EntityVariantGraphResult
{
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<EntityVariantGraphDto> Variants { get; set; } = new();
}
