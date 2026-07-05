using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün attribute'u (varyant ekseni) — bir <see cref="Product"/>'a bağlı (<see cref="ProductId"/> set-once).
/// Ör. "Renk", "Beden". Ürün başına en fazla <see cref="ProductAttributeConsts.MaxAttributesPerProduct"/> (5)
/// tanımlanabilir (kural AppService'te zorlanır). Değerleri <c>ProductAttributeValue</c>'lardır; varyantlar bu
/// attribute'ların değer KOMBİNASYONLARINDAN doğar (üretim API/Domain tarafında). <b>Company-owned</b> (parent
/// üründen denormalize) + per-tenant.
/// </summary>
public class ProductAttribute : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — parent üründen denormalize (güvenlik sınırı). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip ürün — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid ProductId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    protected ProductAttribute() { }

    public ProductAttribute(
        Guid companyId,
        Guid productId,
        string name,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetProduct(productId);
        SetName(name);
        DisplayOrder = displayOrder;
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ProductAttributeConsts.NameMaxLength);
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

    private void SetProduct(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductId));
        }

        ProductId = productId;
    }

    public override string ToString()
    {
        return Name;
    }
}
