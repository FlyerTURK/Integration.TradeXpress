using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Satılabilir ürün — <b>kanonik, polimorfik emtia</b> (Maden/Hurda/Hizmet… <b>Nakit hariç</b>). Emtia türü
/// üründe SABİT DEĞİL; ileride BOM bileşenlerinden türer (Adım 2). <b>Company-owned</b> güvenlik sınırı
/// (<see cref="ICompanyOwned"/>, non-nullable <see cref="CompanyId"/>) + per-tenant. Ürün bir VİTRİN +
/// gruplamadır; satılabilir asıl bilgi (fiyat/reçete/görsel) varyantlarda yaşar (bkz. <c>ProductVariant</c>).
/// Marketplace'e listelenince N11 <c>product ↔ stockItem</c> yapısına eşlenir (Product ↔ Variant).
///
/// <para>Ana varyant kavramı Company→HQ Branch / Branch→default Vault değişmezini devralır: en-az-1 varyant,
/// tekil <c>IsMain</c> (bkz. <c>ProductVariant.IsMain</c>, invariant <c>ProductVariantManager</c>'da).</para>
///
/// <para>NOT (Adım 1 — minimal): Reçete/fiyat/stok/görsel + kanal-listeleme SONRAKİ adımlarda. Product↔Variant
/// alan bölüşümü Adım 2'de netleşecek (şu an kanonik kimlik alanları).</para>
/// </summary>
public class Product : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Kapsam DAİMA çalışılan şirket (sunucu zorlar).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Ürün görselleri (owned → JSON) — dış URL ya da yüklenmiş dosya (blob). Sıra DisplayOrder ile
    /// (küçük önce; ilk = ana görsel). Marketplace push'unda URL-kaynaklılar doğrudan gider.</summary>
    public virtual List<ProductImage> Images { get; protected set; } = new();

    /// <summary>Marketplace listeleme indirimi tipi (ürün-seviyesi; tüm varyant + kanallar). None = indirim yok.</summary>
    public virtual ProductDiscountType DiscountType { get; protected set; }

    /// <summary>İndirim değeri — Amount'ta tutar, Percentage'ta yüzde (0–100). None ise null.</summary>
    public virtual decimal? DiscountValue { get; protected set; }

    /// <summary>İndirim başlangıcı — İŞ TARİHİ (date-only; timezone kaydırmasına girmez). None ise null.</summary>
    public virtual DateTime? DiscountStartDate { get; protected set; }

    /// <summary>İndirim bitişi — İŞ TARİHİ (date-only). None ise null.</summary>
    public virtual DateTime? DiscountEndDate { get; protected set; }

    /// <summary>Üretim tarihi — İŞ TARİHİ (date-only; N11 productionDate). Opsiyonel.</summary>
    public virtual DateTime? ProductionDate { get; protected set; }

    /// <summary>Son kullanma tarihi — İŞ TARİHİ (date-only; N11 expirationDate). Opsiyonel.</summary>
    public virtual DateTime? ExpirationDate { get; protected set; }

    // ── Pazaryeri-genel varsayılanlar (kanal-ürünü devralır + override eder; N11 ürün-seviyesi alanların karşılığı) ──

    /// <summary>Yerli üretim mi (N11 domestic). Varsayılan true.</summary>
    public virtual bool Domestic { get; protected set; }

    /// <summary>Ürün durumu (pazaryeri-genel; her kanala kendi karşılığına eşlenir). Varsayılan New.</summary>
    public virtual ProductCondition Condition { get; protected set; }

    /// <summary>Kargoya verilme süresi (gün) — en az 1. Varsayılan 1.</summary>
    public virtual int PreparingDay { get; protected set; }

    /// <summary>Varsayılan kargo şablonu adı (opsiyonel; pazaryeri kanal-ürünü override eder).</summary>
    public virtual string? ShipmentTemplateName { get; protected set; }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel).</summary>
    public virtual int? MaxPurchaseQuantity { get; protected set; }

    /// <summary>Satıcı notu — kısa düz metin (opsiyonel). Kanal-ürünü boşsa devralır.</summary>
    public virtual string? SellerNote { get; protected set; }

    /// <summary>Varsayılan para birimi (opsiyonel; id-only, nav YOK). Kanal-ürünü boşsa devralır.</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Ürün özelleştirme alanları (owned → JSON; key=müşteri giriş etiketi zorunlu, value opsiyonel).
    /// Kanal-ürünü boşsa devralır.</summary>
    public virtual List<ProductSpecialInfo> SpecialInfo { get; protected set; } = new();

    protected Product() { }

    public Product(
        Guid companyId,
        string code,
        string name)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        IsActive = true;
        Domestic = true;
        Condition = ProductCondition.New;
        PreparingDay = 1;
    }

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04). Normalize + min/max StringFieldGuard'da; benzersizlik AppService'te.
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
    }

    // NOT (Adım 1 varsayımı): NormalizeName TitleCase yapar; marketplace başlıkları casing korumalı olabilir
    // (ör. "iPhone 15") → Adım 5 (kanal-listeleme) öncesi gözden geçirilecek. Şimdilik konvansiyon deseni.
    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ProductConsts.NameMaxLength);
    }

    /// <summary>Ad ataması, TitleCase normalizasyonu SEÇMELİ — <c>normalizeTitle=false</c> pazaryeri IMPORT yolu
    /// içindir: Trendyol başlığı satıcının yazdığı casing'le korunur ("iPhone 15" → "İphone 15" olmaz), yalnız
    /// trim + zorunlu/min/max doğrulanır. EN AZ İSTİLACI çözüm bilinçli tercih: mevcut SetName davranışı (tüm UI/
    /// seed yolları) DEĞİŞMEDEN kalır; import tek çağrı yerinde bu overload'u kullanır.</summary>
    public virtual void SetName(string name, bool normalizeTitle)
    {
        if (normalizeTitle)
        {
            SetName(name);
            return;
        }

        Name = StringFieldGuard.EnsureRequiredText(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ProductConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ProductConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Üretim + son kullanma tarihleri (iş tarihi, date-only). İkisi de doluysa üretim ≤ son kullanma.</summary>
    public virtual void SetShelfLife(DateTime? productionDate, DateTime? expirationDate)
    {
        if (productionDate is { } p && expirationDate is { } e && p.Date > e.Date)
        {
            throw new BusinessException("TradeXpress:Product:ShelfLifeInvalid");
        }

        ProductionDate = productionDate?.Date;
        ExpirationDate = expirationDate?.Date;
    }

    // ── Pazaryeri-genel varsayılan setterları (fail-fast; N11 kanal-ürünü setterlarıyla hizalı) ──

    public virtual void SetDomestic(bool domestic)
    {
        Domestic = domestic;
    }

    public virtual void SetCondition(ProductCondition condition)
    {
        Condition = condition;
    }

    /// <summary>Kargoya verilme süresi (gün) — en az 1 (fail-fast).</summary>
    public virtual void SetPreparingDay(int preparingDay)
    {
        if (preparingDay < 1)
        {
            throw new BusinessException("TradeXpress:Product:PreparingDayInvalid");
        }

        PreparingDay = preparingDay;
    }

    /// <summary>Varsayılan kargo şablonu adı (opsiyonel; boş değilse trim + max).</summary>
    public virtual void SetShipmentTemplate(string? shipmentTemplateName)
    {
        ShipmentTemplateName = StringFieldGuard.EnsureOptionalText(
            shipmentTemplateName, nameof(ShipmentTemplateName), 1, ProductConsts.ShipmentTemplateNameMaxLength);
    }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel) — en az 1 (fail-fast).</summary>
    public virtual void SetMaxPurchaseQuantity(int? maxPurchaseQuantity)
    {
        if (maxPurchaseQuantity is { } value && value < 1)
        {
            throw new BusinessException("TradeXpress:Product:MaxPurchaseQuantityInvalid");
        }

        MaxPurchaseQuantity = maxPurchaseQuantity;
    }

    /// <summary>Satıcı notu (opsiyonel; boş değilse trim + max).</summary>
    public virtual void SetSellerNote(string? sellerNote)
    {
        SellerNote = StringFieldGuard.EnsureOptionalText(
            sellerNote, nameof(SellerNote), 1, ProductConsts.SellerNoteMaxLength);
    }

    /// <summary>Varsayılan para birimi (opsiyonel; sadece atama, boş=null).</summary>
    public virtual void SetCurrencyUnit(Guid? currencyUnitId)
    {
        CurrencyUnitId = currencyUnitId == Guid.Empty ? null : currencyUnitId;
    }

    /// <summary>Ürün özelleştirme alanları — yalnız KEY zorunlu (boş key'li satır elenir), value opsiyonel (trim).
    /// N11 SetSpecialInfo deseninin ürün-genel karşılığı.</summary>
    public virtual void SetSpecialInfo(IEnumerable<ProductSpecialInfo>? specialInfo)
    {
        SpecialInfo = (specialInfo ?? Enumerable.Empty<ProductSpecialInfo>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Key))
            .Select(s => new ProductSpecialInfo(s.Key.Trim(), (s.Value ?? string.Empty).Trim()))
            .ToList();
    }

    /// <summary>Marketplace indirimi (ürün-seviyesi). None → değer/tarihler temizlenir. Amount &gt; 0; Percentage
    /// 0–100 arası. Tarihler ya İKİSİ de dolu ya da İKİSİ de boş; başlangıç ≤ bitiş (fail-fast).</summary>
    public virtual void SetDiscount(ProductDiscountType type, decimal? value, DateTime? startDate, DateTime? endDate)
    {
        if (type == ProductDiscountType.None)
        {
            DiscountType = ProductDiscountType.None;
            DiscountValue = null;
            DiscountStartDate = null;
            DiscountEndDate = null;
            return;
        }

        if (value is not { } v || v <= 0)
        {
            throw new BusinessException("TradeXpress:Product:DiscountValueInvalid");
        }

        if (type == ProductDiscountType.Percentage && v > 100)
        {
            throw new BusinessException("TradeXpress:Product:DiscountPercentageInvalid");
        }

        if ((startDate is null) != (endDate is null))
        {
            throw new BusinessException("TradeXpress:Product:DiscountDatesInvalid");
        }

        if (startDate is { } s && endDate is { } e && s.Date > e.Date)
        {
            throw new BusinessException("TradeXpress:Product:DiscountDatesInvalid");
        }

        DiscountType = type;
        DiscountValue = v;
        DiscountStartDate = startDate?.Date;
        DiscountEndDate = endDate?.Date;
    }

    /// <summary>Görselleri ayarlar — kaynağı boş olanlar (URL'siz Url tipi / blob'suz Upload tipi) elenir,
    /// DisplayOrder'a göre sıralanır, en fazla <see cref="ProductConsts.MaxImageCount"/>. Aynı ürüne aynı URL
    /// ya da aynı dosya adı İKİ KEZ giremez (dostane hata). Tekil-default: birden fazla işaretliyse ilki kalır,
    /// hiç yoksa ilk görsel default olur.</summary>
    public virtual void SetImages(IEnumerable<ProductImage>? images)
    {
        var normalized = (images ?? Enumerable.Empty<ProductImage>())
            .Where(i => i.SourceType is ProductImageSourceType.Url or ProductImageSourceType.Upload)   // bilinmeyen tip ele
            .Where(i => i.SourceType == ProductImageSourceType.Url
                ? !string.IsNullOrWhiteSpace(i.Url)
                : !string.IsNullOrWhiteSpace(i.BlobName))
            .Select(i => new ProductImage(
                i.SourceType,
                string.IsNullOrWhiteSpace(i.Url) ? null : i.Url!.Trim(),
                string.IsNullOrWhiteSpace(i.BlobName) ? null : i.BlobName!.Trim(),
                string.IsNullOrWhiteSpace(i.FileName) ? null : i.FileName!.Trim(),
                i.DisplayOrder,
                i.IsDefault))
            .OrderBy(i => i.DisplayOrder)
            .Take(ProductConsts.MaxImageCount)
            .ToList();

        EnsureImagesUnique(normalized);
        EnsureSingleDefault(normalized);
        Images = normalized;
    }

    /// <summary>Aynı ürüne aynı URL (case-duyarsız) ya da aynı dosya adı iki kez girilemez — dostane hata
    /// (2026-07-07 kullanıcı kararı). UI drill'i de kaydetmeden önce aynı kuralı uygular; burası savunma.</summary>
    private static void EnsureImagesUnique(List<ProductImage> images)
    {
        var duplicateUrl = images
            .Where(i => i.Url is not null)
            .GroupBy(i => i.Url!, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);
        var duplicateFile = images
            .Where(i => i.FileName is not null)
            .GroupBy(i => i.FileName!, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);
        if (duplicateUrl || duplicateFile)
        {
            throw new BusinessException("TradeXpress:Product:ImageDuplicate");
        }
    }

    /// <summary>Tekil varsayılan görsel: birden fazla işaretliyse İLKİ kalır; hiç yoksa ilk görsel default olur.</summary>
    private static void EnsureSingleDefault(List<ProductImage> images)
    {
        if (images.Count == 0)
        {
            return;
        }

        var firstDefaultSeen = false;
        foreach (var image in images)
        {
            if (image.IsDefault)
            {
                if (firstDefaultSeen)
                {
                    image.IsDefault = false;
                }

                firstDefaultSeen = true;
            }
        }

        if (!firstDefaultSeen)
        {
            images[0].IsDefault = true;
        }
    }

    public override string ToString()
    {
        return Code;
    }
}
