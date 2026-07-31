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

    /// <summary>
    /// ÇEKİRDEK ürün kategorisi (<c>ProductCategory</c>) — id-only referans, navigation YOK; <c>null</c> = henüz
    /// sınıflandırılmamış (mevcut ürünler bozulmasın diye ZORUNLU DEĞİL).
    ///
    /// <para><b>Neden var:</b> ürün bir kez çekirdek kategoriye bağlanınca (a) her satış kanalında kategori ayrı
    /// ayrı seçilmez — kanal kategorisi eşleştirmeden çözülür, (b) kanal nitelikleri elle doldurulmaz, kategori
    /// nitelikleri ön-doldurur, (c) kanalın kategori komisyonu ürün seviyesinde bilinir ve reçeteye GrossUp
    /// maliyet olarak girer (kanal ürünü hiç oluşturulmamış olsa bile fiyat hesaplanabilir).</para>
    ///
    /// <para><b>Karıştırma:</b> <c>Good.Category</c>/<c>Jewelry.Category</c>/<c>Stone.Category</c> alanları
    /// SpecialCode tutan STRING gruplama alanlarıdır; bu taksonomiyle ilgileri yoktur.</para>
    /// </summary>
    public virtual Guid? ProductCategoryId { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    // ── Ürün görselleri 2026-07-31'de MERKEZİ DAM'a taşındı (K2 emekliliği) ──
    // Eski owned "Images" JSON koleksiyonu kaldırıldı; görsel + video artık Media + EntityMediaLink'te
    // ("Product" bağlamı — kayıt geneli; "ProductVariant" bağlamı — varyanta özel). Push, önizleme ve
    // sipariş snapshot'ı tek kaynaktan (IEntityMediaAppService) okur.

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

    /// <summary>
    /// Ürünün MENŞEİ ÜLKESİ (id-only, opsiyonel) — yeni üründe şirketin ülkesiyle dolar.
    ///
    /// <para><b>Neden bayrak değil ülke (2026-07-28 Hakan):</b> önceden <c>Domestic</c> adında bir true/false
    /// vardı ve "yerli mi" sorusunun cevabını kullanıcının elle işaretlemesi gerekiyordu. Menşei ülke gerçek
    /// veridir; N11'in beklediği <c>domestic</c> bayrağı ondan TÜRETİLİR (menşei == şirketin ülkesi). Böylece
    /// bilgi bir kez ve doğru yerde girilir, bayrak da kendiliğinden tutarlı kalır.</para>
    ///
    /// <para><c>null</c> = belirtilmemiş; bu durumda türetme yapılamaz ve kanal varsayılanı devreye girer.</para>
    /// </summary>
    public virtual Guid? OriginCountryId { get; protected set; }

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

    // KARGO ŞABLONU BURADAN SÖKÜLDÜ (2026-07-26 Hakan kararı): şablon ürünün değil KANALIN özelliğidir —
    // aynı ürün her pazaryerinde farklı şablonla gider. Bağ artık yalnız kanal katmanında yaşıyor
    // (SalesChannelTrN11Product.ShipmentTemplateId → N11ShipmentTemplate → çekirdek ShipmentTemplate).
    // Eski alanlar: ShipmentTemplateId (FK) + ShipmentTemplateName (K8 legacy snapshot'ı) — ikisi de kalktı.

    /// <summary>
    /// Ürünün REÇETE ŞABLONU ("orta reçete") — id-only, opsiyonel. Şablonun ARA MASRAF satırları (paketleme,
    /// kargo, sigorta) ürünün reçetesine buradan iner.
    ///
    /// <para><b>Neden ürüne kaydediliyor (2026-07-28 Hakan):</b> muadil motoru stok değişince kombinasyonları
    /// YENİDEN üretiyor ve her kombinasyonun reçetesini sıfırdan kuruyor. Şablon yalnız formun ömrü boyunca
    /// yaşasaydı, yeniden üretilen varyantlara ara masraf satırlarının hangi tanımdan geleceği bilinemezdi —
    /// paketleme/kargo/sigorta sessizce düşer ve fiyat eksik hesaplanırdı.</para>
    ///
    /// <para>Bağ id-only'dir: şablon AYRI aggregate'tir ve sonradan değişebilir. Uygulanmış reçete satırları
    /// ürünün KENDİ malıdır — şablondaki sonraki değişiklik geçmiş satırları geriye dönük EZMEZ.</para>
    /// </summary>
    public virtual Guid? RecipeTemplateId { get; protected set; }

    /// <summary>
    /// Ürünün PAKET DESİSİ — kargo tarifesinin girdisi (<c>PackageDesiResolver</c>). <c>null</c> = varyantın ya da
    /// kanalın varsayılanı kullanılır.
    ///
    /// <para>Çözüm sırası DARDAN GENİŞE: varyantın kendi desisi → ürünün desisi → kanal varsayılanı. Yani burası
    /// ürünün NORMAL paketi; varyant alanı yalnız istisna içindir.</para>
    ///
    /// <para>Doğruluğu KULLANICININ sorumluluğundadır (2026-07-28 Hakan): pazaryerleri desiyi kendi tahminleriyle
    /// kargo firmasına anlaştığından, buradaki değer fiyatlama içindir — gerçek kargo bedeli sipariş sürecinde
    /// netleşir.</para>
    /// </summary>
    public virtual int? PackageDesi { get; protected set; }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel).</summary>
    public virtual int? MaxPurchaseQuantity { get; protected set; }

    /// <summary>Satıcı notu — kısa düz metin (opsiyonel). Kanal-ürünü boşsa devralır.</summary>
    public virtual string? SellerNote { get; protected set; }

    /// <summary>Varsayılan para birimi (opsiyonel; id-only, nav YOK). Kanal-ürünü boşsa devralır.</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Ürün özelleştirme alanları — müşterinin sipariş anında dolduracağı ADLANDIRILMIŞ alanlar
    /// (owned → JSON; key=alan etiketi zorunlu, value opsiyonel varsayılan/örnek). Kanal-ürünü boşsa devralır.
    ///
    /// <para>Pazaryeri karşılıkları: N11 <c>SpecialInfo</c> (SOAP, çalışıyor) · Etsy <c>personalization
    /// questions</c> (2026-05'te çoklu adlandırılmış soru modeline geçti; her satır bir soru). Etsy'nin ESKİ
    /// tek-kutulu kişiselleştirme bloğu 2026-04-09'da kapandığı için ürün seviyesindeki o alanlar da
    /// 2026-07-28'de kaldırıldı — kişiselleştirmenin tek taşıyıcısı artık BU listedir.</para></summary>
    public virtual List<ProductSpecialInfo> SpecialInfo { get; protected set; } = new();

    /// <summary>Ürüne atanan eklentiler (owned → JSON; katalogdan seçim + satır override). Efektif değer zinciri
    /// TANIMLI (K11): <c>ChannelInheritance.ResolveAddOns</c> (Application) — satır-override ?? katalog; kanal-ürün
    /// entity'lerinde add-on override alanı YOK → zincir bugün tek-kaynaklı (ürün). Etsy push'unda varyant olarak
    /// yansıtma (projeksiyon) Faz-2 push işi.</summary>
    public virtual List<ProductAddOn> AddOns { get; protected set; } = new();

    // ── Kişiselleştirme alanları 2026-07-28'de KALDIRILDI ──
    // IsPersonalizable / PersonalizationInstructions / PersonalizationIsRequired / PersonalizationCharCountMax
    // Etsy'nin tek-kutulu modelinin ürün-genel karşılığıydı; o model Etsy'de 2026-04-09'da kapandı (gönderen
    // istek hata döner) ve yerine ÇOKLU ADLANDIRILMIŞ SORU geldi. N11'de zaten karşılığı yoktu. Kişiselleştirmenin
    // tek taşıyıcısı artık SpecialInfo'dur (yukarıda) — her satırı bir Etsy sorusuna 1:1 gider.
    // K10 devralma zinciri (ChannelInheritance.ResolvePersonalization) de bu nedenle söküldü.

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
        VariantMode = ProductVariantMode.MultiVariant;
        Condition = ProductCondition.New;
        PreparingDay = 1;
        WhoMade = EtsyWhoMade.IDid;
        MadePeriod = ProductMadePeriod.MadeToOrder;
        IsSupply = false;
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

    /// <summary>Çekirdek kategoriyi atar. Boş Guid "seçilmedi" demektir → <c>null</c> (combo'nun boş değeri
    /// var olmayan bir kategoriye asılı öksüz bağ üretmesin). Kategorinin AYNI ŞİRKETE ait ve var olduğu
    /// AppService'te doğrulanır — entity katalog kaydını göremez.</summary>
    /// <summary>Reçete şablonu bağını atar. Boş Guid null'a indirgenir (istemci "seçim yok"u böyle gönderebilir).</summary>
    public virtual void SetRecipeTemplate(Guid? recipeTemplateId)
    {
        RecipeTemplateId = recipeTemplateId is { } value && value != Guid.Empty ? value : null;
    }

    /// <summary>Paket desisini atar. Negatif değer ANLAMSIZ olduğundan null'a indirgenir — 0 geçerlidir
    /// (desisiz/ağırlıksız kalem), yalnız eksi taşıma hacmi diye bir şey yoktur.</summary>
    public virtual void SetPackageDesi(int? packageDesi)
    {
        PackageDesi = packageDesi is { } value && value >= 0 ? value : null;
    }

    public virtual void SetProductCategory(Guid? productCategoryId)
    {
        ProductCategoryId = productCategoryId is { } value && value != Guid.Empty ? value : null;
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

    public virtual void SetOriginCountry(Guid? originCountryId)
    {
        OriginCountryId = originCountryId is { } value && value != Guid.Empty ? value : null;
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

    public override string ToString()
    {
        return Code;
    }
}
