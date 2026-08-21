using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>Etsy listeleme (taksonomi varyasyon-DIŞI) attribute değeri (name/value) — owned, JSON kolonuna serialize
/// edilir. N11 <c>SalesChannelTrN11ProductCategoryAttribute</c> ikizi. SKU <see cref="SalesChannelEtsyProductSku.PropertySnapshot"/>
/// için de kullanılır (push edilen özellik çiftlerinin kaydı).</summary>
public class SalesChannelEtsyProductListingAttribute
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public SalesChannelEtsyProductListingAttribute()
    {
    }

    public SalesChannelEtsyProductListingAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }
}

/// <summary>Etsy etiketi (<c>tags</c>) — basit string owned tip (N11'de string-listesi owned deseni yok; owned tip kullanılır).</summary>
public class SalesChannelEtsyProductTag
{
    public string Value { get; set; } = string.Empty;

    public SalesChannelEtsyProductTag()
    {
    }

    public SalesChannelEtsyProductTag(string value)
    {
        Value = value;
    }
}

/// <summary>Etsy malzemesi (<c>materials</c>) — basit string owned tip (Tag ile aynı desen; JSON kolonu).</summary>
public class SalesChannelEtsyProductMaterial
{
    public string Value { get; set; } = string.Empty;

    public SalesChannelEtsyProductMaterial()
    {
    }

    public SalesChannelEtsyProductMaterial(string value)
    {
        Value = value;
    }
}

/// <summary>Etsy kişiselleştirme SORUSU (key=soru başlığı, value=varsayılan/örnek) — owned, JSON.
/// N11 <c>SalesChannelTrN11ProductSpecialInfo</c> ikizi; Etsy tarafında bir satır = bir
/// <c>personalization question</c> (<c>question_type=text_input</c>).
///
/// <para><see cref="IsRequired"/> ve <see cref="MaxAllowedCharacters"/> Etsy'de SORU BAŞINA belirlenir — eski
/// tek-kutulu modelde listeleme geneli olan bu iki ayar 2026-07-28'de buraya indi. N11 bu iki alanı yok sayar
/// (SOAP sözleşmesinde karşılığı yok), dolayısıyla varsayılanları zararsızdır.</para></summary>
public class SalesChannelEtsyProductSpecialInfo
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>Alıcı bu soruyu boş bırakabilir mi (<c>required</c>). Varsayılan false.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Cevabın karakter tavanı (<c>max_allowed_characters</c>); boşsa Etsy varsayılanı geçerli.</summary>
    public int? MaxAllowedCharacters { get; set; }

    public SalesChannelEtsyProductSpecialInfo()
    {
    }

    public SalesChannelEtsyProductSpecialInfo(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public SalesChannelEtsyProductSpecialInfo(string key, string value, bool isRequired, int? maxAllowedCharacters)
    {
        Key = key;
        Value = value;
        IsRequired = isRequired;
        MaxAllowedCharacters = maxAllowedCharacters;
    }
}

/// <summary>Etsy SKU kimlik satırı (varyant-başına; owned → JSON). <see cref="FrozenSku"/> İLK kuruluşta üretilir
/// ve DONDURULUR: ProductVariant.Code sonradan değişse ya da synchronizer varyantı silip yeniden üretse bile push
/// aynı uzak SKU'ya gider. <see cref="PropertySnapshot"/> = push edilen name/value çiftleri (sipariş→varyant
/// çözümünün ve yeniden-bağlama imzasının temeli). N11 <c>SalesChannelTrN11ProductSku</c> ikizi
/// (SellerStockCode→FrozenSku, N11SkuId→EtsyProductId, N11Version→EtsyOfferingVersion, AttributeSnapshot→PropertySnapshot).</summary>
public class SalesChannelEtsyProductSku
{
    /// <summary>Bağlı KOMBİNASYON kimliği — ERP-backed satırda <c>ProductVariant.Id</c>, Etsy-only satırda
    /// <c>SalesChannelEtsyProductStockItem.Id</c>. Kaynak yeniden üretilirse kod/imza üzerinden yeniden bağlanır.</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Etsy satıcı SKU stok kodu — DONDURULMUŞ ("{VaryantKodu}-{SequenceNo}", kuruluş anındaki kod).</summary>
    public string FrozenSku { get; set; } = string.Empty;

