using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Bir varyantın PRODUCT-ÖZEL detayı — jenerik <c>EntityVariant</c>'ın Product uzantısı (1:1, <see cref="EntityVariantId"/>
/// set-once). Satış/liste fiyatı (marketplace price/optionPrice) VARYANT seviyesinde; reçete satırları ayrı entity olarak
/// <c>EntityVariantId</c>'ye bağlanır. Company-scoped (varyanttan denormalize) + per-tenant. Jenerik çekirdek (EntityVariant)
/// bu uzantıyı BİLMEZ — sahip (ProductAppService) EntityVariantId ile eşleyip saklar/yükler (<c>GoodVariantDetail</c> deseni).
/// </summary>
public class ProductVariantDetail : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected ProductVariantDetail()
    {
    }

    public ProductVariantDetail(Guid? companyId, Guid entityVariantId)
    {
        CompanyId = companyId;
        SetVariant(entityVariantId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — varyanttan denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Detaylandırdığı jenerik varyant — id-only, set-once (1:1).</summary>
    public virtual Guid EntityVariantId { get; protected set; }

    /// <summary>Satış/liste fiyatı (marketplace price/optionPrice). Null = fiyatlanmamış (henüz listeye hazır değil).</summary>
    public virtual decimal? SalePrice { get; protected set; }

    /// <summary>Satış fiyatı para birimi (CurrencyUnit id-only; N11'e currencyType'a eşlenir). Fiyat null ise null.</summary>
    public virtual Guid? SalePriceCurrencyUnitId { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Satış fiyatı + para birimi (fiyat null → para birimi de null). Negatif fiyat geçersiz (fail-fast).</summary>
    public virtual void SetSalePrice(decimal? price, Guid? currencyUnitId)
    {
        if (price is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:Product:SalePriceNegative");
        }

        SalePrice = price;
        SalePriceCurrencyUnitId = price is null ? null : currencyUnitId;
    }

    public override string ToString()
    {
        return EntityVariantId.ToString();
    }

    private void SetVariant(Guid entityVariantId)
    {
        if (entityVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityVariantId));
        }

        EntityVariantId = entityVariantId;
    }

    #endregion
}
