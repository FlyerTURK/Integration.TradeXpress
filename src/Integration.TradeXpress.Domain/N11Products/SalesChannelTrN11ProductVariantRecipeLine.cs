using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 kanal-ürününde bir VARYANTIN <b>kanal-özel reçete satırı</b> — <see cref="ProductVariantRecipeLine"/>'ın
/// (ürün-geneli reçete) kanal-scope KLONU. İlk açılışta ERP varyant reçetesinden kopyalanır, sonra BAĞIMSIZ
/// düzenlenir (kullanıcı kararı 2026-07-08): kanal maliyeti + marj → N11 türetilmiş fiyatı. <b>Company-owned</b>
/// (<see cref="CompanyId"/> kanal-üründen denormalize) + per-tenant. Anchor: <see cref="SalesChannelTrN11ProductId"/>
/// + <see cref="ProductVariantId"/> (set-once). Net/tutar CANLI hesaplanır (<c>ProductRecipeCostCalculator</c>
/// AYNEN yeniden kullanılır — saf/DB'siz); katalog/StableQuantity/EntryPrice hesap anında canlı okunur.
/// Ürün reçetesiyle AYNI alan seti — hesap motoru ortak kalsın diye birebir hizalı.
/// </summary>
public class SalesChannelTrN11ProductVariantRecipeLine : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductVariantRecipeLine()
    {
    }

    public SalesChannelTrN11ProductVariantRecipeLine(
        Guid companyId,
        Guid salesChannelTrN11ProductId,
        Guid productVariantId,
        RecipeComponentType componentType,
        int lineOrder)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelTrN11ProductId);
        SetProductVariant(productVariantId);
        ComponentType = componentType;
        PaymentType = ProcessPaymentType.Normal;
        SetOrder(lineOrder);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip N11 kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelTrN11ProductId { get; protected set; }

    /// <summary>Reçetenin ait olduğu ERP varyantı — id-only referans (kanal-üründeki SKU ile aynı varyant). Set-once.</summary>
    public virtual Guid ProductVariantId { get; protected set; }

    /// <summary>Satır sırası (türev-satır referans sırası). Kullanıcı sıralaması korunur.</summary>
    public virtual int LineOrder { get; protected set; }

    /// <summary>Bileşen türü — set-once (katalog emtiası / hizmet).</summary>
    public virtual RecipeComponentType ComponentType { get; protected set; }

    /// <summary>Katalog emtia ailesi (Metal/Scrap/Future/Jewelry/Stone). Yalnız CatalogCommodity için dolu.</summary>
    public virtual ProcessType? CommodityProcessType { get; protected set; }

    /// <summary>Katalog kaydı (FK'sız snapshot). Manuel/hizmet etiketinde null olabilir.</summary>
    public virtual Guid? CommodityId { get; protected set; }

    /// <summary>Adet. Adet→gram: <c>Amount = Quantity × StableQuantity</c> (katalogtan canlı).</summary>
    public virtual decimal Quantity { get; protected set; }

    /// <summary>Miktar (gram).</summary>
    public virtual decimal Amount { get; protected set; }

    /// <summary>Milyem/faktör — ailenin çarpanının düzenlenebilir snapshot'ı.</summary>
    public virtual decimal Factor { get; protected set; }

    /// <summary>Doğal-birim (rebase kaynağı) snapshot'ı — metal-bacaklıda FollowingUnit, parasalda EntryPrice birimi.</summary>
    public virtual Guid? ValuationUnitId { get; protected set; }

    /// <summary>Ödeme tipi — Normal (metal + işçilik) / WithCurrency (bedelli, tek bacak). Varsayılan Normal.</summary>
    public virtual ProcessPaymentType PaymentType { get; protected set; }

    /// <summary>Karşı bacak birim fiyatı (N5) — Normal'de işçilik rate, Bedelli'de bedel. Türetilmiş → persist edilmez.</summary>
    public virtual decimal PayFactor { get; protected set; }

    /// <summary>Karşı bacak birimi (işçilik/bedel para birimi) — snapshot.</summary>
    public virtual Guid? PayUnitId { get; protected set; }

    /// <summary>Hizmet/manuel satırın sabit tutarı.</summary>
    public virtual decimal? ManualAmount { get; protected set; }

    /// <summary>Hizmet/manuel tutarının birimi.</summary>
    public virtual Guid? ManualUnitId { get; protected set; }

    public virtual string? Description { get; protected set; }

    // ── türev/devralan satır — yalnız Service'de anlamlı ──

    public virtual RecipeDerivedBaseMode? DerivedBaseMode { get; protected set; }

    public virtual RecipeDerivedOperation? DerivedOperation { get; protected set; }

    public virtual decimal DerivedOperand { get; protected set; }

    /// <summary>SelectedLines kaynak satır Id'lerinin '|'-join CSV snapshot'ı (aynı kanal-reçetesi kardeşlerine
    /// gevşek referans; id-only). AllAbove ve türev-dışı satırda null.</summary>
    public virtual string? DerivedSourceLineIds { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Katalog-emtia satırının alanlarını atar (ürün reçetesi ile birebir; hesap motoru ortak).</summary>
    public virtual void SetCatalogCommodity(
        ProcessType family,
        Guid? commodityId,
        decimal quantity,
        decimal amount,
        decimal factor,
        Guid? valuationUnitId,
        ProcessPaymentType paymentType,
        decimal payFactor,
        Guid? payUnitId)
    {
        CommodityProcessType = family;
        CommodityId = commodityId;
        Quantity = quantity;
        Amount = amount;
        Factor = factor;
        ValuationUnitId = valuationUnitId;
        PaymentType = paymentType;
        PayFactor = payFactor;
        PayUnitId = payUnitId;
        ManualAmount = null;
        ManualUnitId = null;
        ClearDerived();
    }

    /// <summary>Hizmet/türev satırını atar (taban modu + işlem + operand). GrossUp operand'ı [0,100) dışıysa fail-fast.</summary>
    public virtual void SetService(
        Guid? commodityId, RecipeDerivedBaseMode baseMode, RecipeDerivedOperation operation, decimal operand,
        Guid? operandUnitId)
    {
        if (operation == RecipeDerivedOperation.GrossUp
            && (operand < 0m || operand >= ProductRecipeConsts.GrossUpOperandExclusiveMax))
        {
            throw new BusinessException("TradeXpress:ProductRecipeLine:GrossUpRateOutOfRange");
        }

        CommodityId = commodityId;
        DerivedBaseMode = baseMode;
        DerivedOperation = operation;
        DerivedOperand = operand;
        DerivedSourceLineIds = null;
        PayUnitId = operandUnitId;

        CommodityProcessType = null;
        Quantity = 0m;
        Amount = 0m;
        Factor = 0m;
        ValuationUnitId = null;
        PaymentType = ProcessPaymentType.Normal;
        PayFactor = 0m;
        ManualAmount = null;
        ManualUnitId = null;
    }

    /// <summary>SelectedLines kaynak Id CSV'sini atar (iki-geçişli save'in 2. geçişi). SelectedLines'ta boş kaynak fail-fast.</summary>
    public virtual void SetDerivedSources(string? sourceLineIdsCsv)
    {
        if (DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines && string.IsNullOrWhiteSpace(sourceLineIdsCsv))
        {
            throw new BusinessException("TradeXpress:ProductRecipeLine:DerivedNeedsSelection");
        }

        DerivedSourceLineIds = DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines ? sourceLineIdsCsv : null;
    }

    public virtual void SetOrder(int lineOrder)
    {
        if (lineOrder < 0)
        {
            throw new BusinessException("TradeXpress:ProductRecipeLine:OrderMustBeNonNegative");
        }

        LineOrder = lineOrder;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ProductRecipeConsts.DescriptionMaxLength);
    }

    public override string ToString()
    {
        return $"{ComponentType}#{LineOrder}";
    }

    private void ClearDerived()
    {
        DerivedBaseMode = null;
        DerivedOperation = null;
        DerivedOperand = 0m;
        DerivedSourceLineIds = null;
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
