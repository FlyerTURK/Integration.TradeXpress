namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol kanal-ürününe özel varyant ÖZELLİĞİ (ör. "Renk", "Beden") — ERP <see cref="Integration.TradeXpress.Products.ProductAttribute"/>
/// deseninin Trendyol-scope KLONU (N11 <c>SalesChannelTrN11ProductAttribute</c> ile birebir aynı desen).
/// Klon-sonra-ayrış: oluşturulduğunda ERP niteliklerinden üretilir (klonlama mantığı Application katmanında), ama
/// sonrasında ERP'den BAĞIMSIZ yaşar — Trendyol'da yeni özellik eklenebilir/var olan silinebilir, ERP'ye dokunmadan.
/// Değerleri <see cref="SalesChannelTrTrendyolProductAttributeValue"/>'lardır; kombinasyonlar
/// (<see cref="SalesChannelTrTrendyolProductStockItem"/>) bu özelliklerin değer KOMBİNASYONLARINDAN doğar.
/// Trendyol'un id-bazlı KATEGORİ attribute'larından (<see cref="SalesChannelTrTrendyolProductCategoryAttribute"/>)
/// TAMAMEN AYRIDIR — bu tip kombinasyon üretimi içindir, pazaryeri kategori şeması değildir.
/// <b>Company-owned</b> (kanal-üründen denormalize) + per-tenant.
/// </summary>
public class SalesChannelTrTrendyolProductAttribute : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrTrendyolProductAttribute()
    {
    }

    public SalesChannelTrTrendyolProductAttribute(
        Guid companyId,
        Guid salesChannelTrTrendyolProductId,
        string name,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelTrTrendyolProductId);
        SetName(name);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip Trendyol kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelTrTrendyolProductId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, TrendyolProductConsts.AttributeNameMaxLength);
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

    private void SetChannelProduct(Guid salesChannelTrTrendyolProductId)
    {
        if (salesChannelTrTrendyolProductId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelTrTrendyolProductId));
        }

        SalesChannelTrTrendyolProductId = salesChannelTrTrendyolProductId;
    }

    #endregion
}
