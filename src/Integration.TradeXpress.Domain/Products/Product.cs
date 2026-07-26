using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Substitutions;

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

    // ── Etsy zorunlu menşe alanları (ürün-özü karar 2026-07-18; Etsy who_made/when_made/is_supply). N11/Trendyol
    // bunları tüketmez; Etsy kanal-ürünü bu ürün-seviyesi değerleri push'ta devralır. ──

    /// <summary>Ürünü kim yaptı (Etsy who_made). Varsayılan IDid.</summary>
    public virtual EtsyWhoMade WhoMade { get; protected set; }

    /// <summary>Ürün ne zaman yapıldı / dönem kovası (Etsy when_made'in kaynağı; 19-kovalı, kronolojik).
    /// Varsayılan MadeToOrder.</summary>
    public virtual ProductMadePeriod MadePeriod { get; protected set; }

    /// <summary>Bu bir üretim malzemesi/sarf mı (Etsy is_supply). Varsayılan false.</summary>
    public virtual bool IsSupply { get; protected set; }

    /// <summary>Kargoya verilme süresi (gün) — en az 1. Varsayılan 1.</summary>
    public virtual int PreparingDay { get; protected set; }

    /// <summary>Varsayılan kargo şablonu adı (opsiyonel; pazaryeri kanal-ürünü override eder). LEGACY snapshot —
    /// K8-Faz1: OKUMA tek kaynağı <see cref="ShipmentTemplateId"/> (ad çekirdek şablondan çözülür; bu string yalnız
    /// FK boşken fallback); FK doluysa yazma yolunda ad FK'den senkron dolar (Carrier id+ad deseni). Kolon Faz-4'te
    /// kaldırılacak (K8).</summary>
    public virtual string? ShipmentTemplateName { get; protected set; }

    /// <summary>Birleşik ERP kargo şablonu referansı (<c>ShipmentTemplate.Id</c>; id-only, nav YOK). Opsiyonel.
    /// Referans bütünlüğü ShipmentTemplate silme-guard'ıyla korunur (sert FK/cascade DEĞİL).</summary>
    public virtual Guid? ShipmentTemplateId { get; protected set; }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel).</summary>
    public virtual int? MaxPurchaseQuantity { get; protected set; }

    /// <summary>Satıcı notu — kısa düz metin (opsiyonel). Kanal-ürünü boşsa devralır.</summary>
    public virtual string? SellerNote { get; protected set; }

    /// <summary>Varsayılan para birimi (opsiyonel; id-only, nav YOK). Kanal-ürünü boşsa devralır.</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Ürün özelleştirme alanları (owned → JSON; key=müşteri giriş etiketi zorunlu, value opsiyonel).
    /// Kanal-ürünü boşsa devralır.</summary>
    public virtual List<ProductSpecialInfo> SpecialInfo { get; protected set; } = new();

    /// <summary>Ürüne atanan eklentiler (owned → JSON; katalogdan seçim + satır override). Efektif değer zinciri
    /// TANIMLI (K11): <c>ChannelInheritance.ResolveAddOns</c> (Application) — satır-override ?? katalog; kanal-ürün
    /// entity'lerinde add-on override alanı YOK → zincir bugün tek-kaynaklı (ürün). Etsy push'unda varyant olarak
    /// yansıtma (projeksiyon) Faz-2 push işi.</summary>
    public virtual List<ProductAddOn> AddOns { get; protected set; } = new();

    // ── Kişiselleştirme (pazaryeri-genel; Etsy who_made/is_supply deseni). Devralma zinciri TANIMLI (K10):
    // ChannelInheritance.ResolvePersonalization (Application) — kanal bloğu doluysa (IsPersonalizable) kanal,
    // değilse ürün; Etsy push (Faz-2) bu tek çağrıyı kullanır. ──

    /// <summary>Ürün kişiselleştirilebilir mi (Etsy is_personalizable). Varsayılan false.</summary>
    public virtual bool IsPersonalizable { get; protected set; }

    /// <summary>Kişiselleştirme talimatı (Etsy personalization_instructions; personalizable ise anlamlı). Opsiyonel.</summary>
    public virtual string? PersonalizationInstructions { get; protected set; }

    /// <summary>Kişiselleştirme zorunlu mu (Etsy personalization_is_required). Yalnız kişiselleştirilebilir üründe
    /// anlamlı; zorlanmaz, olduğu gibi saklanır. Varsayılan false.</summary>
    public virtual bool PersonalizationIsRequired { get; protected set; }

    /// <summary>Müşteri girişinin maksimum karakter sayısı (Etsy personalization_char_count_max). Opsiyonel;
    /// dolu ise 1..<see cref="ProductConsts.PersonalizationCharCountMaxLimit"/> aralığında (fail-fast).</summary>
    public virtual int? PersonalizationCharCountMax { get; protected set; }

    // ── Varyant modu + Muadil (paket) konfigürasyonu (Dilim-3). İş gerekçesi: "Yeni-Eski Karışık Ziynet Sepeti"
    // ürünü grubun tüm varyantlarıyla; "Yeni Tarihli Ziynet Sepeti" ürünü override ağacında yalnız yeni
    // tarihlilerle kombinasyon kurar. Muadil alanları yalnız VariantMode=Substitution iken dolu (tutarlılık
    // SetSubstitutionConfig'te; mod dışında temizlenir). ──

    /// <summary>Varyant üretim tercihi — varsayılan <see cref="ProductVariantMode.MultiVariant"/> (statüko).</summary>
    public virtual ProductVariantMode VariantMode { get; protected set; }

    /// <summary>Muadil grubu referansı (id-only, nav YOK) — yalnız Substitution modunda dolu (zorunlu).</summary>
    public virtual Guid? SubstitutionGroupId { get; protected set; }

    /// <summary>Kombinasyon hedef miktarı (gram) — Substitution modunda zorunlu, &gt; 0 (fail-fast).</summary>
    public virtual decimal? SubstitutionTargetQuantity { get; protected set; }

    /// <summary>Tolerans türü override'ı — null = grubun tolerans politikası kullanılır (değerle ÇİFT dolar).</summary>
    public virtual ToleranceType? SubstitutionToleranceType { get; protected set; }

    /// <summary>Tolerans değeri override'ı — null = grup ayarı; dolu ise negatif olamaz (türle ÇİFT dolar).</summary>
    public virtual decimal? SubstitutionToleranceValue { get; protected set; }

    /// <summary>Ürün-düzeyi varyant OVERRIDE kümesi (EF primitive-collection → JSON kolonu; Dilim-1
    /// IncludedVariantIds deseni birebir). <b>BOŞ liste = gruptan devral</b> (resolver zinciri:
    /// override ?? IncludedVariantIds ?? ana varyant); dolu ise grup kalemlerinin kapsamını TAMAMEN ezer.</summary>
    public virtual List<Guid> SubstitutionOverrideVariantIds { get; protected set; } = new();

    /// <summary>Muadil kombinasyonlarının varyanta dönüşme biçimi (Tek/Çoklu) — yalnız Substitution modunda
    /// anlamlı; mod dışında <see cref="SetSubstitutionConfig"/> Single'a sıfırlar. Üretim otomatiktir
    /// (ürün kaydı + maden stok değişimi; ADR-PRODUCT-ORCHESTRATION).</summary>
    public virtual SubstitutionVariantMode SubstitutionVariantMode { get; protected set; }

    /// <summary>Kanal stok sayısının kaynağı — varsayılan <see cref="ProductStockPolicy.Fixed"/> (statüko:
    /// elle girilen stok; orkestratör dokunmaz). Muadil ürünler doğal olarak Calculated'dır
    /// (<see cref="SetSubstitutionConfig"/> zorlar — muadilde stok her zaman hesaptan gelir).</summary>
    public virtual ProductStockPolicy StockPolicy { get; protected set; }

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
        VariantMode = ProductVariantMode.MultiVariant;
        Condition = ProductCondition.New;
        PreparingDay = 1;
        WhoMade = EtsyWhoMade.IDid;
        MadePeriod = ProductMadePeriod.MadeToOrder;
        IsSupply = false;
        IsPersonalizable = false;
        PersonalizationIsRequired = false;
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

    /// <summary>Etsy who_made (ürünü kim yaptı; varsayılan IDid).</summary>
    public virtual void SetWhoMade(EtsyWhoMade whoMade)
    {
        WhoMade = whoMade;
    }

    /// <summary>Ürün dönem kovası (Etsy when_made'in kaynağı; varsayılan MadeToOrder).</summary>
    public virtual void SetMadePeriod(ProductMadePeriod madePeriod)
    {
        MadePeriod = madePeriod;
    }

    /// <summary>Etsy is_supply (üretim malzemesi/sarf mı; varsayılan false).</summary>
    public virtual void SetSupply(bool isSupply)
    {
        IsSupply = isSupply;
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

    /// <summary>Birleşik ERP kargo şablonu referansı (opsiyonel; id-only atama, boş=null).</summary>
    public virtual void SetShipmentTemplateId(Guid? shipmentTemplateId)
    {
        ShipmentTemplateId = shipmentTemplateId == Guid.Empty ? null : shipmentTemplateId;
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

    /// <summary>Ürüne atanan eklentiler — yalnız GEÇERLİ (AddOnId dolu) satırlar; DisplayOrder'a göre sıralanır,
    /// Note trim'lenir. Katalog referansı; boş AddOnId'li satır elenir.</summary>
    public virtual void SetAddOns(IEnumerable<ProductAddOn>? addOns)
    {
        AddOns = (addOns ?? Enumerable.Empty<ProductAddOn>())
            .Where(a => a.AddOnId != Guid.Empty)
            .OrderBy(a => a.DisplayOrder)
            .Select(a => new ProductAddOn(
                a.AddOnId,
                a.PriceOverride,
                a.CurrencyUnitOverrideId,
                a.IsRequired,
                a.DisplayOrder,
                string.IsNullOrWhiteSpace(a.Note) ? null : a.Note.Trim()))
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
    /// ya da aynı BLOB adı İKİ KEZ giremez (dostane hata). Tekil-default: birden fazla işaretliyse ilki kalır,
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
                i.IsDefault,
                i.VariantId,
                string.IsNullOrWhiteSpace(i.VariantCode) ? null : i.VariantCode!.Trim()))
            .OrderBy(i => i.DisplayOrder)
            .Take(ProductConsts.MaxImageCount)
            .ToList();

        EnsureImagesUnique(normalized);
        EnsureSingleDefault(normalized);
        Images = normalized;
    }

    /// <summary>Aynı ürüne aynı URL (case-duyarsız) ya da aynı BLOB adı iki kez girilemez — dostane hata.
    /// Dosya adı ARTIK dedupe anahtarı DEĞİL (2026-07-18 kullanıcı kararı: aynı dosya adı farklı varyant
    /// klasöründe meşru; blob adı path-önekli ve sunucu ilk-boş-sıra probe'uyla zaten TEKİL). UI drill'i de
    /// kaydetmeden önce aynı kuralı uygular; burası savunma.</summary>
    private static void EnsureImagesUnique(List<ProductImage> images)
    {
        var duplicateUrl = images
            .Where(i => i.Url is not null)
            .GroupBy(i => i.Url!, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);
        var duplicateBlob = images
            .Where(i => i.BlobName is not null)
            .GroupBy(i => i.BlobName!, StringComparer.Ordinal)
            .Any(g => g.Count() > 1);
        if (duplicateUrl || duplicateBlob)
        {
            throw new BusinessException("TradeXpress:Product:ImageDuplicate");
        }
    }

    /// <summary>Kişiselleştirme alanlarını ayarlar (pazaryeri-genel; kanal devralma zinciri
    /// <c>ChannelInheritance.ResolvePersonalization</c>'da — kanal-ürünü push'ta oradan devralır).
    /// Talimat boş değilse trim + max; karakter sınırı dolu ise 1..<see cref="ProductConsts.PersonalizationCharCountMaxLimit"/>
    /// (fail-fast). <paramref name="isRequired"/> yalnız kişiselleştirilebilir üründe anlamlı — zorlanmaz, olduğu gibi saklanır.</summary>
    public virtual void SetPersonalization(bool isPersonalizable, string? instructions, bool isRequired, int? charCountMax)
    {
        if (charCountMax is { } max && (max < 1 || max > ProductConsts.PersonalizationCharCountMaxLimit))
        {
            throw new BusinessException("TradeXpress:Product:PersonalizationCharCountMaxInvalid");
        }

        IsPersonalizable = isPersonalizable;
        PersonalizationInstructions = StringFieldGuard.EnsureOptionalText(
            instructions, nameof(PersonalizationInstructions), 1, ProductConsts.PersonalizationInstructionsMaxLength);
        PersonalizationIsRequired = isRequired;
        PersonalizationCharCountMax = charCountMax;
    }

    /// <summary>Varyant üretim tercihi — SetCondition deseni (basit atama). Muadil konfigürasyonunun mod
    /// tutarlılığı <see cref="SetSubstitutionConfig"/>'te (bu setter'dan SONRA çağrılır — Create/Update simetrik).</summary>
    public virtual void SetVariantMode(ProductVariantMode variantMode)
    {
        VariantMode = variantMode;
    }

    /// <summary>Muadil (paket) konfigürasyonu — TEK mutator (tutarlılık tek yerde):
    /// <list type="bullet">
    ///   <item>Mod ≠ Substitution → TÜM muadil alanları temizlenir (bayat grup/hedef/override taşınmaz).</item>
    ///   <item>Mod = Substitution → grup ZORUNLU + hedef &gt; 0 (fail-fast); tolerans türü/değeri ya İKİSİ de
    ///   dolu ya da İKİSİ de boş (boş = grubun tolerans politikası), değer negatif olamaz.</item>
    ///   <item>Override kümesi Dilim-1 <c>SetIncludedVariants</c> sözleşmesiyle normalize edilir
    ///   (boş-Guid ayıklanır, duplike düşer, kullanıcı sırası korunur; BOŞ = gruptan devral).</item>
    /// </list></summary>
    public virtual void SetSubstitutionConfig(
        Guid? substitutionGroupId,
        decimal? targetQuantity,
        ToleranceType? toleranceType,
        decimal? toleranceValue,
        IEnumerable<Guid>? overrideVariantIds,
        SubstitutionVariantMode substitutionVariantMode = SubstitutionVariantMode.Single)
    {
        if (VariantMode != ProductVariantMode.Substitution)
        {
            SubstitutionGroupId = null;
            SubstitutionTargetQuantity = null;
            SubstitutionToleranceType = null;
            SubstitutionToleranceValue = null;
            SubstitutionOverrideVariantIds = new List<Guid>();
            SubstitutionVariantMode = SubstitutionVariantMode.Single;
            return;
        }

        if (substitutionGroupId is not { } groupId || groupId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Product:SubstitutionGroupRequired");
        }

        if (targetQuantity is not { } target || target <= 0m)
        {
            throw new BusinessException("TradeXpress:Product:SubstitutionTargetQuantityInvalid");
        }

        if ((toleranceType is null) != (toleranceValue is null))
        {
            throw new BusinessException("TradeXpress:Product:SubstitutionToleranceInvalid");
        }

        if (toleranceValue is { } tolerance && tolerance < 0m)
        {
            throw new BusinessException("TradeXpress:Product:SubstitutionToleranceInvalid");
        }

        SubstitutionGroupId = groupId;
        SubstitutionTargetQuantity = target;
        SubstitutionToleranceType = toleranceType;
        SubstitutionToleranceValue = toleranceValue;
        SubstitutionOverrideVariantIds = (overrideVariantIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        SubstitutionVariantMode = substitutionVariantMode;

        // Muadilde stok DAİMA hesaptan gelir (kombinasyonlar o anki stoktan türer) → politika zorlanır.
        // Elle stok (Fixed) muadille çelişir: kullanıcının yazdığı sayı ile üretilebilir paket sayısı
        // ayrışınca oversell kapısı açılırdı.
        StockPolicy = ProductStockPolicy.Calculated;
    }

    /// <summary>Stok politikası — SetCondition deseni (basit atama). Muadil modda çağrı ETKİSİZDİR:
    /// <see cref="SetSubstitutionConfig"/> Calculated'ı zorlar (muadil stoğu hesaptan gelir, elle olamaz).</summary>
    public virtual void SetStockPolicy(ProductStockPolicy stockPolicy)
    {
        if (VariantMode == ProductVariantMode.Substitution)
        {
            StockPolicy = ProductStockPolicy.Calculated;
            return;
        }

        StockPolicy = stockPolicy;
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
