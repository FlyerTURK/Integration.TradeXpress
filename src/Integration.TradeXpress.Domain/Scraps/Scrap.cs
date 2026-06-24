using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Scraps;

/// <summary>
/// Scrap = bir <b>hurda maden</b> tanımı (katalog). Bir ana birimi (<see cref="FollowingUnitId"/>, ZORUNLU;
/// hurdanın saflaştığı birim, ör. HAS) takip eder + bir <see cref="Purity"/> (milyem/saflık, 0..1) taşır.
/// VoucherLine'da commodity olarak seçilir; Has = Miktar × Purity (ana bacak).
/// <see cref="PurityChange"/> fişte milyemin editlenebilirliğini belirler.
///
/// <para>Host + tenant scoped (Future gibi): host kataloğu (TenantId=null) herkese görünür, tenant
/// düzenleyemez/silemez; tenant kendi kayıtlarını ekleyebilir.</para>
/// </summary>
public class Scrap : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Scrap()
    {
    }

    public Scrap(
        string code,
        string name,
        Guid followingUnitId,
        decimal purity = ScrapConsts.DefaultPurity,
        bool purityChange = true,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        SetPurity(purity);
        PurityChange = purityChange;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;

    /// <summary>Takip edilen ana para birimi (FK, ZORUNLU) — hurdanın saflaştığı birim (ör. HAS).</summary>
    public virtual Guid FollowingUnitId { get; protected set; }
    public virtual CurrencyUnit? FollowingUnit { get; protected set; }

    /// <summary>Saflık/milyem (0..1) — Has = Miktar × Purity. Varsayılan 0.570.</summary>
    public virtual decimal Purity { get; protected set; }

    /// <summary>Milyem fişte editlenebilir mi? false=kilitli, true=serbest.</summary>
    public virtual bool PurityChange { get; protected set; }

    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ScrapConsts.NameMaxLength);
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
        if (value <= 0m || value > 1m)
        {
            throw new BusinessException("TradeXpress:Scrap:PurityOutOfRange");
        }

        Purity = value;
    }

    public virtual void SetPurityChange(bool value)
    {
        PurityChange = value;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ScrapConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    private void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, ScrapConsts.CodeMaxLength);
    }

    #endregion
}
