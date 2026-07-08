namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 kanal-ürününde bir VARYANTIN <b>kanal-özel override başlığı</b> — ERP <see cref="Integration.TradeXpress.Products.ProductVariant"/>'ın
/// (fiyat/stok SSOT'u) N11-scope özelleştirmesi. null alan = ERP'den DEVRAL (kullanıcı kararı 2026-07-08).
/// <b>Company-owned</b> (<see cref="CompanyId"/> kanal-üründen denormalize) + per-tenant. Anchor:
/// <see cref="SalesChannelTrN11ProductId"/> + <see cref="ProductVariantId"/> (set-once). Kanal-özel reçete satırları
/// (<see cref="SalesChannelTrN11ProductVariantRecipeLine"/>) bu başlığa AYNI çift-anahtarla eşlenir (ayrı tablo; nav yok, id-only).
///
/// <para><b>Türetilmiş fiyat/NetCost PERSIST EDİLMEZ</b> — canlı hesaplanır (<c>ProductRecipeCostCalculator</c>):
/// türetilmiş fiyat = NetCost × (1 + <see cref="Margin"/>/100) [MARKUP]. <see cref="Margin"/> varyant-başı yüzde marj.
/// Push fiyat zinciri: <see cref="OverridePrice"/> ?? türetilmiş ?? ERP SalePrice; stok: <see cref="OverrideStock"/> ?? ERP StockQuantity.</para>
/// </summary>
public class SalesChannelTrN11ProductVariant : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductVariant()
    {
    }

    public SalesChannelTrN11ProductVariant(
        Guid companyId,
        Guid salesChannelTrN11ProductId,
        Guid productVariantId)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelTrN11ProductId);
        SetProductVariant(productVariantId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip N11 kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelTrN11ProductId { get; protected set; }

    /// <summary>Override'ın ait olduğu ERP varyantı — id-only referans. Set-once.</summary>
    public virtual Guid ProductVariantId { get; protected set; }

    /// <summary>Kanal-özel mutlak liste fiyatı (opsiyonel). null = ERP/türetilmiş fiyat devralınır.</summary>
    public virtual decimal? OverridePrice { get; protected set; }

    /// <summary>Override fiyatının para birimi (id-only, nav yok). Fiyat null ise null.</summary>
    public virtual Guid? OverridePriceCurrencyUnitId { get; protected set; }

    /// <summary>Kanal-özel stok miktarı (opsiyonel). null = ERP StockQuantity devralınır.</summary>
    public virtual int? OverrideStock { get; protected set; }

    /// <summary>Varyant-başı marj (markup yüzdesi; ör. 20 → türetilmiş = NetCost × 1.20). null = marj yok.</summary>
    public virtual decimal? Margin { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Kanal-özel mutlak fiyat + para birimi (fiyat null → para birimi de null). Negatif fiyat geçersiz (fail-fast).</summary>
    public virtual void SetOverridePrice(decimal? price, Guid? currencyUnitId)
    {
        if (price is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:N11:ProductVariant:OverridePriceNegative");
        }

        OverridePrice = price;
        OverridePriceCurrencyUnitId = price is null ? null : (currencyUnitId == Guid.Empty ? null : currencyUnitId);
    }

    /// <summary>Kanal-özel stok (opsiyonel; negatif geçersiz).</summary>
    public virtual void SetOverrideStock(int? stock)
    {
        if (stock is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:N11:ProductVariant:OverrideStockNegative");
        }

        OverrideStock = stock;
    }

    /// <summary>Varyant-başı marj (markup yüzdesi; opsiyonel). Negatif geçersiz (fail-fast).</summary>
    public virtual void SetMargin(decimal? margin)
    {
        if (margin is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:N11:ProductVariant:MarginNegative");
        }

        Margin = margin;
    }

    public override string ToString()
    {
        return $"{SalesChannelTrN11ProductId}/{ProductVariantId}";
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

    private void SetProductVariant(Guid productVariantId)
    {
        if (productVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductVariantId));
        }

        ProductVariantId = productVariantId;
    }

    #endregion
}
