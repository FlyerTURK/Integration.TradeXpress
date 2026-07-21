namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy kanal-ürününe özel varyant ÖZELLİĞİ (ör. "Renk", "Beden") — ERP <see cref="Integration.TradeXpress.Products.ProductAttribute"/>
/// deseninin Etsy-scope KLONU. Klon-sonra-ayrış: oluşturulduğunda ERP niteliklerinden üretilir (klonlama mantığı
/// Application katmanında), ama sonrasında ERP'den BAĞIMSIZ yaşar — Etsy'de yeni özellik eklenebilir/var olan
/// silinebilir, ERP'ye dokunmadan. Değerleri <see cref="SalesChannelEtsyProductAttributeValue"/>'lardır;
/// kombinasyonlar (<see cref="SalesChannelEtsyProductStockItem"/>) bu özelliklerin değer KOMBİNASYONLARINDAN doğar.
/// <b>Company-owned</b> (kanal-üründen denormalize) + per-tenant. N11 <c>SalesChannelTrN11ProductAttribute</c> ikizi AYNEN.
/// </summary>
public class SalesChannelEtsyProductAttribute : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelEtsyProductAttribute()
    {
    }

    public SalesChannelEtsyProductAttribute(
        Guid companyId,
        Guid salesChannelEtsyProductId,
        string name,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelEtsyProductId);
        SetName(name);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip Etsy kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelEtsyProductId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, SalesChannelEtsyProductConsts.AttributeNameMaxLength);
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

    private void SetChannelProduct(Guid salesChannelEtsyProductId)
    {
        if (salesChannelEtsyProductId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelEtsyProductId));
        }

        SalesChannelEtsyProductId = salesChannelEtsyProductId;
    }

    #endregion
}
