using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Reçete şablonunun bir SATIRI — <see cref="ProductVariantRecipeLine"/>'ın kalıbı. Alan seti bilinçli olarak
/// reçete satırının AYNISIDIR: şablon uygulanırken alan alan çeviri değil DÜZ KOPYA yapılır, böylece iki
/// tarafın davranışı zamanla ayrışmaz (çeviri katmanı olsaydı yeni bir alan eklendiğinde sessizce düşerdi).
///
/// <para>İki tür satır taşır — reçetedeki gibi: <see cref="RecipeComponentType.CatalogCommodity"/> (yarı mamul /
/// katalog emtiası: kendi maliyetini ekler) ve <see cref="RecipeComponentType.Service"/> (hizmet: devralınan
/// tabanın üstüne türevsel bedel — paketleme, kargo, sigorta, işçilik…).</para>
///
/// <para><b>Türev kaynak seçimi (SelectedLines) TAŞINMAZ:</b> o mod satır kimliklerine referans verir ve
/// şablon satırının kimliği ürüne uygulandığında geçersizdir. Şablon satırları <see cref="RecipeDerivedBaseMode.AllAbove"/>
/// kullanır — "üstümdeki her şeyin toplamı" ürüne uygulandığında da doğru anlamı korur.</para>
/// </summary>
public class RecipeTemplateLine : FullAuditedEntity<Guid>
{
    #region Constructors

    protected RecipeTemplateLine()
    {
    }

    public RecipeTemplateLine(Guid templateId, RecipeComponentType componentType, int lineOrder)
    {
        TemplateId = templateId;
        ComponentType = componentType;
        PaymentType = ProcessPaymentType.Normal;
        SetOrder(lineOrder);
    }

    #endregion

    #region Properties

    /// <summary>Sahip şablon (aggregate içi FK; navigation YOK).</summary>
    public virtual Guid TemplateId { get; protected set; }

    public virtual int LineOrder { get; protected set; }

    /// <summary>Bileşen türü — katalog emtiası (yarı mamul) ya da hizmet.</summary>
    public virtual RecipeComponentType ComponentType { get; protected set; }

    // ── katalog-emtia satırı (yarı mamul) ──

    public virtual ProcessType? CommodityProcessType { get; protected set; }

    public virtual Guid? CommodityId { get; protected set; }

    public virtual Guid? CommodityVariantId { get; protected set; }

    public virtual decimal Quantity { get; protected set; }

    public virtual decimal Amount { get; protected set; }

    public virtual decimal Factor { get; protected set; }

    public virtual Guid? ValuationUnitId { get; protected set; }

    public virtual ProcessPaymentType PaymentType { get; protected set; }

    public virtual decimal PayFactor { get; protected set; }

    public virtual Guid? PayUnitId { get; protected set; }

    // ── hizmet satırı (türevsel bedel) ──

    public virtual RecipeDerivedBaseMode? DerivedBaseMode { get; protected set; }

    public virtual RecipeDerivedOperation? DerivedOperation { get; protected set; }

    public virtual decimal DerivedOperand { get; protected set; }

    /// <summary>Yan-maliyet türü (paketleme/kargo/sigorta…) — uygulanan satıra da kopyalanır; fiş hizalamasının
    /// ve kanal yan-maliyet reconcile'ının anahtarıdır.</summary>
    public virtual SideCostKind? SideCostKind { get; protected set; }

    public virtual string? Description { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Yarı mamul / katalog emtiası satırını atar — reçetedeki <c>SetCatalogCommodity</c> ile aynı alanlar.</summary>
    public virtual void SetCatalogCommodity(
        ProcessType family,
        Guid? commodityId,
        Guid? commodityVariantId,
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
        CommodityVariantId = commodityVariantId;
        Quantity = quantity;
        Amount = amount;
        Factor = factor;
        ValuationUnitId = valuationUnitId;
        PaymentType = paymentType;
        PayFactor = payFactor;
        PayUnitId = payUnitId;

        // Tür geçişinde artık değer kalmasın.
        DerivedBaseMode = null;
        DerivedOperation = null;
        DerivedOperand = 0m;
    }

    /// <summary>
    /// Hizmet satırını atar. Taban modu DAİMA <see cref="RecipeDerivedBaseMode.AllAbove"/>'dur — sebep sınıf
    /// özetinde: seçili-satır referansları ürüne uygulandığında geçersiz olurdu.
    /// GrossUp oranı reçetedeki AYNI sınıra tabidir (fail-fast; sessiz eksik-fiyatlama YOK).
    /// </summary>
    public virtual void SetService(
        Guid? serviceId,
        RecipeDerivedOperation operation,
        decimal operand,
        Guid? operandUnitId,
        SideCostKind? sideCostKind)
    {
        if (operation == RecipeDerivedOperation.GrossUp
            && (operand < 0m || operand >= ProductRecipeConsts.GrossUpOperandExclusiveMax))
        {
            throw new BusinessException("TradeXpress:ProductRecipeLine:GrossUpRateOutOfRange");
        }

        CommodityId = serviceId;
        DerivedBaseMode = RecipeDerivedBaseMode.AllAbove;
        DerivedOperation = operation;
        DerivedOperand = operand;
        PayUnitId = operandUnitId;
        SideCostKind = sideCostKind;

        // Katalog/fiziki alanları temizle.
        CommodityProcessType = null;
        CommodityVariantId = null;
        Quantity = 0m;
        Amount = 0m;
        Factor = 0m;
        ValuationUnitId = null;
        PaymentType = ProcessPaymentType.Normal;
        PayFactor = 0m;
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
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, RecipeTemplateConsts.LineDescriptionMaxLength);
    }

    public override string ToString()
    {
        return $"{ComponentType}#{LineOrder}";
    }

    #endregion
}
