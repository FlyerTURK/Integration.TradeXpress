namespace Integration.TradeXpress.N11Products;

/// <summary>
/// <see cref="SalesChannelTrN11ProductAttribute"/> özelliğinin bir DEĞERİ (ör. Renk → "Kırmızı"/"Yeşil"/"Siyah").
/// ERP <see cref="Integration.TradeXpress.Products.ProductAttributeValue"/> deseninin N11-scope klonu — klon-sonra-ayrış
/// (2026-07-09 kullanıcı kararı): ERP'de karşılığı olmayan değerler (ör. sadece N11'de satılan "Siyah") burada serbestçe
/// eklenebilir; bu tür değerlerin doğurduğu varyant kombinasyonları N11-only SKU'lardır (<see cref="SalesChannelTrN11ProductStockItem.ProductVariantId"/>
/// null). <b>Company-owned</b> (denormalize) + per-tenant.
/// </summary>
public class SalesChannelTrN11ProductAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductAttributeValue()
    {
    }

    public SalesChannelTrN11ProductAttributeValue(
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
            value, nameof(Value), EntityFieldConsts.NameMinLength, N11ProductConsts.AttributeValueMaxLength);
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