    /// <summary>Etsy'nin atadığı inventory product id'si (push yanıtından; SKU-düzeyi mutabakat anahtarı).</summary>
    public long? EtsyProductId { get; set; }

    /// <summary>Etsy offering versiyonu — fiyat/adet değişiminde (satış dahil) artar; drift sinyali.</summary>
    public long? EtsyOfferingVersion { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen adet (dirty-tracking temeli).</summary>
    public int? LastSentQuantity { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen offering fiyatı (mutlak liste fiyatı).</summary>
    public decimal? LastSentPrice { get; set; }

    /// <summary>Push edilen varyant seçenekleri (name/value) — sipariş eşleme + imza.</summary>
    public List<SalesChannelEtsyProductListingAttribute> PropertySnapshot { get; set; } = new();

    public SalesChannelEtsyProductSku()
    {
    }

    public SalesChannelEtsyProductSku(Guid productVariantId, string frozenSku)
    {
        ProductVariantId = productVariantId;
        FrozenSku = frozenSku;
    }
}

/// <summary>Push edilecek kombinasyon adayı — <see cref="SalesChannelEtsyProduct.ReconcileSkus"/> girdisi
/// (kimlik + kod + Etsy'ye gidecek property çiftleri). <see cref="VariantId"/> = kombinasyon kimliği: ERP-backed
/// satırda <c>ProductVariant.Id</c>, Etsy-only satırda <c>SalesChannelEtsyProductStockItem.Id</c>;
/// <see cref="VariantCode"/> Etsy-only satırda kombinasyon-türevli koddur (ör. "SIYAH-42"). N11 <c>N11SkuPushCandidate</c> ikizi.</summary>
public sealed record EtsySkuPushCandidate(
    Guid VariantId,
    string VariantCode,
    List<SalesChannelEtsyProductListingAttribute> Attributes);

/// <summary>
/// Etsy ürün listelemesi — bir ERP <see cref="Integration.TradeXpress.Products.Product"/>'ın belirli bir Etsy satış
/// kanalında (SalesChannelEtsy) listelenmesi. <b>Company-owned + per-tenant</b>. Etsy'ye çok-adımlı push edilir
/// (createDraftListing + updateListingInventory + uploadImage×N + publish). <see cref="SellerSkuBase"/> = "{Ürün.Code}-{Seq}"
/// (kayıt-bazlı upsert kimliği); <see cref="EtsyListingId"/> ilk publish'te Etsy tarafından atanır. Aynı kanalda aynı
/// ürün için ÇOK kayıt olabilir; kanal SET-ONCE. N11 <c>SalesChannelTrN11Product</c> ikizi (Etsy alan delta'sıyla).
/// </summary>
public class SalesChannelEtsyProduct : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelEtsyProduct()
    {
    }

    public SalesChannelEtsyProduct(
        Guid companyId,
        Guid salesChannelId,
        Guid productId,
        string sellerSkuBase,
        int sequenceNo,
        EtsyListingType listingType = EtsyListingType.Physical)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetProduct(productId);
        SetSellerSkuBase(sellerSkuBase, sequenceNo);
        ListingType = listingType;
        PreparingDay = 1;
        ShouldAutoRenew = true;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (güvenlik sınırı, set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip Etsy satış kanalı (set-once; kanalın kimliğiyle push edilir).</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Listelenen ERP ürünü (set-once; id-only, nav yok).</summary>
    public virtual Guid ProductId { get; protected set; }

    /// <summary>Etsy satıcı SKU tabanı — KAYIT-BAZLI benzersiz ("{Ürün.Code}-{SequenceNo}"). Set-once: sonradan ürün
    /// kodu değişse bile sabit kalır ki push aynı uzak listelemeye gitsin.</summary>
    public virtual string SellerSkuBase { get; protected set; } = null!;

    /// <summary>Kayıt sırası (aynı ürün+kanal içinde; silinmişler DAHİL max+1 üretilir). Varyant stok kodu
    /// eklerinde de kullanılır ("{VaryantKodu}-{SequenceNo}") — satıcı-geneli stok kodu çakışmasın.</summary>
    public virtual int SequenceNo { get; protected set; }

    // ── Etsy listing config ──

    /// <summary>Etsy taksonomi yaprağı (<c>taxonomy_id</c>; opsiyonel — yayın için zorunlu, taslakta boş olabilir).</summary>
    public virtual long? TaxonomyId { get; protected set; }

    /// <summary>Etsy listeleme türü (fiziksel/dijital). Varsayılan Physical.</summary>
    public virtual EtsyListingType ListingType { get; protected set; }

    /// <summary>Etsy kargo profili id'si (<c>shipping_profile_id</c>; Etsy'de önceden oluşturulmuş profil — yayın için gerekli).</summary>
    public virtual long? ShippingProfileId { get; protected set; }

    /// <summary>Etsy iade politikası id'si (<c>return_policy_id</c>; Etsy'de önceden tanımlı — bazı bölgelerde yayın için gerekli).</summary>
    public virtual long? ReturnPolicyId { get; protected set; }

    /// <summary>Etsy dükkân bölümü id'si (<c>shop_section_id</c>; opsiyonel).</summary>
    public virtual long? ShopSectionId { get; protected set; }

    /// <summary>Etsy minimum işleme süresi (<c>processing_min</c>, gün; opsiyonel).</summary>
    public virtual int? ProcessingMin { get; protected set; }

    /// <summary>Etsy maksimum işleme süresi (<c>processing_max</c>, gün; opsiyonel).</summary>
    public virtual int? ProcessingMax { get; protected set; }

    /// <summary>Etsy başlık override (<c>title</c>; boşsa push'ta ürün adı devralınır).</summary>
    public virtual string? TitleOverride { get; protected set; }

    /// <summary>Etsy açıklama override (<c>description</c>; boşsa push'ta ürün açıklaması devralınır).</summary>
    public virtual string? DescriptionOverride { get; protected set; }

    // ── Kişiselleştirme ──
    // Eski tek-kutulu model (is_personalizable / personalization_instructions / _is_required / _char_count_max)
    // 2026-07-28'de SÖKÜLDÜ: Etsy o alanları 9 Nisan 2026'da kapattı, gönderen istek hata döner. Yerine
    // ÇOKLU ADLANDIRILMIŞ SORU modeli geldi ve bizde onun taşıyıcısı SpecialInfo'dur (aşağıda).
    // "Kişiselleştirilebilir mi" artık SAKLANAN değil TÜREYEN bir bilgidir: <see cref="IsPersonalizable"/>.

    /// <summary>Listeleme süresi dolunca Etsy'de otomatik yenilensin mi (<c>should_auto_renew</c>). Etsy-ÖZEL
    /// (N11/Trendyol yenilemez). Varsayılan true.</summary>
    public virtual bool ShouldAutoRenew { get; protected set; }

    // ── Ortak listeleme ──

    /// <summary>Kargoya verilme süresi (gün) — varsayılan 1. (ProcessingMin/Max ile örtüşebilir; yine de tutulur.)</summary>
    public virtual int PreparingDay { get; protected set; }

    /// <summary>Etsy para birimi (opsiyonel; id-only, nav yok). Boşsa varyant para birimi devralınır.
    /// (Etsy'de listing para birimi mağazaya sabittir — push'ta uyum kontrolü.)</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Satıcı notu (kanal-özel kısa düz not; opsiyonel).</summary>
    public virtual string? SellerNote { get; protected set; }

    /// <summary>Etsy taksonomi varyasyon-DIŞI attribute değerleri (owned → JSON; N11 CategoryAttributes deseni).</summary>
    public virtual List<SalesChannelEtsyProductListingAttribute> ListingAttributes { get; protected set; } = new();

    /// <summary>Etsy etiketleri (owned → JSON; ≤13).</summary>
    public virtual List<SalesChannelEtsyProductTag> Tags { get; protected set; } = new();

    /// <summary>Etsy malzemeleri (owned → JSON; ≤13).</summary>
    public virtual List<SalesChannelEtsyProductMaterial> Materials { get; protected set; } = new();

    /// <summary>Kişiselleştirme soruları (owned → JSON; her satır bir Etsy <c>personalization question</c>).</summary>
    public virtual List<SalesChannelEtsyProductSpecialInfo> SpecialInfo { get; protected set; } = new();

    /// <summary>Bu listeleme kişiselleştirilebilir mi (<c>is_personalizable</c>). SAKLANMAZ — en az bir
    /// kişiselleştirme sorusu varsa true. Etsy'de de aynı ilişki var: son soru silinince bayrak kendiliğinden
    /// false'a düşer, ayrıca yazılamaz.</summary>
    public virtual bool IsPersonalizable
    {
        get
        {
            return SpecialInfo.Count > 0;
        }
    }

    /// <summary>Varyant-başına Etsy SKU kimlik satırları (owned → JSON) — FrozenSku dondurma + Etsy product
    /// id/version + push snapshot'ı. Satır SİLİNMEZ (varyant yok olsa da Etsy'de yaşıyor olabilir).</summary>
    public virtual List<SalesChannelEtsyProductSku> Skus { get; protected set; } = new();

    // ── Etsy senkron durumu (push sonrası) ──

    /// <summary>Etsy'nin atadığı listing id'si (ilk başarılı publish'te dolar).</summary>
    public virtual long? EtsyListingId { get; protected set; }

    /// <summary>Etsy listeleme durumu (dönen <c>state</c>: draft/active/inactive).</summary>
    public virtual string? ListingState { get; protected set; }

    public virtual DateTime? LastSyncedAt { get; protected set; }

    /// <summary>Son push hatası (başarısızsa dolu, başarıda temizlenir).</summary>
    public virtual string? LastError { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Etsy taksonomi yaprağı (opsiyonel; yayın öncesi doldurulur). Dolu ise pozitif olmalı (fail-fast).</summary>
    public virtual void SetTaxonomy(long? taxonomyId)
    {
        if (taxonomyId is { } value && value <= 0)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:TaxonomyIdInvalid");
        }

        TaxonomyId = taxonomyId;
    }

    public virtual void SetListingType(EtsyListingType listingType)
    {
        ListingType = listingType;
    }

    /// <summary>Etsy kargo profili id'si (opsiyonel; dolu ise pozitif). Boş=null.</summary>
    public virtual void SetShippingProfile(long? shippingProfileId)
    {
        if (shippingProfileId is { } value && value <= 0)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ShippingProfileIdInvalid");
        }

        ShippingProfileId = shippingProfileId;
    }

    /// <summary>Etsy iade politikası id'si (opsiyonel; dolu ise pozitif). Boş=null.</summary>
    public virtual void SetReturnPolicy(long? returnPolicyId)
    {
        if (returnPolicyId is { } value && value <= 0)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ReturnPolicyIdInvalid");
        }

        ReturnPolicyId = returnPolicyId;
    }

