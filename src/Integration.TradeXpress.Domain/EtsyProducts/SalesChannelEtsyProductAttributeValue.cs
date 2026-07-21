namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// <see cref="SalesChannelEtsyProductAttribute"/> özelliğinin bir DEĞERİ (ör. Renk → "Kırmızı"/"Yeşil"/"Siyah").
/// ERP <see cref="Integration.TradeXpress.Products.ProductAttributeValue"/> deseninin Etsy-scope klonu — klon-sonra-ayrış:
/// ERP'de karşılığı olmayan değerler (ör. sadece Etsy'de satılan "Siyah") burada serbestçe eklenebilir; bu tür
/// değerlerin doğurduğu varyant kombinasyonları Etsy-only SKU'lardır (<see cref="SalesChannelEtsyProductStockItem.ProductVariantId"/>
/// null). <b>Company-owned</b> (denormalize) + per-tenant. N11 <c>SalesChannelTrN11ProductAttributeValue</c> ikizi AYNEN.
/// </summary>
public class SalesChannelEtsyProductAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelEtsyProductAttributeValue()
    {
    }

    public SalesChannelEtsyProductAttributeValue(
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
            value, nameof(Value), EntityFieldConsts.NameMinLength, SalesChannelEtsyProductConsts.AttributeValueMaxLength);
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
