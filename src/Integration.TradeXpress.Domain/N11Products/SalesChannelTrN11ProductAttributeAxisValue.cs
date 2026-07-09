namespace Integration.TradeXpress.N11Products;

/// <summary>
/// <see cref="SalesChannelTrN11ProductAttributeAxis"/> ekseninin bir DEĞERİ (ör. Renk → "Kırmızı"/"Yeşil"/"Siyah").
/// ERP <see cref="Integration.TradeXpress.Products.ProductAttributeValue"/> deseninin N11-scope klonu — klon-sonra-ayrış
/// (2026-07-09 kullanıcı kararı): ERP'de karşılığı olmayan değerler (ör. sadece N11'de satılan "Siyah") burada serbestçe
/// eklenebilir; bu tür değerlerin doğurduğu varyant kombinasyonları N11-only SKU'lardır (<see cref="SalesChannelTrN11ProductVariant.ProductVariantId"/>
/// null). <b>Company-owned</b> (denormalize) + per-tenant.
/// </summary>
public class SalesChannelTrN11ProductAttributeAxisValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductAttributeAxisValue()
    {
    }

    public SalesChannelTrN11ProductAttributeAxisValue(
        Guid companyId,
        Guid axisId,
        string value,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetAxis(axisId);
        SetValue(value);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip eksen — id-only referans. Set-once.</summary>
    public virtual Guid AxisId { get; protected set; }

    public virtual string Value { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetValue(string value)
    {
        Value = StringFieldGuard.NormalizeName(
            value, nameof(Value), EntityFieldConsts.NameMinLength, N11ProductConsts.AttributeAxisValueMaxLength);
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

    private void SetAxis(Guid axisId)
    {
        if (axisId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(AxisId));
        }

        AxisId = axisId;
    }

    #endregion
}