    /// <summary>Etsy dükkân bölümü id'si (opsiyonel; dolu ise pozitif). Boş=null.</summary>
    public virtual void SetShopSection(long? shopSectionId)
    {
        if (shopSectionId is { } value && value <= 0)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ShopSectionIdInvalid");
        }

        ShopSectionId = shopSectionId;
    }

    /// <summary>Etsy işleme süresi aralığı (gün; opsiyonel). Dolu değer ≥1; ikisi de doluysa min ≤ max (fail-fast).</summary>
    public virtual void SetProcessing(int? processingMin, int? processingMax)
    {
        if (processingMin is { } min && min < 1)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ProcessingInvalid");
        }

        if (processingMax is { } max && max < 1)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ProcessingInvalid");
        }

        if (processingMin is { } lo && processingMax is { } hi && lo > hi)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ProcessingInvalid");
        }

        ProcessingMin = processingMin;
        ProcessingMax = processingMax;
    }

    /// <summary>Etsy başlık override (opsiyonel; boş değilse trim + max). Boşsa push'ta ürün adı devralınır.</summary>
    public virtual void SetTitleOverride(string? titleOverride)
    {
        TitleOverride = StringFieldGuard.EnsureOptionalText(
            titleOverride, nameof(TitleOverride), 1, SalesChannelEtsyProductConsts.TitleOverrideMaxLength);
    }

    /// <summary>Etsy açıklama override (opsiyonel; boş değilse trim + max). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public virtual void SetDescriptionOverride(string? descriptionOverride)
    {
        DescriptionOverride = StringFieldGuard.EnsureOptionalText(
            descriptionOverride, nameof(DescriptionOverride), 1, SalesChannelEtsyProductConsts.DescriptionOverrideMaxLength);
    }

    /// <summary>Listeleme süresi dolunca Etsy'de otomatik yenilensin mi (should_auto_renew). Varsayılan true.</summary>
    public virtual void SetAutoRenew(bool shouldAutoRenew)
    {
        ShouldAutoRenew = shouldAutoRenew;
    }

    /// <summary>Kargoya verilme süresi (gün) — en az 1 (fail-fast).</summary>
    public virtual void SetPreparingDay(int preparingDay)
    {
        if (preparingDay < 1)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:PreparingDayInvalid");
        }

        PreparingDay = preparingDay;
    }

    /// <summary>Etsy para birimi (opsiyonel; sadece atama, boş=null).</summary>
    public virtual void SetCurrencyUnit(Guid? currencyUnitId)
    {
        CurrencyUnitId = currencyUnitId == Guid.Empty ? null : currencyUnitId;
    }

    /// <summary>Satıcı notu (opsiyonel; boş değilse trim + max).</summary>
    public virtual void SetSellerNote(string? sellerNote)
    {
        SellerNote = StringFieldGuard.EnsureOptionalText(
            sellerNote, nameof(SellerNote), 1, SalesChannelEtsyProductConsts.SellerNoteMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Etsy taksonomi varyasyon-DIŞI attribute değerleri (boş adlı satır elenir; trim). N11 SetCategoryAttributes ikizi.</summary>
    public virtual void SetListingAttributes(IEnumerable<SalesChannelEtsyProductListingAttribute>? attributes)
    {
        ListingAttributes = (attributes ?? Enumerable.Empty<SalesChannelEtsyProductListingAttribute>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new SalesChannelEtsyProductListingAttribute(a.Name.Trim(), (a.Value ?? string.Empty).Trim()))
            .ToList();
    }

    /// <summary>Etsy etiketleri — boş elenir, trim, en fazla <see cref="SalesChannelEtsyProductConsts.MaxTagCount"/>.</summary>
    public virtual void SetTags(IEnumerable<SalesChannelEtsyProductTag>? tags)
    {
        Tags = (tags ?? Enumerable.Empty<SalesChannelEtsyProductTag>())
            .Where(t => !string.IsNullOrWhiteSpace(t.Value))
            .Select(t => new SalesChannelEtsyProductTag(t.Value.Trim()))
            .Take(SalesChannelEtsyProductConsts.MaxTagCount)
            .ToList();
    }

    /// <summary>Etsy malzemeleri — boş elenir, trim, en fazla <see cref="SalesChannelEtsyProductConsts.MaxMaterialCount"/>.</summary>
    public virtual void SetMaterials(IEnumerable<SalesChannelEtsyProductMaterial>? materials)
    {
        Materials = (materials ?? Enumerable.Empty<SalesChannelEtsyProductMaterial>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Value))
            .Select(m => new SalesChannelEtsyProductMaterial(m.Value.Trim()))
            .Take(SalesChannelEtsyProductConsts.MaxMaterialCount)
            .ToList();
    }

    /// <summary>Kişiselleştirme soruları — her satır bir Etsy <c>personalization question</c>. Boş başlıklı satır
    /// elenir, başlık/değer trim'lenir.
    ///
    /// <para>Etsy kısıtları FAIL-FAST doğrulanır (push'ta sessiz reddedilmek yerine burada patlasın):
    /// en fazla <see cref="SalesChannelEtsyProductConsts.MaxSpecialInfoCount"/> soru · cevap karakter tavanı
    /// verilmişse 1..<see cref="SalesChannelEtsyProductConsts.SpecialInfoMaxAllowedCharactersLimit"/>.
    /// Başlık uzunluğu <see cref="StringFieldGuard"/> ile ayrıca sınırlanır.</para>
    ///
    /// <para>Sayı sınırı SESSİZCE KIRPILMAZ (Tags/Materials'taki Take deseninin aksine): kullanıcı 7 soru
    /// tanımladıysa hangi 2'sinin düştüğünü bilmeli — kişiselleştirme sorusu kaybı sipariş içeriğini değiştirir.</para></summary>
    public virtual void SetSpecialInfo(IEnumerable<SalesChannelEtsyProductSpecialInfo>? specialInfo)
    {
        var rows = (specialInfo ?? Enumerable.Empty<SalesChannelEtsyProductSpecialInfo>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Key))
            .ToList();

        if (rows.Count > SalesChannelEtsyProductConsts.MaxSpecialInfoCount)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:SpecialInfoCountExceeded")
                .WithData("Max", SalesChannelEtsyProductConsts.MaxSpecialInfoCount)
                .WithData("Actual", rows.Count);
        }

        foreach (var row in rows)
        {
            if (row.MaxAllowedCharacters is { } max
                && (max < 1 || max > SalesChannelEtsyProductConsts.SpecialInfoMaxAllowedCharactersLimit))
            {
                throw new BusinessException("TradeXpress:Etsy:Product:SpecialInfoMaxAllowedCharactersInvalid")
                    .WithData("Key", row.Key)
                    .WithData("Limit", SalesChannelEtsyProductConsts.SpecialInfoMaxAllowedCharactersLimit);
            }
        }

        SpecialInfo = rows
            .Select(s => new SalesChannelEtsyProductSpecialInfo(
                StringFieldGuard.EnsureRequiredText(
                    s.Key,
                    nameof(SalesChannelEtsyProductSpecialInfo.Key),
                    1,
                    SalesChannelEtsyProductConsts.SpecialInfoKeyMaxLength),
                (s.Value ?? string.Empty).Trim(),
                s.IsRequired,
                s.MaxAllowedCharacters))
            .ToList();
    }

    /// <summary>Her varyanta gidecek FrozenSku'yu belirler — <b>entity'yi MUTASYONA UĞRATMAZ</b> (push ÖNCESİ güvenli
    /// çağrı): mevcut dondurulmuş satır kodunu tercih eder, eşleşme yoksa O ANKİ koddan üretir. Push başarısız olsa
    /// bile yeni satır persist edilmez (kod ancak başarılı push'ta <see cref="ReconcileSkus"/> ile kalıcılaşır).</summary>
    public virtual IReadOnlyDictionary<Guid, string> PlanStockCodes(IReadOnlyList<EtsySkuPushCandidate> candidates)
    {
        var assignment = AssignSkus(candidates, allowCreate: false);
        return candidates.ToDictionary(
            c => c.VariantId,
            c => assignment[c.VariantId]?.FrozenSku ?? BuildStockCode(c.VariantCode));
    }

    /// <summary>Push edilecek varyant setini kalıcı SKU satırlarıyla eşler + eksikleri kurar (BAŞARILI push SONRASI
    /// çağrılır) — varyant başına satır döner. Eşleme sırası (çalınma olmasın diye TÜM set üzerinden aşamalı):
    /// (1) ProductVariantId birebir; (2) dondurulmuş stok kodu = adayın üreteceği kod; (3) attribute imzası;
    /// (4) hiçbiri yoksa YENİ satır (FrozenSku O ANKİ varyant kodundan üretilir ve DONDURULUR). Yeniden bağlanan
    /// satırın ProductVariantId'si güncellenir; FrozenSku ASLA değişmez. N11 ReconcileSkus AYNEN.</summary>
    public virtual IReadOnlyDictionary<Guid, SalesChannelEtsyProductSku> ReconcileSkus(IReadOnlyList<EtsySkuPushCandidate> candidates)
    {
        return AssignSkus(candidates, allowCreate: true)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    // Ortak eşleme metodu (SSOT): PlanStockCodes (readonly, allowCreate=false) ve ReconcileSkus (allowCreate=true)
    // aynı iki-aşamalı deterministik atamayı paylaşır → plan ile commit AYNI kodu üretir.
    private Dictionary<Guid, SalesChannelEtsyProductSku?> AssignSkus(IReadOnlyList<EtsySkuPushCandidate> candidates, bool allowCreate)
    {
        var map = new Dictionary<Guid, SalesChannelEtsyProductSku?>();
        var claimed = new HashSet<SalesChannelEtsyProductSku>();
        var pending = new List<EtsySkuPushCandidate>();

        // (1) VariantId birebir — hâlâ bağlı satırlar önce sahiplenilir ki imza eşlemesi onları çalamasın.
        foreach (var candidate in candidates)
        {
            var byId = Skus.FirstOrDefault(s => s.ProductVariantId == candidate.VariantId);
            if (byId is not null && claimed.Add(byId))
            {
                map[candidate.VariantId] = byId;
            }
            else
            {
                pending.Add(candidate);
            }
        }

        // (2) Dondurulmuş kod eşleşmesi → (3) attribute imzası → (4) yeni satır (yalnız allowCreate).
        foreach (var candidate in pending)
        {
            var candidateCode = BuildStockCode(candidate.VariantCode);
            var sku = Skus.FirstOrDefault(s =>
                          !claimed.Contains(s)
                          && string.Equals(s.FrozenSku, candidateCode, StringComparison.OrdinalIgnoreCase))
                      ?? MatchUnclaimedBySignature(candidate.Attributes, claimed);

            if (sku is null && allowCreate)
            {
                sku = new SalesChannelEtsyProductSku(candidate.VariantId, candidateCode);
                Skus.Add(sku);
            }

            if (sku is not null)
            {
                sku.ProductVariantId = candidate.VariantId;   // yeniden-bağlama; kod DONDURULMUŞ kalır
                claimed.Add(sku);
            }

            map[candidate.VariantId] = sku;
        }

        return map;
    }

    /// <summary>Başarılı push SONRASI gönderilen SKU verisini kaydeder (dirty-tracking + sipariş-eşleme snapshot'ı).
    /// Push başarısızsa çağrılmaz — LastSent* yalnız Etsy'ye GERÇEKTEN ulaşan değerleri yansıtır.</summary>
    public virtual void RecordSkuPush(string frozenSku, int quantity, decimal? price, IEnumerable<SalesChannelEtsyProductListingAttribute> snapshot)
    {
        var sku = FindSku(frozenSku);
        if (sku is null)
        {
            return;
        }

        sku.LastSentQuantity = quantity;
        sku.LastSentPrice = price;
        sku.PropertySnapshot = snapshot
            .Select(a => new SalesChannelEtsyProductListingAttribute(a.Name, a.Value))
            .ToList();
    }

    /// <summary>Stok/fiyat senkronu SONRASI — SKU'nun son gönderilen adet/fiyatını + version'ını günceller.
    /// <b>PropertySnapshot'a DOKUNMAZ</b> (stok/fiyat senkronunda seçenekler değişmez).</summary>
    public virtual void RecordStockPriceSync(string frozenSku, int quantity, decimal? price, long? version)
    {
        var sku = FindSku(frozenSku);
        if (sku is null)
        {
            return;
        }

        sku.LastSentQuantity = quantity;
        sku.LastSentPrice = price;
        sku.EtsyOfferingVersion = version ?? sku.EtsyOfferingVersion;
    }

    /// <summary>Etsy yanıtındaki SKU kimliğini (id/version) yerel satıra işler — SKU-düzeyi mutabakat anahtarı.
    /// Yanıtta olmayan alan yereldekini SİLMEZ.</summary>
    public virtual void ApplySkuIdentity(string frozenSku, long? etsyProductId, long? version)
    {
        var sku = FindSku(frozenSku);
        if (sku is null)
        {
            return;
        }

        sku.EtsyProductId = etsyProductId ?? sku.EtsyProductId;
        sku.EtsyOfferingVersion = version ?? sku.EtsyOfferingVersion;
    }

    /// <summary>Varyant SKU stok kodu — kayıt-scoped: İLK listeleme ÇIPLAK varyant kodunu taşır, aynı ürünün ikinci
    /// listelemesinden itibaren "-{SequenceNo}" son eki ayırır (satıcı-geneli stok kodu çakışmaz). Kural
    /// <see cref="ChannelSequenceCode"/>'da (SSOT) — "-1" üretilmez.</summary>
    public virtual string BuildStockCode(string variantCode)
    {
        return ChannelSequenceCode.Compose(variantCode, SequenceNo);
    }

    /// <summary>IMPORT ile bir SKU kimlik satırını upsert eder (Trendyol <c>UpsertImportedSku</c> ikizi). FrozenSku
    /// REMOTE'tan gelir (Etsy'de zaten yaşayan offering'in sku'su ya da varyant-türevli kod) ve DOĞDUĞU GİBİ dondurulur;
    /// yerel "{VaryantKodu}-{Sıra}" üretimi bu satıra HİÇ uygulanmaz. <see cref="SalesChannelEtsyProductSku.EtsyProductId"/>
    /// = Etsy inventory <c>product_id</c> (offering-düzeyi mutabakat anahtarı). İkinci import aynı satırı bulur
    /// (FrozenSku eşleşmesi), yeniden bağlar; FrozenSku ASLA değişmez.</summary>
    public virtual void UpsertImportedSku(Guid productVariantId, string frozenSku, long? etsyProductId)
    {
        // Varyant bağı zorunlu — fail-fast konvansiyonu (SetProduct/SetSalesChannel ile simetrik guard).
        if (productVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelEtsyProductSku.ProductVariantId));
        }

        var normalizedSku = StringFieldGuard.EnsureRequiredText(
            frozenSku, nameof(SalesChannelEtsyProductSku.FrozenSku), 1, SalesChannelEtsyProductConsts.StockCodeMaxLength);

        var sku = FindSku(normalizedSku);
        if (sku is null)
        {
            sku = new SalesChannelEtsyProductSku(productVariantId, normalizedSku);
            Skus.Add(sku);
        }
        else
        {
            sku.ProductVariantId = productVariantId;   // yeniden-bağlama; FrozenSku DONDURULMUŞ kalır
        }

        sku.EtsyProductId = etsyProductId ?? sku.EtsyProductId;
    }

    /// <summary>Başarılı publish/güncelleme sonrası Etsy durumunu işaretler (hata temizlenir).</summary>
    public virtual void MarkSynced(long? etsyListingId, string? listingState, DateTime syncedAtUtc)
    {
        EtsyListingId = etsyListingId ?? EtsyListingId;
        ListingState = StringFieldGuard.EnsureOptionalText(listingState, nameof(ListingState), 1, SalesChannelEtsyProductConsts.ListingStateMaxLength);
        LastSyncedAt = syncedAtUtc;
        LastError = null;
    }

    /// <summary>Başarısız push sonrası hatayı kaydeder (senkron durumu korunur).</summary>
    public virtual void MarkSyncFailed(string? error, DateTime attemptedAtUtc)
    {
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, SalesChannelEtsyProductConsts.LastErrorMaxLength);
        LastSyncedAt = attemptedAtUtc;
    }

    public override string ToString()
    {
        return $"{ProductId} @ {SalesChannelId}";
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetSalesChannel(Guid salesChannelId)
    {
        if (salesChannelId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelId));
        }

        SalesChannelId = salesChannelId;
    }

    private void SetProduct(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductId));
        }

        ProductId = productId;
    }

    // Etsy satıcı SKU tabanı + sıra — SET-ONCE (yalnız ctor'dan; sonradan değişirse uzak listeleme kimliği kayar).
    private void SetSellerSkuBase(string sellerSkuBase, int sequenceNo)
    {
        SellerSkuBase = StringFieldGuard.EnsureRequiredText(
            sellerSkuBase, nameof(SellerSkuBase), 1, SalesChannelEtsyProductConsts.SellerSkuBaseMaxLength);
        if (sequenceNo < 1)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:SequenceNoInvalid");
        }

        SequenceNo = sequenceNo;
    }

    // Sahiplenilmemiş satırlar içinde attribute imzası eşleşmesi — aynı seçenek kombinasyonu = aynı uzak SKU.
    private SalesChannelEtsyProductSku? MatchUnclaimedBySignature(
        List<SalesChannelEtsyProductListingAttribute> attributes, HashSet<SalesChannelEtsyProductSku> claimed)
    {
        if (attributes.Count == 0)
        {
            return null;   // imzasız aday belirsiz — yanlış satıra bağlanmaktansa yeni satır açılır
        }

        var signature = SignatureOf(attributes);
        return Skus.FirstOrDefault(s =>
            !claimed.Contains(s)
            && s.PropertySnapshot.Count > 0
            && SignatureOf(s.PropertySnapshot) == signature);
    }

    // Seçenek imzası: ada göre sıralı, "NAME<US>VALUE" çiftleri <RS> ile birleştirilir. Ayraçlar (Unit/Record
    // Separator) metinde geçemez → birleşim belirsizliği yok. Normalizasyon Türkçe kültürle (tr-TR): validator
    // "beden"="Beden" sayarken imza da aynı katlamayı yapsın (İ/ı invariant/tr-TR ayrışması eşleşmeyi bozmasın).
    private static string SignatureOf(IEnumerable<SalesChannelEtsyProductListingAttribute> attributes)
    {
        return string.Join(
            '',
            attributes
                .Select(a => $"{NormalizeForSignature(a.Name)}{NormalizeForSignature(a.Value)}")
                .OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string NormalizeForSignature(string value)
    {
        return value.Trim().ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
    }

    private SalesChannelEtsyProductSku? FindSku(string frozenSku)
    {
        return Skus.FirstOrDefault(s => string.Equals(s.FrozenSku, frozenSku, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
