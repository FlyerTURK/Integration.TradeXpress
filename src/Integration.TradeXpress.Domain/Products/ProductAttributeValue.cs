using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Attribute değeri — bir <see cref="ProductAttribute"/>'a bağlı (<see cref="ProductAttributeId"/> set-once).
/// Ör. Renk → "Kırmızı"/"Mavi", Beden → "S"/"M"/"L". Varyantlar, her attribute'tan bir değer seçilerek oluşan
/// kombinasyonlardır (<c>ProductVariantAttributeValue</c> bağı). <b>Company-owned</b> (denormalize) + per-tenant.
/// </summary>
public class ProductAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize (güvenlik sınırı). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip attribute — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid ProductAttributeId { get; protected set; }

    public virtual string Value { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    protected ProductAttributeValue() { }

    public ProductAttributeValue(
        Guid companyId,
        Guid productAttributeId,
        string value,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetAttribute(productAttributeId);
        SetValue(value);
        DisplayOrder = displayOrder;
    }

    public virtual void SetValue(string value)
    {
        Value = StringFieldGuard.NormalizeName(
            value, nameof(Value), EntityFieldConsts.NameMinLength, ProductAttributeConsts.ValueMaxLength);
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetAttribute(Guid productAttributeId)
    {
        if (productAttributeId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductAttributeId));
        }

        ProductAttributeId = productAttributeId;
    }

    public override string ToString()
    {
        return Value;
    }
}
