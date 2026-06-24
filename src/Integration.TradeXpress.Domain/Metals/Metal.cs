using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Metal = bir <b>maden</b> (altın/gümüş/platin işlenmiş ürün/sikke) tanımı (katalog). Hurda'nın (<c>Scrap</c>)
/// üstüne <b>işçilik (labor)</b> ve <b>sikke/adet</b> takibi ekler. Bir ana birim (<see cref="FollowingUnitId"/>,
/// ZORUNLU; ör. HAS) + <see cref="Purity"/> (milyem; gram-altı ≤1, sikke birim-başı HAS-gram &gt;1) taşır.
///
/// <para>Host + tenant scoped (Scrap gibi): host kataloğu (TenantId=null) herkese görünür, tenant
/// düzenleyemez/silemez; tenant kendi kayıtlarını ekleyebilir.</para>
/// </summary>
public class Metal : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Metal()
    {
    }

    public Metal(
        string code,
        string name,
        Guid followingUnitId,
        decimal purity = MetalConsts.DefaultPurity,
        bool purityChange = false,
        bool isQuantity = false,
        decimal stableQuantity = 0m,
        MetalLaborType laborType = MetalLaborType.Amount,
        bool laborTypeChange = false,
        decimal entryLabor = 0m,
        Guid? entryLaborUnitId = null,
        bool entryLaborChange = false,
        decimal exitLabor = 0m,
        Guid? exitLaborUnitId = null,
        bool exitLaborChange = false,
        Guid? costUnitId = null,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        SetPurity(purity);
        PurityChange     = purityChange;
        IsQuantity       = isQuantity;
        StableQuantity   = stableQuantity;
        LaborType        = laborType;
        LaborTypeChange  = laborTypeChange;
        EntryLabor       = entryLabor;
        EntryLaborUnitId = entryLaborUnitId;
        EntryLaborChange = entryLaborChange;
        ExitLabor        = exitLabor;
        ExitLaborUnitId  = exitLaborUnitId;
        ExitLaborChange  = exitLaborChange;
        CostUnitId       = costUnitId;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }
    public virtual string? Barcode { get; protected set; }

    /// <summary>Madenin saf olarak dönüştüğü ana birim (FK, ZORUNLU; ör. HAS).</summary>
    public virtual Guid FollowingUnitId { get; protected set; }
    public virtual CurrencyUnit? FollowingUnit { get; protected set; }

    /// <summary>Milyem — gram-altı ≤1 (ör. 0.995), sikkede birim-başı HAS-gram &gt;1 (ör. 1.605). Yalnız pozitif.</summary>
    public virtual decimal Purity { get; protected set; }
    public virtual bool PurityChange { get; protected set; }

    /// <summary>Adet bazlı takip mi (sikke)?</summary>
    public virtual bool IsQuantity { get; protected set; }
    /// <summary>Adet başına sabit miktar (gram). IsQuantity + &gt;0 ise Miktar = Adet × StableQuantity.</summary>
    public virtual decimal StableQuantity { get; protected set; }

    // ── İşçilik ──
    public virtual MetalLaborType LaborType { get; protected set; }
    public virtual bool LaborTypeChange { get; protected set; }
    public virtual decimal EntryLabor { get; protected set; }
    public virtual Guid? EntryLaborUnitId { get; protected set; }
    public virtual bool EntryLaborChange { get; protected set; }
    public virtual decimal ExitLabor { get; protected set; }
    public virtual Guid? ExitLaborUnitId { get; protected set; }
    public virtual bool ExitLaborChange { get; protected set; }
    public virtual Guid? CostUnitId { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, MetalConsts.NameMaxLength);
    }

    public virtual void SetFollowingUnit(Guid followingUnitId)
    {
        if (followingUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(FollowingUnitId));
        }

        FollowingUnitId = followingUnitId;
    }

    public virtual void SetPurity(decimal value)
    {
        // Yalnız pozitif — üst sınır yok (sikkede milyem HAS-gram olarak >1 olabilir).
        if (value <= 0m)
        {
            throw new BusinessException("TradeXpress:Metal:PurityMustBePositive");
        }

        Purity = value;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, MetalConsts.DescriptionMaxLength);
    }

    public virtual void SetBarcode(string? barcode)
    {
        Barcode = StringFieldGuard.EnsureOptionalText(barcode, nameof(Barcode), 0, MetalConsts.BarcodeMaxLength);
    }

    public virtual void SetLabor(
        MetalLaborType laborType, bool laborTypeChange,
        decimal entryLabor, Guid? entryLaborUnitId, bool entryLaborChange,
        decimal exitLabor, Guid? exitLaborUnitId, bool exitLaborChange,
        Guid? costUnitId)
    {
        LaborType        = laborType;
        LaborTypeChange  = laborTypeChange;
        EntryLabor       = entryLabor;
        EntryLaborUnitId = entryLaborUnitId;
        EntryLaborChange = entryLaborChange;
        ExitLabor        = exitLabor;
        ExitLaborUnitId  = exitLaborUnitId;
        ExitLaborChange  = exitLaborChange;
        CostUnitId       = costUnitId;
    }

    public virtual void SetQuantityTracking(bool isQuantity, decimal stableQuantity)
    {
        IsQuantity     = isQuantity;
        StableQuantity = stableQuantity;
    }

    public virtual void SetPurityChange(bool value) => PurityChange = value;

    public virtual void SetActive(bool value) => IsActive = value;

    private void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, MetalConsts.CodeMaxLength);
    }

    #endregion
}
