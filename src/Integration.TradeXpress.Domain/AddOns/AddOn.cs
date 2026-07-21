using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.AddOns;

/// <summary>
/// Sipariş anı EKLENTİSİ (add-on) — müşteriye ürün siparişinde sunulan fiyatlı seçenek (kurdele, kutu, hediye
/// ambalajı…). Yeniden kullanılabilir <b>katalog</b>: bir kez tanımlanır, ürünlere "Seçenekler" olarak atanır
/// (atama satırında fiyat/zorunluluk override edilebilir). <b>Company-owned</b> (güvenlik sınırı;
/// <see cref="CompanyId"/> non-null <see cref="ICompanyOwned"/>) + per-tenant. Standart kimlik
/// (Code/Name/Description/IsActive) + DisplayOrder; ayrıca <see cref="Price"/> ve tutulduğu para birimi
/// <see cref="CurrencyUnitId"/> (ZORUNLU). Fiyat negatif olamaz (0 = ücretsiz seçenek).
/// </summary>
public class AddOn : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected AddOn()
    {
    }

    public AddOn(
        Guid companyId,
        string code,
        string name,
        Guid currencyUnitId,
        decimal price = 0m,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        SetCurrencyUnit(currencyUnitId);
        SetPrice(price);
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (company-owned; oluşturmadan sonra değişmez).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>Katalog liste fiyatı (varsayılan; ürün atamasında override edilebilir). Negatif olamaz.</summary>
    public virtual decimal Price { get; protected set; }

    /// <summary>Fiyatın tutulduğu para birimi — ZORUNLU (id-only, nav YOK).</summary>
    public virtual Guid CurrencyUnitId { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCode(string code)
    {
        // NormalizeCode: Trim + çoklu boşluk→tek + UPPER (boşluk KORUNUR) + zorunlu/min/max.
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, AddOnConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase + zorunlu/min/max.
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, AddOnConsts.NameMaxLength);
    }

    public virtual void SetCurrencyUnit(Guid currencyUnitId)
    {
        if (currencyUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CurrencyUnitId));
        }

        CurrencyUnitId = currencyUnitId;
    }

    public virtual void SetPrice(decimal price)
    {
        if (price < 0m)
        {
            throw new BusinessException("TradeXpress:AddOn:PriceNegative");
        }

        Price = price;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, AddOnConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Code;
    }

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    #endregion
}
