using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol kategori attribute değeri (id-bazlı; Trendyol attributeId + attributeValueId ya da serbest
/// customValue) — owned, JSON kolonuna serialize edilir.</summary>
public class TrendyolListingAttribute
{
    /// <summary>Trendyol attribute id'si (kategori attribute tanımından).</summary>
    public int AttributeId { get; set; }

    /// <summary>Trendyol attribute value id'si (değer listesinden seçilen). Serbest değerde null.</summary>
    public int? AttributeValueId { get; set; }

    /// <summary>Serbest (custom) değer — attribute değer listesi kabul etmiyorsa. Value id ile birlikte kullanılmaz.</summary>
    public string? CustomValue { get; set; }

    public TrendyolListingAttribute()
    {
    }

    public TrendyolListingAttribute(int attributeId, int? attributeValueId, string? customValue)
    {
        AttributeId = attributeId;
        AttributeValueId = attributeValueId;
        CustomValue = customValue;
    }
}

/// <summary>
/// Trendyol ürün listelemesi — bir ERP <see cref="Integration.TradeXpress.Products.Product"/>'ın belirli bir Trendyol
/// satış kanalında (SalesChannelTrTrendyol) listelenmesi. <b>Company-owned + per-tenant</b>. Trendyol'a ASENKRON
/// gönderilir (submit → <see cref="BatchRequestId"/>; durum ayrıca batch-request sorgusuyla çekilir). Kanalın KENDİ
/// kimliğiyle push edilir; varyantlar Trendyol item'larına (barcode/stockCode) eşlenir. Kimlik (SalesChannelId, ProductId) benzersiz.
/// </summary>
public class TrendyolProductListing : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected TrendyolProductListing()
    {
    }

    public TrendyolProductListing(
        Guid companyId,
        Guid salesChannelId,
        Guid productId,
        string categoryId,
        string brandId)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetProduct(productId);
        SetCategory(categoryId, null);
        SetBrand(brandId);
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

    /// <summary>Trendyol kategori id'si (numerik; string tutulur).</summary>
    public virtual string CategoryId { get; protected set; } = null!;

    /// <summary>Kategori görüntü adı (opsiyonel; UI kolaylığı).</summary>
    public virtual string? CategoryName { get; protected set; }

    /// <summary>Trendyol marka id'si (numerik; string tutulur — Trendyol zorunlu).</summary>
    public virtual string BrandId { get; protected set; } = null!;

    /// <summary>KDV oranı (Trendyol vatRate; %). Varsayılan 20.</summary>
    public virtual int VatRate { get; protected set; }

    /// <summary>Trendyol kargo firması id'si (cargoCompanyId; opsiyonel — kanal/varsayılan kargo).</summary>
    public virtual int? CargoCompanyId { get; protected set; }

    /// <summary>Desi/hacimsel ağırlık (Trendyol dimensionalWeight; opsiyonel).</summary>
    public virtual decimal? DimensionalWeight { get; protected set; }

    /// <summary>Trendyol kategori attribute değerleri (id-bazlı; owned → JSON).</summary>
    public virtual List<TrendyolListingAttribute> Attributes { get; protected set; } = new();

    // ── Trendyol senkron durumu (async submit sonrası) ──
    /// <summary>Trendyol'un döndürdüğü batch istek kimliği (durum bununla sorgulanır).</summary>
    public virtual string? BatchRequestId { get; protected set; }

    /// <summary>Son bilinen batch/işlem durumu (PROCESSING/COMPLETED/FAILED ...).</summary>
    public virtual string? Status { get; protected set; }

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

    public virtual void SetBrand(string brandId)
    {
        BrandId = StringFieldGuard.EnsureRequiredText(
            brandId, nameof(BrandId), 1, TrendyolProductConsts.BrandIdMaxLength);
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

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetAttributes(IEnumerable<TrendyolListingAttribute>? attributes)
    {
        Attributes = (attributes ?? Enumerable.Empty<TrendyolListingAttribute>())
            .Where(a => a.AttributeId > 0)
            .Select(a => new TrendyolListingAttribute(
                a.AttributeId,
                a.AttributeValueId,
                string.IsNullOrWhiteSpace(a.CustomValue) ? null : a.CustomValue!.Trim()))
            .ToList();
    }

    /// <summary>Async submit sonrası: batch id + PROCESSING durumu işaretlenir (hata temizlenir).</summary>
    public virtual void MarkSubmitted(string? batchRequestId, DateTime submittedAtUtc)
    {
        BatchRequestId = StringFieldGuard.EnsureOptionalText(
            batchRequestId, nameof(BatchRequestId), 1, TrendyolProductConsts.BatchRequestIdMaxLength);
        Status = "PROCESSING";
        LastSyncedAt = submittedAtUtc;
        LastError = null;
    }

    /// <summary>Batch durum sorgusu sonrası: durum + (varsa) hata mesajı işaretlenir.</summary>
    public virtual void MarkStatus(string? status, string? error, DateTime syncedAtUtc)
    {
        Status = StringFieldGuard.EnsureOptionalText(status, nameof(Status), 1, TrendyolProductConsts.StatusMaxLength);
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, TrendyolProductConsts.LastErrorMaxLength);
        LastSyncedAt = syncedAtUtc;
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

    #endregion
}
