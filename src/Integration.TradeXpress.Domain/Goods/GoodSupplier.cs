using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Goods;

/// <summary>
/// Bir mamülün (<see cref="Good"/>) TEDARİKÇİSİ — hangi cari (Account, opsiyonel SubAccount) hangi FİYATLA kaç GÜNDE
/// temin edebiliyor. Good'un tedarikçiler alt drill'inin satır entity'si (ayrı tablo <c>AppGoodSuppliers</c>).
/// Company-scoped (parent Good'dan denormalize) + per-tenant. (<see cref="GoodId"/>, <see cref="AccountId"/>,
/// <see cref="SubAccountId"/>) set-once. Cari hesap (Account) ZORUNLU; alt hesap (SubAccount) OPSİYONEL.
/// </summary>
public class GoodSupplier : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected GoodSupplier()
    {
    }

    public GoodSupplier(Guid? companyId, Guid goodId, Guid accountId, Guid? subAccountId = null)
    {
        CompanyId = companyId;
        SetGood(goodId);
        SetSupplier(accountId, subAccountId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — parent Good'dan denormalize (null = tenant-geneli). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Ait olduğu mamül — id-only, set-once.</summary>
    public virtual Guid GoodId { get; protected set; }

    /// <summary>Tedarikçi ana hesabı (cari) — id-only, ZORUNLU, set-once. Cari hesap tek başına yeterli.</summary>
    public virtual Guid AccountId { get; protected set; }

    /// <summary>Tedarikçi alt hesabı — id-only, OPSİYONEL, set-once. Boşsa cari hesap seviyesinde tedarikçi.</summary>
    public virtual Guid? SubAccountId { get; protected set; }

    /// <summary>Bu tedarikçiden temin fiyatı.</summary>
    public virtual decimal Price { get; protected set; }

    /// <summary>Fiyat para birimi — id-only, opsiyonel.</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Fiyat vergi (KDV) DAHİL mi.</summary>
    public virtual bool TaxIncluded { get; protected set; }

    /// <summary>Kaç GÜNDE temin edilebilir (tedarik süresi, gün).</summary>
    public virtual int LeadDays { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Temin koşullarını atar (fiyat + birim + vergi-dahil + gün). Negatif fiyat/gün geçersiz (fail-fast).</summary>
    public virtual void SetTerms(decimal price, Guid? currencyUnitId, bool taxIncluded, int leadDays)
    {
        if (price < 0m)
        {
            throw new BusinessException("TradeXpress:GoodSupplier:PriceNegative");
        }

        if (leadDays < 0)
        {
            throw new BusinessException("TradeXpress:GoodSupplier:LeadDaysNegative");
        }

        Price = price;
        CurrencyUnitId = currencyUnitId == Guid.Empty ? null : currencyUnitId;
        TaxIncluded = taxIncluded;
        LeadDays = leadDays;
    }

    public override string ToString()
    {
        return $"{GoodId}:{AccountId}";
    }

    private void SetGood(Guid goodId)
    {
        if (goodId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(GoodId));
        }

        GoodId = goodId;
    }

    // Cari hesap ZORUNLU; alt hesap OPSİYONEL (boş/Empty → null).
    private void SetSupplier(Guid accountId, Guid? subAccountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(AccountId));
        }

        AccountId = accountId;
        SubAccountId = subAccountId == Guid.Empty ? null : subAccountId;
    }

    #endregion
}
