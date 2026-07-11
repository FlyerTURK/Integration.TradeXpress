using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol KATEGORİ attribute değeri (id-bazlı; Trendyol attributeId + attributeValueId ya da serbest
/// customValue) — owned, JSON kolonuna serialize edilir. Ad "CategoryAttribute" (N11 sözlüğüyle hizalı, S6 rename):
/// varyant-kombinasyon üreten <see cref="SalesChannelTrTrendyolProductAttribute"/> ENTITY'sinden tamamen ayrıdır.</summary>
public class SalesChannelTrTrendyolProductCategoryAttribute
{
    /// <summary>Trendyol attribute id'si (kategori attribute tanımından).</summary>
    public int AttributeId { get; set; }

    /// <summary>Trendyol attribute value id'si (değer listesinden seçilen). Serbest değerde null.</summary>
    public int? AttributeValueId { get; set; }

    /// <summary>Serbest (custom) değer — attribute değer listesi kabul etmiyorsa. Value id ile birlikte kullanılmaz.</summary>
    public string? CustomValue { get; set; }

    public SalesChannelTrTrendyolProductCategoryAttribute()
    {
    }

    public SalesChannelTrTrendyolProductCategoryAttribute(int attributeId, int? attributeValueId, string? customValue)
    {
        AttributeId = attributeId;
        AttributeValueId = attributeValueId;
        CustomValue = customValue;
    }
}

/// <summary>Varyant-belirleyici (varianter) attribute'un id çifti — SKU yeniden-bağlama imzasının temeli.
/// <see cref="SalesChannelTrTrendyolProductSku.AttributeSnapshot"/> içinde tutulur (name/value DEĞİL, id/valueId:
/// Trendyol id-bazlı olduğu için kültür/tr-TR normalizasyonuna gerek kalmaz).</summary>
public class SalesChannelTrTrendyolProductSkuAttribute
{
    public int AttributeId { get; set; }
    public int AttributeValueId { get; set; }

    public SalesChannelTrTrendyolProductSkuAttribute()
    {
    }

    public SalesChannelTrTrendyolProductSkuAttribute(int attributeId, int attributeValueId)
    {
        AttributeId = attributeId;
        AttributeValueId = attributeValueId;
    }
}

/// <summary>Trendyol SKU kimlik satırı (varyant-başına; owned → JSON). <see cref="Barcode"/> İLK başarılı push'ta
/// üretilir ve DONDURULUR: ProductVariant.Code sonradan değişse ya da synchronizer varyantı silip yeniden üretse bile
/// push aynı uzak Trendyol item'ına gider (satıcı-geneli barcode; onaylı üründe DEĞİŞTİRİLEMEZ). <see cref="StockCode"/>
/// = merchantSku (variant-bulk ile güncellenebilir; mutable). <see cref="RemoteContentId"/> = productContentId
/// (content-bulk-update kimliği; başarılı push sonrası dolar). <see cref="AttributeSnapshot"/> = varianter attribute
/// id çiftleri (yeniden-bağlama imzası).</summary>
public class SalesChannelTrTrendyolProductSku
{
    /// <summary>Bağlı ERP varyantı (yeniden üretilirse kod/imza üzerinden bu alana yeniden bağlanır).</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Trendyol satıcı-geneli barcode — DONDURULMUŞ ("{VaryantKodu}-{SequenceNo}", kuruluş anındaki kod).</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>Trendyol stok kodu (= merchantSku; variant-bulk ile güncellenebilir).</summary>
    public string StockCode { get; set; } = string.Empty;

    /// <summary>Trendyol'un atadığı içerik id'si (productContentId; content-bulk-update kimliği). Başarılı push'ta dolar.</summary>
    public long? RemoteContentId { get; set; }

    /// <summary>Push edilen varianter attribute id çiftleri — yeniden-bağlama imzası.</summary>
    public List<SalesChannelTrTrendyolProductSkuAttribute> AttributeSnapshot { get; set; } = new();

    /// <summary>Son BAŞARILI push'ta gönderilen adet (dirty-tracking temeli).</summary>
    public int? LastSentQuantity { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen listePrice (indirim öncesi referans).</summary>
    public decimal? LastSentListPrice { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen salePrice (efektif satış fiyatı).</summary>
    public decimal? LastSentSalePrice { get; set; }

    public SalesChannelTrTrendyolProductSku()
    {
    }

    public SalesChannelTrTrendyolProductSku(Guid productVariantId, string barcode, string stockCode)
    {
        ProductVariantId = productVariantId;
        Barcode = barcode;
        StockCode = stockCode;
    }
}

/// <summary>Push edilecek varyant adayı — <see cref="SalesChannelTrTrendyolProduct.ReconcileSkus"/> girdisi
/// (varyant kimliği + kodu + varianter attribute id çiftleri).</summary>
public sealed record TrendyolSkuPushCandidate(
    Guid VariantId,
    string VariantCode,
    IReadOnlyList<SalesChannelTrTrendyolProductSkuAttribute> VarianterAttributes);

/// <summary>
/// Trendyol ürün listelemesi — bir ERP <see cref="Integration.TradeXpress.Products.Product"/>'ın belirli bir Trendyol
/// satış kanalında (SalesChannelTrTrendyol) listelenmesi. <b>Company-owned + per-tenant</b>. Trendyol'a ASENKRON
/// gönderilir (submit → <see cref="BatchRequestId"/>; durum ayrıca batch-request sorgusuyla çekilir). Kanalın KENDİ
/// kimliğiyle push edilir; varyantlar Trendyol item'larına (barcode/stockCode) eşlenir. <see cref="ProductMainId"/>
/// = varyant grup anahtarı ("{ÜrünKodu}-{SequenceNo}", frozen). Aynı kanalda aynı ürün için ÇOK kayıt olabilir
/// (2026-07-07); kanal SET-ONCE.
/// </summary>
public class SalesChannelTrTrendyolProduct : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrTrendyolProduct()
    {
    }

    public SalesChannelTrTrendyolProduct(
        Guid companyId,
        Guid salesChannelId,
        Guid productId,
        string productMainId,
        int sequenceNo,
        string categoryId,
        string brandId)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetProduct(productId);
        SetProductMainId(productMainId, sequenceNo);
        SetCategory(categoryId, null);
        SetBrand(brandId, null);
        VatRate = 20;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (güvenlik sınırı, set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip Trendyol satış kanalı (set-once; kanalın kimliğiyle push edilir).</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Listelenen ERP ürünü (set-once; id-only, nav yok).</summary>
    public virtual Guid ProductId { get; protected set; }

    /// <summary>Trendyol varyant grup anahtarı (productMainId) — KAYIT-BAZLI benzersiz ("{ÜrünKodu}-{SequenceNo}").
    /// Set-once/FROZEN: sonradan ürün kodu değişse bile sabit kalır ki ikinci listeleme çakışmasın ve onaylı üründe
    /// DEĞİŞTİRİLEMEZ olan bu alan aynı uzak gruba gitsin.</summary>
    public virtual string ProductMainId { get; protected set; } = null!;

    /// <summary>Kayıt sırası (aynı ürün+kanal içinde; silinmişler DAHİL max+1 üretilir). Barcode/productMainId
    /// eklerinde de kullanılır ("{VaryantKodu}-{SequenceNo}") — Trendyol'da satıcı-geneli çakışmasın.</summary>
    public virtual int SequenceNo { get; protected set; }

    /// <summary>Trendyol kategori id'si (numerik; string tutulur).</summary>
    public virtual string CategoryId { get; protected set; } = null!;

    /// <summary>Kategori görüntü adı (opsiyonel; UI kolaylığı).</summary>
    public virtual string? CategoryName { get; protected set; }

    /// <summary>Trendyol marka id'si (numerik; string tutulur — Trendyol zorunlu; onaylıda değiştirilemez).</summary>
    public virtual string BrandId { get; protected set; } = null!;

    /// <summary>Marka görüntü adı (marka arama sonucundan; opsiyonel).</summary>
    public virtual string? BrandName { get; protected set; }

    /// <summary>KDV oranı (Trendyol vatRate; %). Varsayılan 20.</summary>
    public virtual int VatRate { get; protected set; }

    /// <summary>Trendyol kargo firması id'si (cargoCompanyId) — REZERVE: Trendyol V2 create şemasında yer almadığı
    /// için push'a KONMAZ (kargo panel/satıcı seviyesi); ileride shipment-provider referansı netleşirse kullanılır.</summary>
    public virtual int? CargoCompanyId { get; protected set; }

    /// <summary>Desi/hacimsel ağırlık (Trendyol dimensionalWeight; opsiyonel).</summary>
    public virtual decimal? DimensionalWeight { get; protected set; }

    /// <summary>Kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public virtual string? Description { get; protected set; }

    /// <summary>Kargoya teslim süresi (gün) — Trendyol deliveryOption.deliveryDuration (opsiyonel).
    /// <see cref="FastDeliveryType"/> doluysa 1 olmalıdır.</summary>
    public virtual int? DeliveryDuration { get; protected set; }

    /// <summary>Hızlı teslimat tipi (opsiyonel). Doluysa <see cref="DeliveryDuration"/>=1 zorunludur.</summary>
    public virtual TrendyolFastDeliveryType? FastDeliveryType { get; protected set; }

    /// <summary>Trendyol kategori attribute değerleri (id-bazlı; owned → JSON kolonu "Attributes" — S6 tip rename'i
    /// şemayı DEĞİŞTİRMEZ).</summary>
    public virtual List<SalesChannelTrTrendyolProductCategoryAttribute> Attributes { get; protected set; } = new();

    /// <summary>Varyant-başına Trendyol SKU kimlik satırları (owned → JSON) — barcode dondurma + contentId + push
    /// snapshot'ı. Satır SİLİNMEZ (varyant yok olsa da Trendyol'da yaşıyor olabilir; emeklilik ileride).
    /// İKİ dolum yolu vardır: (1) PUSH — barcode YEREL üretilir ("{VaryantKodu}-{SequenceNo}", <see cref="BuildBarcode"/>)
    /// ve ilk başarılı push'ta dondurulur; (2) IMPORT (<see cref="UpsertImportedSku"/>) — barcode REMOTE'tan gelir
    /// (Trendyol'da zaten yaşayan değer) ve DOĞDUĞU GİBİ dondurulur; yerel üretim bu satıra HİÇ uygulanmaz
    /// (sonraki push'lar dondurulmuş remote barcode'u aynen kullanır — çatışma yok).</summary>
    public virtual List<SalesChannelTrTrendyolProductSku> Skus { get; protected set; } = new();

    // ── Uzak (Trendyol'daki) kayıt görüntüsü — IMPORT ile dolar, salt bilgi (push'a girmez) ──

    /// <summary>TRENDYOL'un varyant grup anahtarı (satıcının pazaryerine girdiği <c>productMainId</c>) — bizim
    /// ürettiğimiz kayıt-bazlı <see cref="ProductMainId"/>'den ("{ÜrünKodu}-{SequenceNo}", frozen) TAMAMEN AYRI:
    /// bizimki push kimliğidir ve bizde üretilir; bu alan ise pazaryerindeki MEVCUT kaydın kimliğidir ve import'un
    /// kanal-kaydı eşleşme anahtarıdır (ikinci import aynı kaydı bulur, dublike üretmez).</summary>
    public virtual string? RemoteProductMainId { get; protected set; }

    /// <summary>Uzak kayıt Trendyol tarafından ONAYLI mı (listing approved). null = henüz import edilmedi/bilinmiyor.</summary>
    public virtual bool? RemoteApproved { get; protected set; }

    /// <summary>Uzak kayıt SATIŞTA mı (onSale). null = henüz import edilmedi/bilinmiyor.</summary>
    public virtual bool? RemoteOnSale { get; protected set; }

    /// <summary>Uzak kayıttaki liste fiyatı (listPrice; indirim öncesi referans). Import görüntüsü — push fiyat
    /// zinciri StockItem override'larından yürür, bu alan zinciri ETKİLEMEZ.</summary>
    public virtual decimal? ListPrice { get; protected set; }

    // ── Trendyol senkron durumu (async submit sonrası) ──
    /// <summary>Trendyol'un döndürdüğü batch istek kimliği (durum bununla sorgulanır).</summary>
    public virtual string? BatchRequestId { get; protected set; }

    /// <summary>Son gönderilen batch işlem tipi (OnBoarding/Update/InventoryUpdate) — hangi işlemin sonucu ayırt edilir.</summary>
    public virtual string? LastBatchRequestType { get; protected set; }

    /// <summary>Son bilinen batch/işlem durumu (PROCESSING/COMPLETED/FAILED ...).</summary>
    public virtual string? Status { get; protected set; }

    /// <summary>Son batch sonucundaki başarısız kalem sayısı (kısmi-hata sinyali).</summary>
    public virtual int? FailedItemCount { get; protected set; }

    public virtual DateTime? LastSyncedAt { get; protected set; }

    /// <summary>Son push/durum hatası (başarısızsa dolu, başarıda temizlenir).</summary>
    public virtual string? LastError { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCategory(string categoryId, string? categoryName)
    {
        CategoryId = StringFieldGuard.EnsureRequiredText(
            categoryId, nameof(CategoryId), 1, TrendyolProductConsts.CategoryIdMaxLength);
        CategoryName = StringFieldGuard.EnsureOptionalText(
            categoryName, nameof(CategoryName), 1, TrendyolProductConsts.CategoryNameMaxLength);
    }

    public virtual void SetBrand(string brandId, string? brandName)
    {
        BrandId = StringFieldGuard.EnsureRequiredText(
            brandId, nameof(BrandId), 1, TrendyolProductConsts.BrandIdMaxLength);
        BrandName = StringFieldGuard.EnsureOptionalText(
            brandName, nameof(BrandName), 1, TrendyolProductConsts.BrandNameMaxLength);
    }

    /// <summary>KDV oranı (0–100).</summary>
    public virtual void SetVatRate(int vatRate)
    {
        if (vatRate < 0 || vatRate > 100)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:VatRateInvalid");
        }

        VatRate = vatRate;
    }

    public virtual void SetCargoCompany(int? cargoCompanyId)
    {
        if (cargoCompanyId is { } value && value < 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:CargoCompanyInvalid");
        }

        CargoCompanyId = cargoCompanyId;
    }

    public virtual void SetDimensionalWeight(decimal? dimensionalWeight)
    {
        if (dimensionalWeight is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DimensionalWeightInvalid");
        }

        DimensionalWeight = dimensionalWeight;
    }

    /// <summary>Kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), 1, TrendyolProductConsts.DescriptionMaxLength);
    }

    /// <summary>Teslimat seçeneği (opsiyonel). Hızlı teslimat tipi seçildiyse gün süresi 1 olmalıdır (Trendyol kuralı).</summary>
    public virtual void SetDeliveryOption(int? deliveryDuration, TrendyolFastDeliveryType? fastDeliveryType)
    {
        if (deliveryDuration is { } days && days < 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DeliveryDurationInvalid");
        }

        if (fastDeliveryType is not null && deliveryDuration != 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:FastDeliveryRequiresOneDay");
        }

        DeliveryDuration = deliveryDuration;
        FastDeliveryType = fastDeliveryType;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetAttributes(IEnumerable<SalesChannelTrTrendyolProductCategoryAttribute>? attributes)
    {
        Attributes = (attributes ?? Enumerable.Empty<SalesChannelTrTrendyolProductCategoryAttribute>())
            .Where(a => a.AttributeId > 0)
            .Select(a => new SalesChannelTrTrendyolProductCategoryAttribute(
                a.AttributeId,
                a.AttributeValueId,
                string.IsNullOrWhiteSpace(a.CustomValue) ? null : a.CustomValue!.Trim()))
            .ToList();
    }

    /// <summary>Varyant barcode'u — kayıt-scoped ("{VaryantKodu}-{SequenceNo}"): aynı ürünün ikinci Trendyol
    /// listelemesinde satıcı-geneli barcode çakışmaz. TEK üretim yeri (SSOT).</summary>
    public virtual string BuildBarcode(string variantCode)
    {
        return $"{variantCode}-{SequenceNo}";
    }

    /// <summary>Her varyanta gidecek barcode'u belirler — <b>entity'yi MUTASYONA UĞRATMAZ</b> (push ÖNCESİ güvenli
    /// çağrı): mevcut dondurulmuş satır barcode'unu tercih eder, eşleşme yoksa O ANKİ koddan üretir. Push başarısız
    /// olsa bile yeni satır persist edilmez (barcode ancak başarılı push'ta <see cref="ReconcileSkus"/> ile
    /// kalıcılaşır) — böylece "hiç Trendyol'a ulaşmamış bayat barcode" DB'ye donmaz.</summary>
    public virtual IReadOnlyDictionary<Guid, string> PlanBarcodes(IReadOnlyList<TrendyolSkuPushCandidate> candidates)
    {
        var assignment = AssignSkus(candidates, allowCreate: false);
        return candidates.ToDictionary(
            c => c.VariantId,
            c => assignment[c.VariantId]?.Barcode ?? BuildBarcode(c.VariantCode));
    }

    /// <summary>Push edilecek varyant setini kalıcı SKU satırlarıyla eşler + eksikleri kurar (BAŞARILI push
    /// SONRASI çağrılır) — varyant başına satır döner. Eşleme sırası (çalınma olmasın diye TÜM set üzerinden
    /// aşamalı): (1) ProductVariantId birebir; (2) dondurulmuş barcode = adayın üreteceği kod (synchronizer varyantı
    /// silip AYNI kodla yeniden üretti); (3) varianter attribute id imzası (kod da değiştiyse son ağ — aynı seçenek
    /// kombinasyonu = aynı uzak SKU); (4) hiçbiri yoksa YENİ satır (barcode O ANKİ varyant kodundan üretilir ve
    /// DONDURULUR). Yeniden bağlanan satırın ProductVariantId'si güncellenir; Barcode ASLA değişmez.</summary>
    public virtual IReadOnlyDictionary<Guid, SalesChannelTrTrendyolProductSku> ReconcileSkus(IReadOnlyList<TrendyolSkuPushCandidate> candidates)
    {
        return AssignSkus(candidates, allowCreate: true)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    /// <summary>Başarılı push SONRASI gönderilen SKU verisini kaydeder (dirty-tracking + imza snapshot'ı). Push
    /// başarısızsa çağrılmaz — LastSent* yalnız Trendyol'a GERÇEKTEN ulaşan değerleri yansıtır.</summary>
    public virtual void RecordSkuPush(
        string barcode,
        int quantity,
        decimal? listPrice,
        decimal? salePrice,
        IEnumerable<SalesChannelTrTrendyolProductSkuAttribute> snapshot)
    {
        var sku = FindSku(barcode);
        if (sku is null)
        {
            return;
        }

        sku.LastSentQuantity = quantity;
        sku.LastSentListPrice = listPrice;
        sku.LastSentSalePrice = salePrice;
        sku.AttributeSnapshot = snapshot
            .Select(a => new SalesChannelTrTrendyolProductSkuAttribute(a.AttributeId, a.AttributeValueId))
            .ToList();
    }

    /// <summary>Trendyol yanıtındaki içerik id'sini (productContentId) yerel satıra işler — content-bulk-update
    /// kimliği. Yanıtta olmayan alan yereldekini SİLMEZ.</summary>
    public virtual void ApplyRemoteContentId(string barcode, long? remoteContentId)
    {
        var sku = FindSku(barcode);
        if (sku is null)
        {
            return;
        }

        sku.RemoteContentId = remoteContentId ?? sku.RemoteContentId;
    }

    /// <summary>Async submit sonrası: batch id + işlem tipi + PROCESSING durumu işaretlenir (hata temizlenir).</summary>
    public virtual void MarkSubmitted(string? batchRequestId, string? batchRequestType, DateTime submittedAtUtc)
    {
        BatchRequestId = StringFieldGuard.EnsureOptionalText(
            batchRequestId, nameof(BatchRequestId), 1, TrendyolProductConsts.BatchRequestIdMaxLength);
        LastBatchRequestType = StringFieldGuard.EnsureOptionalText(
            batchRequestType, nameof(LastBatchRequestType), 1, TrendyolProductConsts.BatchRequestTypeMaxLength);
        Status = "PROCESSING";
        FailedItemCount = null;
        LastSyncedAt = submittedAtUtc;
        LastError = null;
    }

    /// <summary>Batch durum sorgusu sonrası: durum + başarısız kalem sayısı + (varsa) hata mesajı işaretlenir.</summary>
    public virtual void MarkStatus(string? status, int? failedItemCount, string? error, DateTime syncedAtUtc)
    {
        Status = StringFieldGuard.EnsureOptionalText(status, nameof(Status), 1, TrendyolProductConsts.StatusMaxLength);
        FailedItemCount = failedItemCount;
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, TrendyolProductConsts.LastErrorMaxLength);
        LastSyncedAt = syncedAtUtc;
    }

    /// <summary>Import'ta uzak kayıt görüntüsünü işler (remote productMainId + onay/satış bayrakları + listPrice).
    /// Salt bilgi alanlarıdır — push kimliği <see cref="ProductMainId"/> ve fiyat zinciri DEĞİŞMEZ.</summary>
    public virtual void ApplyRemoteSnapshot(string? remoteProductMainId, bool? approved, bool? onSale, decimal? listPrice)
    {
        if (listPrice is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ListPriceNegative");
        }

        RemoteProductMainId = StringFieldGuard.EnsureOptionalText(
            remoteProductMainId, nameof(RemoteProductMainId), 1, TrendyolProductConsts.ProductMainIdMaxLength);
        RemoteApproved = approved;
        RemoteOnSale = onSale;
        ListPrice = listPrice;
    }

    /// <summary>Import'tan gelen SKU kimlik satırını upsert eder — anahtar BARCODE (remote'tan gelir, DONDURULMUŞ;
    /// yerel "{Kod}-{Sıra}" üretimi bu satıra uygulanmaz). Var olan satırda barcode ASLA değişmez; varyant bağı /
    /// stockCode / contentId tazelenir. Yeni satır remote kimliğiyle doğar.</summary>
    public virtual void UpsertImportedSku(Guid productVariantId, string barcode, string stockCode, long? remoteContentId)
    {
        // Varyant bağı zorunlu — fail-fast konvansiyonu (SetProduct/SetSalesChannel ile simetrik guard).
        if (productVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelTrTrendyolProductSku.ProductVariantId));
        }

        var normalizedBarcode = StringFieldGuard.EnsureRequiredText(
            barcode, nameof(SalesChannelTrTrendyolProductSku.Barcode), 1, TrendyolProductConsts.BarcodeMaxLength);
        var normalizedStockCode = StringFieldGuard.EnsureRequiredText(
            stockCode, nameof(SalesChannelTrTrendyolProductSku.StockCode), 1, TrendyolProductConsts.StockCodeMaxLength);

        var sku = FindSku(normalizedBarcode);
        if (sku is null)
        {
            sku = new SalesChannelTrTrendyolProductSku(productVariantId, normalizedBarcode, normalizedStockCode);
            Skus.Add(sku);
        }
        else
        {
            sku.ProductVariantId = productVariantId;   // yeniden-bağlama; barcode DONDURULMUŞ kalır
            sku.StockCode = normalizedStockCode;
        }

        sku.RemoteContentId = remoteContentId ?? sku.RemoteContentId;
    }

    /// <summary>Başarısız submit/sorgu sonrası hatayı kaydeder.</summary>
    public virtual void MarkSyncFailed(string? error, DateTime attemptedAtUtc)
    {
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, TrendyolProductConsts.LastErrorMaxLength);
        LastSyncedAt = attemptedAtUtc;
    }

    public override string ToString()
    {
        return $"{ProductId} @ {SalesChannelId}";
    }

    // Ortak eşleme çekirdeği (SSOT): PlanBarcodes (readonly, allowCreate=false) ve ReconcileSkus (allowCreate=true)
    // aynı çok-aşamalı deterministik atamayı paylaşır → plan ile commit AYNI barcode'u üretir.
    private Dictionary<Guid, SalesChannelTrTrendyolProductSku?> AssignSkus(IReadOnlyList<TrendyolSkuPushCandidate> candidates, bool allowCreate)
    {
        var map = new Dictionary<Guid, SalesChannelTrTrendyolProductSku?>();
        var claimed = new HashSet<SalesChannelTrTrendyolProductSku>();
        var pending = new List<TrendyolSkuPushCandidate>();

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

        // (2) Dondurulmuş barcode eşleşmesi → (3) attribute id imzası → (4) yeni satır (yalnız allowCreate).
        foreach (var candidate in pending)
        {
            var candidateBarcode = BuildBarcode(candidate.VariantCode);
            var sku = Skus.FirstOrDefault(s =>
                          !claimed.Contains(s)
                          && string.Equals(s.Barcode, candidateBarcode, StringComparison.OrdinalIgnoreCase))
                      ?? MatchUnclaimedBySignature(candidate.VarianterAttributes, claimed);

            if (sku is null && allowCreate)
            {
                sku = new SalesChannelTrTrendyolProductSku(candidate.VariantId, candidateBarcode, candidate.VariantCode);
                Skus.Add(sku);
            }

            if (sku is not null)
            {
                sku.ProductVariantId = candidate.VariantId;   // yeniden-bağlama; barcode DONDURULMUŞ kalır
                claimed.Add(sku);
            }

            map[candidate.VariantId] = sku;
        }

        return map;
    }

    // Sahiplenilmemiş satırlar içinde varianter attribute id imzası eşleşmesi — aynı seçenek kombinasyonu = aynı uzak SKU.
    private SalesChannelTrTrendyolProductSku? MatchUnclaimedBySignature(
        IReadOnlyList<SalesChannelTrTrendyolProductSkuAttribute> attributes, HashSet<SalesChannelTrTrendyolProductSku> claimed)
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

    // Seçenek imzası (id-bazlı): (AttributeId, AttributeValueId) çiftleri id'ye göre sıralı birleştirilir. Trendyol
    // id-bazlı olduğu için N11'in tr-TR kültür normalizasyonu GEREKMEZ (saf sayı karşılaştırması).
    private static string SignatureOf(IEnumerable<SalesChannelTrTrendyolProductSkuAttribute> attributes)
    {
        return string.Join(
            '|',
            attributes
                .Select(a => $"{a.AttributeId}:{a.AttributeValueId}")
                .OrderBy(x => x, StringComparer.Ordinal));
    }

    private SalesChannelTrTrendyolProductSku? FindSku(string barcode)
    {
        return Skus.FirstOrDefault(s => string.Equals(s.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
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

    // Trendyol varyant grup anahtarı + sıra — SET-ONCE/FROZEN (yalnız ctor'dan; sonradan değişirse uzak grup kimliği
    // kayar ve onaylı üründe productMainId DEĞİŞTİRİLEMEZ).
    private void SetProductMainId(string productMainId, int sequenceNo)
    {
        ProductMainId = StringFieldGuard.EnsureRequiredText(
            productMainId, nameof(ProductMainId), 1, TrendyolProductConsts.ProductMainIdMaxLength);
        if (sequenceNo < 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:SequenceNoInvalid");
        }

        SequenceNo = sequenceNo;
    }

    #endregion
}
