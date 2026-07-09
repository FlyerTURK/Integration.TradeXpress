namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 kanal-ürününe özel varyant EKSENİ (ör. "Renk", "Beden") — ERP <see cref="Integration.TradeXpress.Products.ProductAttribute"/>
/// deseninin N11-scope KLONU. Klon-sonra-ayrış (2026-07-09 kullanıcı kararı): oluşturulduğunda ERP eksenlerinden
/// üretilir (klonlama mantığı Application katmanında), ama sonrasında ERP'den BAĞIMSIZ yaşar — N11'de yeni eksen
/// eklenebilir/var olan silinebilir, ERP'ye dokunmadan. Değerleri <see cref="SalesChannelTrN11ProductAttributeAxisValue"/>'lardır;
/// varyantlar (<see cref="SalesChannelTrN11ProductVariant"/>) bu eksenlerin değer KOMBİNASYONLARINDAN doğar.
/// <b>Company-owned</b> (kanal-üründen denormalize) + per-tenant.
/// </summary>
public class SalesChannelTrN11ProductAttributeAxis : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductAttributeAxis()
    {
    }

    public SalesChannelTrN11ProductAttributeAxis(
        Guid companyId,
        Guid salesChannelTrN11ProductId,
        string name,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelTrN11ProductId);
        SetName(name);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip N11 kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelTrN11ProductId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, N11ProductConsts.AttributeAxisNameMaxLength);
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Name;
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetChannelProduct(Guid salesChannelTrN11ProductId)
    {
        if (salesChannelTrN11ProductId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelTrN11ProductId));
        }

        SalesChannelTrN11ProductId = salesChannelTrN11ProductId;
    }

    #endregion
}
