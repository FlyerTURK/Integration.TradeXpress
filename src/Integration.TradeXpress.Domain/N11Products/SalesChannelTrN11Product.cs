using System;
using System.Collections.Generic;
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
/// <see cref="N11ProductId"/> ilk push'ta N11 tarafından atanır. Kimlik (SalesChannelId, ProductId) benzersiz.
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
        string categoryExternalId,
        string shipmentTemplateName,
        N11ProductCondition condition = N11ProductCondition.New)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetProduct(productId);
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

    #endregion
}
