namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="SalesChannelTrTrendyolProductAttribute"/> özelliğinin bir DEĞERİ (ör. Renk → "Kırmızı"/"Yeşil"/"Siyah").
/// ERP <see cref="Integration.TradeXpress.Products.ProductAttributeValue"/> deseninin Trendyol-scope klonu —
/// klon-sonra-ayrış (N11 portu): ERP'de karşılığı olmayan değerler (ör. sadece Trendyol'da satılan "Siyah") burada
/// serbestçe eklenebilir; bu tür değerlerin doğurduğu varyant kombinasyonları Trendyol-only satırlardır
/// (<see cref="SalesChannelTrTrendyolProductStockItem.ProductVariantId"/> null). <b>Company-owned</b> (denormalize) + per-tenant.
/// </summary>
public class SalesChannelTrTrendyolProductAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrTrendyolProductAttributeValue()
    {
    }

    public SalesChannelTrTrendyolProductAttributeValue(
        Guid companyId,
        Guid attributeId,
        string value,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetAttribute(attributeId);
        SetValue(value);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip özellik — id-only referans. Set-once.</summary>
    public virtual Guid AttributeId { get; protected set; }

    public virtual string Value { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetValue(string value)
    {
        Value = StringFieldGuard.NormalizeName(
            value, nameof(Value), EntityFieldConsts.NameMinLength, TrendyolProductConsts.AttributeValueMaxLength);
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Value;
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetAttribute(Guid attributeId)
    {
        if (attributeId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(AttributeId));
        }

        AttributeId = attributeId;
    }

    #endregion
}
