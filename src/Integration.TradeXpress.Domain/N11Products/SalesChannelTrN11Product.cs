using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.N11Products;

/// <summary>N11 kategori attribute değeri (name/value) — owned, JSON kolonuna serialize edilir.</summary>
public class SalesChannelTrN11ProductAttribute
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public SalesChannelTrN11ProductAttribute()
    {
    }

    public SalesChannelTrN11ProductAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }
}

/// <summary>N11 SKU kimlik satırı (varyant-başına; owned → JSON). <see cref="SellerStockCode"/> İLK kuruluşta üretilir
/// ve DONDURULUR: ProductVariant.Code sonradan değişse ya da synchronizer varyantı silip yeniden üretse bile push
/// aynı uzak SKU'ya gider (Faz 1 — sellerStockCode kayması koruması). <see cref="AttributeSnapshot"/> = push edilen
/// name/value çiftleri (sipariş→varyant çözümünün ve yeniden-bağlama imzasının temeli; N11 sipariş kaleminde
/// sellerStockCode YOK, varyant yalnız attribute imzasıyla bulunur).</summary>
public class SalesChannelTrN11ProductSku
{
    /// <summary>Bağlı ERP varyantı (yeniden üretilirse kod/imza üzerinden bu alana yeniden bağlanır).</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>N11 satıcı-geneli SKU stok kodu — DONDURULMUŞ ("{VaryantKodu}-{SequenceNo}", kuruluş anındaki kod).</summary>
    public string SellerStockCode { get; set; } = string.Empty;

    /// <summary>N11'in atadığı SKU id'si (push yanıtından; SKU-düzeyi mutabakat anahtarı).</summary>
    public long? N11SkuId { get; set; }

