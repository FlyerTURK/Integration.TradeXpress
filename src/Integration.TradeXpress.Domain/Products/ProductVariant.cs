using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün varyantı — bir <see cref="Product"/>'a bağlı (<see cref="ProductId"/>, ZORUNLU, oluşturmadan sonra
/// DEĞİŞMEZ). Satılabilir asıl bilgiyi (Adım 2+: reçete/fiyat/stok/görsel) taşıyacak kayıt. <b>Company-owned</b>
/// (<see cref="ICompanyOwned"/>; <see cref="CompanyId"/> parent üründen DENORMALİZE — SubAccount deseni) + per-tenant.
///
/// <para><b>Ana varyant değişmezi</b> (<see cref="IsMain"/>): Company→HQ Branch / Branch→default Vault ile aynı —
/// ürün başına en-az-1 varyant, tekil main. Yeni varyant ana varyantın snapshot-klonu olarak doğar (sonra bağımsız).
/// Değişmez <c>ProductVariantManager</c>'da yönetilir (en-az-1 · tek-main · ensure-main · klonla).</para>
/// </summary>
public class ProductVariant : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — parent üründen denormalize (güvenlik sınırı). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip ürün — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid ProductId { get; protected set; }

    /// <summary>Ana (main) varyant mı — ürün başına TEKİL (invariant manager'da; Branch.IsHeadquarters / Vault.IsDefault gibi).</summary>
    public virtual bool IsMain { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Satış/liste fiyatı (marketplace price/optionPrice). Null = fiyatlanmamış (henüz listeye hazır değil).</summary>
    public virtual decimal? SalePrice { get; protected set; }

    /// <summary>Satış fiyatı para birimi (CurrencyUnit id-only; N11'e currencyType'a eşlenir). Fiyat null ise null.</summary>
    public virtual Guid? SalePriceCurrencyUnitId { get; protected set; }

    /// <summary>Stok miktarı (marketplace quantity). Varsayılan 0.</summary>
    public virtual int StockQuantity { get; protected set; }

    protected ProductVariant() { }

    public ProductVariant(
        Guid companyId,
        Guid productId,
        string code,
        string name,
        bool isMain = false,
        bool isActive = true)
    {
        SetCompany(companyId);
        SetProduct(productId);
        SetCode(code);
        SetName(name);
        SetAsMain(isMain);
        SetActive(isActive);
    }

    // Kod DÜZENLENEBİLİR. Normalize + min/max StringFieldGuard'da; benzersizlik AppService'te (Product scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ProductConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ProductConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Satış fiyatı + para birimi (fiyat null → para birimi de null). Negatif fiyat geçersiz (fail-fast).</summary>
    public virtual void SetSalePrice(decimal? price, Guid? currencyUnitId)
    {
        if (price is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:Product:SalePriceNegative");
        }

        SalePrice = price;
        SalePriceCurrencyUnitId = price is null ? null : currencyUnitId;
    }

    /// <summary>Stok miktarı (negatif geçersiz).</summary>
    public virtual void SetStock(int quantity)
    {
        if (quantity < 0)
        {
            throw new BusinessException("TradeXpress:Product:StockNegative");
        }

        StockQuantity = quantity;
    }

    /// <summary>Main bayrağını değiştirir. Tekil-main değişmezi (diğerlerini düşür) <c>ProductVariantManager</c>'da.</summary>
    public virtual void SetAsMain(bool value)
    {
        IsMain = value;
    }

    // Şirket set-once → public mutator YOK; parent üründen denormalize (AppService/Manager geçer).
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    // Parent (ProductId) set-once → public mutator YOK.
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
        return Code;
    }
}
