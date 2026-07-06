using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Bir <see cref="ProductVariant"/>'ın <b>reçete satırı</b> — varyantın design-time maliyetini oluşturan tek
/// bileşen (VoucherLine alan setinden ESİNLENİR ama AYRI entity: <b>LEDGER'A YAZMAZ</b>, sadece maliyet çıkarır).
/// <b>Company-owned</b> (<see cref="ICompanyOwned"/>; <see cref="CompanyId"/> varyanttan DENORMALİZE — güvenlik
/// sınırı) + per-tenant. Varyant bağı (<see cref="ProductVariantId"/>) ve bileşen türü
/// (<see cref="ComponentType"/>) set-once.
///
/// <para><b>Net/tutar alanı YOK</b> — satır tutarı ve net maliyet CANLI hesaplanır
/// (<c>ProductRecipeCostCalculator</c>): kur değişince maliyet güncellenir, dondurulmaz. Katalog referansı
/// (<see cref="CommodityId"/>) FK'sız <b>snapshot</b>'tır; adet→gram çevirimi (StableQuantity) ve parasal
/// giriş fiyatı (EntryPrice) hesap anında katalogtan CANLI okunur. Ailenin milyemi/faktörü ise
/// <see cref="Factor"/> alanında düzenlenebilir snapshot olarak tutulur (milyem fiziksel özellik, canlı olan kur).</para>
/// </summary>
public class ProductVariantRecipeLine : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ProductVariantRecipeLine()
    {
    }

    public ProductVariantRecipeLine(
        Guid companyId,
        Guid productVariantId,
        RecipeComponentType componentType,
        int lineOrder)
    {
        SetCompany(companyId);
        SetProductVariant(productVariantId);
        ComponentType = componentType;
        PaymentType = ProcessPaymentType.Normal;
        SetOrder(lineOrder);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — varyanttan denormalize (güvenlik sınırı). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip varyant — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid ProductVariantId { get; protected set; }

    /// <summary>Satır sırası (türev-satır referans sırası için; 3b). Kullanıcı sıralaması korunur.</summary>
    public virtual int LineOrder { get; protected set; }

    /// <summary>Bileşen türü — set-once (katalog emtiası / hizmet / manuel).</summary>
    public virtual RecipeComponentType ComponentType { get; protected set; }

    /// <summary>Katalog emtia ailesi (Metal/Scrap/Future/Jewelry/Stone). Yalnız <see cref="RecipeComponentType.CatalogCommodity"/>
    /// için dolu; hizmet/manuelde null.</summary>
    public virtual ProcessType? CommodityProcessType { get; protected set; }

    /// <summary>Katalog kaydı (Metal/Scrap/Future/Jewelry/Stone ya da hizmet) — FK'sız <b>snapshot</b>. Manuel maliyette null.</summary>
    public virtual Guid? CommodityId { get; protected set; }

    /// <summary>Adet (sikke/parça bazlı emtiada). Adet→gram: <c>Amount = Quantity × StableQuantity</c> (katalog).</summary>
    public virtual decimal Quantity { get; protected set; }

    /// <summary>Miktar (gram). Gramlı emtiada kullanıcı girer; adetli emtiada adetten türetilir.</summary>
    public virtual decimal Amount { get; protected set; }

    /// <summary>Milyem/faktör — ailenin çarpanının düzenlenebilir snapshot'ı (Metal.Factor / Scrap.Factor / Future.FollowingFactor).</summary>
    public virtual decimal Factor { get; protected set; }

    /// <summary>Doğal-birim (rebase kaynağı) snapshot'ı — metal-bacaklıda katalog FollowingUnit (HAS vb.) =
    /// VoucherLine.MainUnitId rolü; parasalda giriş fiyatının birimi. Hizmet/manuelde null (birim <see cref="ManualUnitId"/>).</summary>
    public virtual Guid? ValuationUnitId { get; protected set; }

    /// <summary>Ödeme tipi (VoucherLine paritesi) — reçetede yalnız Normal (metal + işçilik bacağı) ve
    /// WithCurrency/Bedelli (sabit bedel = tek bacak) anlamlıdır. Varsayılan Normal.</summary>
    public virtual ProcessPaymentType PaymentType { get; protected set; }

    /// <summary>Karşı bacak birim fiyatı (N5) — Normal'de işçilik rate'i (adet/miktar başına),
    /// Bedelli'de 1 ana-birim başına bedel. Total/PayTotal TÜRETİLMİŞ → persist edilmez (canlı-maliyet).</summary>
    public virtual decimal PayFactor { get; protected set; }

    /// <summary>Karşı bacak birimi (işçilik/bedel para birimi) — snapshot, FK yok.</summary>
    public virtual Guid? PayUnitId { get; protected set; }

    /// <summary>Hizmet/manuel satırın sabit tutarı.</summary>
    public virtual decimal? ManualAmount { get; protected set; }

    /// <summary>Hizmet/manuel tutarının birimi (para/metal birimi).</summary>
    public virtual Guid? ManualUnitId { get; protected set; }

    public virtual string? Description { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Katalog-emtia satırının alanlarını atar (Metal/Scrap/Future/Jewelry/Stone). Doğal birim
    /// (<paramref name="valuationUnitId"/>) = metal-bacaklıda FollowingUnit, parasalda EntryPrice birimi.</summary>
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
        CommodityId          = commodityId;
        Quantity             = quantity;
        Amount               = amount;
        Factor               = factor;
        ValuationUnitId      = valuationUnitId;
        PaymentType          = paymentType;
        PayFactor            = payFactor;
        PayUnitId            = payUnitId;
        // Hizmet/manuel alanları temizle (tür geçişinde artık değer kalmasın).
        ManualAmount = null;
        ManualUnitId = null;
    }

    /// <summary>Hizmet ya da manuel-maliyet satırının sabit tutar@birimini atar. Hizmette
    /// <paramref name="commodityId"/> opsiyonel hizmet katalog referansı; manuelde null.</summary>
    public virtual void SetServiceOrManual(Guid? commodityId, decimal manualAmount, Guid? manualUnitId)
    {
        CommodityId  = commodityId;
        ManualAmount = manualAmount;
        ManualUnitId = manualUnitId;
        // Katalog-emtia alanlarını temizle.
        CommodityProcessType = null;
        Quantity        = 0m;
        Amount          = 0m;
        Factor          = 0m;
        ValuationUnitId = null;
        PaymentType     = ProcessPaymentType.Normal;
        PayFactor       = 0m;
        PayUnitId       = null;
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

    // Şirket set-once → public mutator YOK; varyanttan denormalize (AppService geçer).
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    // Varyant bağı set-once → public mutator YOK.
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