    /// <summary>N11 SKU versiyonu — fiyat/adet değişiminde (satış dahil) artar; drift sinyali.</summary>
    public long? N11Version { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen adet (Faz 2 dirty-tracking temeli).</summary>
    public int? LastSentQuantity { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen optionPrice (mutlak liste fiyatı).</summary>
    public decimal? LastSentOptionPrice { get; set; }

    /// <summary>Push edilen varyant seçenekleri (name/value) — sipariş eşleme + imza.</summary>
    public List<SalesChannelTrN11ProductAttribute> AttributeSnapshot { get; set; } = new();

    public SalesChannelTrN11ProductSku()
    {
    }

    public SalesChannelTrN11ProductSku(Guid productVariantId, string sellerStockCode)
    {
        ProductVariantId = productVariantId;
        SellerStockCode = sellerStockCode;
    }
}

/// <summary>N11 varyant EKSENİ (owned → JSON) — kullanıcının N11 formunda tanımladığı stockItem eksen adı
/// (N11 kategorisi isVariant setiyle uyumlu, ör. "Beden") + N11-uyumlu değerleri ("S"/"M"/"L"). Ana üründeki
/// nitelik/değer sihirbazının N11-scope karşılığı. Push'ta bu eksenlerin KARTEZYENİ stockItem kombinasyonlarını
/// verir; her kombinasyon isim/değer imzasıyla ERP varyantına eşleşip fiyat/stok/kod alır (SSOT ERP).</summary>
public class SalesChannelTrN11ProductVariantAxis
{
    public string Name { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();

    public SalesChannelTrN11ProductVariantAxis()
    {
    }

    public SalesChannelTrN11ProductVariantAxis(string name, IEnumerable<string> values)
    {
        Name = name;
        Values = values.ToList();
    }
}

/// <summary>Push edilecek varyant adayı — <see cref="SalesChannelTrN11Product.ReconcileSkus"/> girdisi
/// (varyant kimliği + kodu + N11'e gidecek seçenek çiftleri).</summary>
public sealed record N11SkuPushCandidate(
    Guid VariantId,
    string VariantCode,
    List<SalesChannelTrN11ProductAttribute> Attributes);

/// <summary>N11 Seyahat kategorisi özel bilgi (key=TurProgrami/IptalIadeKosullari/EkHizmetler, value=HTML) — owned, JSON.</summary>
public class SalesChannelTrN11ProductSpecialInfo
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public SalesChannelTrN11ProductSpecialInfo()
    {
    }

    public SalesChannelTrN11ProductSpecialInfo(string key, string value)
    {
        Key = key;
        Value = value;
    }
}

/// <summary>
/// N11 ürün listelemesi — bir ERP <see cref="Integration.TradeXpress.Products.Product"/>'ın belirli bir N11
/// satış kanalında (SalesChannelTrN11) listelenmesi. <b>Company-owned + per-tenant</b>. N11'e SaveProduct ile
/// gönderilir (kanalın KENDİ kimliğiyle): ürün + varyantları (stockItems) + kategori (leaf) + attribute'lar +
/// kargo şablonu + condition + Seyahat özel bilgisi. <see cref="ProductSellerCode"/> = Ürün.Code (N11 upsert kimliği);
/// <see cref="N11ProductId"/> ilk push'ta N11 tarafından atanır. Aynı kanalda aynı ürün için ÇOK kayıt olabilir (2026-07-07); kanal SET-ONCE.
/// </summary>
public class SalesChannelTrN11Product : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11Product()
    {
    }

    public SalesChannelTrN11Product(
        Guid companyId,
        Guid salesChannelId,
        Guid productId,
        string sellerCode,
        int sequenceNo,
        string categoryExternalId,
        string shipmentTemplateName,
        N11ProductCondition condition = N11ProductCondition.New)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetProduct(productId);
        SetSellerCode(sellerCode, sequenceNo);
        SetCategory(categoryExternalId, null);
        SetShipmentTemplate(shipmentTemplateName);
        Condition = condition;
        Domestic = true;
        PreparingDay = 1;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (güvenlik sınırı, set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip N11 satış kanalı (set-once; kanalın kimliğiyle push edilir).</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Listelenen ERP ürünü (set-once; id-only, nav yok).</summary>
    public virtual Guid ProductId { get; protected set; }

    /// <summary>N11 upsert kimliği (productSellerCode) — KAYIT-BAZLI benzersiz ("{ÜrünKodu}-{SequenceNo}";
    /// 2026-07-07 kullanıcı kararı: her kayıt N11'de AYRI listeleme). Set-once: sonradan ürün kodu değişse bile
    /// sabit kalır ki push aynı uzak listelemeye gitsin.</summary>
    public virtual string SellerCode { get; protected set; } = null!;

    /// <summary>Kayıt sırası (aynı ürün+kanal içinde; silinmişler DAHİL max+1 üretilir). Varyant stok kodu
    /// eklerinde de kullanılır ("{VaryantKodu}-{SequenceNo}") — N11'de satıcı-geneli stok kodu çakışmasın.</summary>
    public virtual int SequenceNo { get; protected set; }

    /// <summary>N11 leaf kategori id'si (ExternalId). Ürün yalnız yaprak kategoriye listelenir.</summary>
    public virtual string CategoryExternalId { get; protected set; } = null!;

    /// <summary>Kategori görüntü adı (opsiyonel; UI kolaylığı).</summary>
    public virtual string? CategoryName { get; protected set; }

    public virtual N11ProductCondition Condition { get; protected set; }

    /// <summary>N11 kargo şablonu adı (N11ShipmentTemplate.TemplateName — N11'de isimle referans).</summary>
    public virtual string ShipmentTemplateName { get; protected set; } = null!;

    /// <summary>Yerli üretim mi (N11 domestic).</summary>
    public virtual bool Domestic { get; protected set; }

    /// <summary>Kargoya verilme süresi (gün) — N11 preparingDay (zorunlu). Varsayılan 1.</summary>
    public virtual int PreparingDay { get; protected set; }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel).</summary>
    public virtual int? MaxPurchaseQuantity { get; protected set; }

    /// <summary>N11 kategori attribute değerleri (owned → JSON).</summary>
    public virtual List<SalesChannelTrN11ProductAttribute> Attributes { get; protected set; } = new();

    /// <summary>Seyahat kategorisi özel bilgi (owned → JSON; kategori Seyahat ise zorunlu).</summary>
    public virtual List<SalesChannelTrN11ProductSpecialInfo> SpecialInfo { get; protected set; } = new();

    /// <summary>Varyant-başına N11 SKU kimlik satırları (owned → JSON) — sellerStockCode dondurma + N11 SKU
    /// id/version + push snapshot'ı. Satır SİLİNMEZ (varyant yok olsa da N11'de yaşıyor olabilir; emeklilik Faz 3).</summary>
    public virtual List<SalesChannelTrN11ProductSku> Skus { get; protected set; } = new();

    /// <summary>N11 varyant eksenleri (owned → JSON) — kullanıcının N11 formunda tanımladığı stockItem eksen/değer
    /// sihirbazı. BOŞSA push mevcut davranışa döner (ERP varyant nitelikleri doğrudan gider).</summary>
    public virtual List<SalesChannelTrN11ProductVariantAxis> VariantAxes { get; protected set; } = new();

    // ── N11 senkron durumu (push sonrası) ──
    /// <summary>N11'in atadığı ürün id'si (ilk başarılı push'ta dolar).</summary>
    public virtual long? N11ProductId { get; protected set; }

    /// <summary>N11 satış durumu (dönen saleStatus).</summary>
    public virtual string? SaleStatus { get; protected set; }

    /// <summary>N11 onay durumu (dönen approvalStatus).</summary>
    public virtual string? ApprovalStatus { get; protected set; }

    public virtual DateTime? LastSyncedAt { get; protected set; }

    /// <summary>Son push hatası (başarısızsa dolu, başarıda temizlenir).</summary>
    public virtual string? LastError { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCategory(string categoryExternalId, string? categoryName)
    {
        CategoryExternalId = StringFieldGuard.EnsureRequiredText(
            categoryExternalId, nameof(CategoryExternalId), 1, N11ProductConsts.ExternalIdMaxLength);
        CategoryName = StringFieldGuard.EnsureOptionalText(categoryName, nameof(CategoryName), 1, N11ProductConsts.CategoryNameMaxLength);
    }

    public virtual void SetCondition(N11ProductCondition condition)
    {
        Condition = condition;
    }

    public virtual void SetShipmentTemplate(string shipmentTemplateName)
    {
        ShipmentTemplateName = StringFieldGuard.EnsureRequiredText(
            shipmentTemplateName, nameof(ShipmentTemplateName), 1, N11ProductConsts.ShipmentTemplateNameMaxLength);
    }

    public virtual void SetDomestic(bool domestic)
    {
        Domestic = domestic;
    }

    /// <summary>Kargoya verilme süresi (gün) — en az 1 (fail-fast).</summary>
    public virtual void SetPreparingDay(int preparingDay)
    {
        if (preparingDay < 1)
        {
            throw new BusinessException("TradeXpress:N11:Product:PreparingDayInvalid");
        }

        PreparingDay = preparingDay;
    }

    public virtual void SetMaxPurchaseQuantity(int? maxPurchaseQuantity)
    {
        if (maxPurchaseQuantity is { } value && value < 1)
        {
            throw new BusinessException("TradeXpress:N11:Product:MaxPurchaseQuantityInvalid");
        }

        MaxPurchaseQuantity = maxPurchaseQuantity;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetAttributes(IEnumerable<SalesChannelTrN11ProductAttribute>? attributes)
    {
        Attributes = (attributes ?? Enumerable.Empty<SalesChannelTrN11ProductAttribute>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new SalesChannelTrN11ProductAttribute(a.Name.Trim(), (a.Value ?? string.Empty).Trim()))
            .ToList();
    }

    public virtual void SetSpecialInfo(IEnumerable<SalesChannelTrN11ProductSpecialInfo>? specialInfo)
    {
        SpecialInfo = (specialInfo ?? Enumerable.Empty<SalesChannelTrN11ProductSpecialInfo>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Key) && !string.IsNullOrWhiteSpace(s.Value))
            .Select(s => new SalesChannelTrN11ProductSpecialInfo(s.Key.Trim(), s.Value))
            .ToList();
    }

    /// <summary>N11 varyant eksenlerini ayarlar (sihirbaz) — adı boş eksen + boş/yinelenen değer elenir; en az bir
    /// değeri olmayan eksen atılır. Eksen adları kendi içinde benzersiz (aynı eksen iki kez tanımlanamaz).</summary>
    public virtual void SetVariantAxes(IEnumerable<SalesChannelTrN11ProductVariantAxis>? axes)
    {
        var normalized = (axes ?? Enumerable.Empty<SalesChannelTrN11ProductVariantAxis>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new SalesChannelTrN11ProductVariantAxis(
                a.Name.Trim(),
                (a.Values ?? new List<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)))
            .Where(a => a.Values.Count > 0)
            .ToList();

        if (normalized.Select(a => a.Name.ToUpperInvariant()).Distinct().Count() != normalized.Count)
        {
            throw new BusinessException("TradeXpress:N11:Product:DuplicateVariantAxis");
        }

        VariantAxes = normalized;
    }

    /// <summary>Her varyanta gidecek sellerStockCode'u belirler — <b>entity'yi MUTASYONA UĞRATMAZ</b> (push
    /// ÖNCESİ güvenli çağrı): mevcut dondurulmuş satır kodunu tercih eder, eşleşme yoksa O ANKİ koddan üretir.
    /// Push başarısız olsa bile yeni satır persist edilmez (kod ancak başarılı push'ta <see cref="ReconcileSkus"/>
    /// ile kalıcılaşır) — böylece "hiç N11'e ulaşmamış bayat kod" DB'ye donmaz.</summary>
    public virtual IReadOnlyDictionary<Guid, string> PlanStockCodes(IReadOnlyList<N11SkuPushCandidate> candidates)
    {
        var assignment = AssignSkus(candidates, allowCreate: false);
        return candidates.ToDictionary(
            c => c.VariantId,
            c => assignment[c.VariantId]?.SellerStockCode ?? BuildStockCode(c.VariantCode));
    }

    /// <summary>Push edilecek varyant setini kalıcı SKU satırlarıyla eşler + eksikleri kurar (BAŞARILI push
    /// SONRASI çağrılır) — varyant başına satır döner. Eşleme sırası (çalınma olmasın diye TÜM set üzerinden
    /// aşamalı): (1) ProductVariantId birebir; (2) dondurulmuş stok kodu = adayın üreteceği kod (synchronizer
    /// varyantı silip AYNI kodla yeniden üretti); (3) attribute imzası (kod da değiştiyse son ağ — aynı seçenek
    /// kombinasyonu = aynı uzak SKU); (4) hiçbiri yoksa YENİ satır (stok kodu O ANKİ varyant kodundan üretilir ve
    /// DONDURULUR). Yeniden bağlanan satırın ProductVariantId'si güncellenir; SellerStockCode ASLA değişmez.</summary>
    public virtual IReadOnlyDictionary<Guid, SalesChannelTrN11ProductSku> ReconcileSkus(IReadOnlyList<N11SkuPushCandidate> candidates)
    {
        return AssignSkus(candidates, allowCreate: true)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    // Ortak eşleme çekirdeği (SSOT): PlanStockCodes (readonly, allowCreate=false) ve ReconcileSkus (allowCreate=true)
    // aynı iki-aşamalı deterministik atamayı paylaşır → plan ile commit AYNI kodu üretir.
    private Dictionary<Guid, SalesChannelTrN11ProductSku?> AssignSkus(IReadOnlyList<N11SkuPushCandidate> candidates, bool allowCreate)
    {
        var map = new Dictionary<Guid, SalesChannelTrN11ProductSku?>();
        var claimed = new HashSet<SalesChannelTrN11ProductSku>();
        var pending = new List<N11SkuPushCandidate>();

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
                          && string.Equals(s.SellerStockCode, candidateCode, StringComparison.OrdinalIgnoreCase))
                      ?? MatchUnclaimedBySignature(candidate.Attributes, claimed);

            if (sku is null && allowCreate)
            {
                sku = new SalesChannelTrN11ProductSku(candidate.VariantId, candidateCode);
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

    /// <summary>Başarılı SaveProduct SONRASI gönderilen SKU verisini kaydeder (dirty-tracking + sipariş-eşleme
    /// snapshot'ı). Push başarısızsa çağrılmaz — LastSent* yalnız N11'e GERÇEKTEN ulaşan değerleri yansıtır.</summary>
    public virtual void RecordSkuPush(string sellerStockCode, int quantity, decimal? optionPrice, IEnumerable<SalesChannelTrN11ProductAttribute> snapshot)
    {
        var sku = FindSku(sellerStockCode);
        if (sku is null)
        {
            return;
        }

        sku.LastSentQuantity = quantity;
        sku.LastSentOptionPrice = optionPrice;
        sku.AttributeSnapshot = snapshot
            .Select(a => new SalesChannelTrN11ProductAttribute(a.Name, a.Value))
            .ToList();
    }

    /// <summary>Faz 2 stok/fiyat senkronu SONRASI — SKU'nun son gönderilen adet/fiyatını + version'ını günceller.
    /// <b>AttributeSnapshot'a DOKUNMAZ</b> (stok/fiyat senkronunda seçenekler değişmez; snapshot Faz 1 push'unun
    /// kaydıdır). Dirty-tracking temeli: sonraki senkron bu değerlerle karşılaştırır.</summary>
    public virtual void RecordStockPriceSync(string sellerStockCode, int quantity, decimal? optionPrice, long? version)
    {
        var sku = FindSku(sellerStockCode);
        if (sku is null)
        {
            return;
        }

        sku.LastSentQuantity = quantity;
        sku.LastSentOptionPrice = optionPrice;
        sku.N11Version = version ?? sku.N11Version;
    }

    /// <summary>N11 yanıtındaki SKU kimliğini (id/version) yerel satıra işler — SKU-düzeyi mutabakat anahtarı.
    /// Yanıtta olmayan alan yereldekini SİLMEZ.</summary>
    public virtual void ApplySkuIdentity(string sellerStockCode, long? n11SkuId, long? version)
    {
        var sku = FindSku(sellerStockCode);
        if (sku is null)
        {
            return;
        }

        sku.N11SkuId = n11SkuId ?? sku.N11SkuId;
        sku.N11Version = version ?? sku.N11Version;
    }

    /// <summary>Varyant SKU stok kodu — kayıt-scoped ("{VaryantKodu}-{SequenceNo}"): aynı ürünün ikinci N11
    /// listelemesinde satıcı-geneli stok kodu çakışmaz. TEK üretim yeri (SSOT).</summary>
    public virtual string BuildStockCode(string variantCode)
    {
        return $"{variantCode}-{SequenceNo}";
    }

    /// <summary>Başarılı push sonrası N11 durumunu işaretler (hata temizlenir).</summary>
    public virtual void MarkSynced(long? n11ProductId, string? saleStatus, string? approvalStatus, DateTime syncedAtUtc)
    {
        N11ProductId = n11ProductId ?? N11ProductId;
        SaleStatus = StringFieldGuard.EnsureOptionalText(saleStatus, nameof(SaleStatus), 1, N11ProductConsts.StatusMaxLength);
        ApprovalStatus = StringFieldGuard.EnsureOptionalText(approvalStatus, nameof(ApprovalStatus), 1, N11ProductConsts.StatusMaxLength);
        LastSyncedAt = syncedAtUtc;
        LastError = null;
    }

    /// <summary>Başarısız push sonrası hatayı kaydeder (senkron durumu korunur).</summary>
    public virtual void MarkSyncFailed(string? error, DateTime attemptedAtUtc)
    {
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, N11ProductConsts.LastErrorMaxLength);
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

    // N11 upsert kimliği + sıra — SET-ONCE (yalnız ctor'dan; sonradan değişirse uzak listeleme kimliği kayar).
    private void SetSellerCode(string sellerCode, int sequenceNo)
    {
        SellerCode = StringFieldGuard.EnsureRequiredText(
            sellerCode, nameof(SellerCode), 1, N11ProductConsts.SellerCodeMaxLength);
        if (sequenceNo < 1)
        {
            throw new BusinessException("TradeXpress:N11:Product:SequenceNoInvalid");
        }

        SequenceNo = sequenceNo;
    }

    // Sahiplenilmemiş satırlar içinde attribute imzası eşleşmesi — aynı seçenek kombinasyonu = aynı uzak SKU.
    private SalesChannelTrN11ProductSku? MatchUnclaimedBySignature(
        List<SalesChannelTrN11ProductAttribute> attributes, HashSet<SalesChannelTrN11ProductSku> claimed)
    {
        if (attributes.Count == 0)
        {
            return null;   // imzasız aday belirsiz — yanlış satıra bağlanmaktansa yeni satır açılır
        }

        var signature = SignatureOf(attributes);
        return Skus.FirstOrDefault(s =>
            !claimed.Contains(s)
            && s.AttributeSnapshot.Count > 0
            && SignatureOf(s.AttributeSnapshot) == signature);
    }

    // Seçenek imzası: ada göre sıralı, "NAME<US>VALUE" çiftleri <RS> ile birleştirilir. Ayraçlar (Unit/Record
    // Separator) metinde geçemez → birleşim belirsizliği yok. Normalizasyon Türkçe kültürle (tr-TR): validator
    // "beden"="Beden" sayarken imza da aynı katlamayı yapsın (İ/ı invariant/tr-TR ayrışması eşleşmeyi bozmasın).
    private static string SignatureOf(IEnumerable<SalesChannelTrN11ProductAttribute> attributes)
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

    private SalesChannelTrN11ProductSku? FindSku(string sellerStockCode)
    {
        return Skus.FirstOrDefault(s => string.Equals(s.SellerStockCode, sellerStockCode, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
