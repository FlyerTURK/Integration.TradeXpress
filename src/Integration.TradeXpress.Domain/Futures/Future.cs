using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Futures;

/// <summary>
/// Future = bir <b>vadeli enstrüman</b> tanımı (katalog). Bir ana birimi (<see cref="FollowingUnitId"/>,
/// ZORUNLU) takip eder + bir <see cref="FollowingFactor"/> çarpanı taşır (milyem/lot/saflık; varsayılan 1, &gt;0).
/// Voucher/VoucherLine'da commodity olarak seçilir; ana bacak Total = Miktar × FollowingFactor.
///
/// <para>Host + tenant scoped (Cash gibi): host kataloğu (TenantId=null) herkese görünür, tenant
/// düzenleyemez/silemez; tenant kendi kayıtlarını ekleyebilir.</para>
/// </summary>
public class Future : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Future()
    {
    }

    public Future(
        string code,
        string name,
        Guid followingUnitId,
        decimal followingFactor = 1m,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        SetFollowingFactor(followingFactor);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;

    /// <summary>Takip edilen ana para birimi (FK, ZORUNLU) — ana bacağın cinsi.</summary>
    public virtual Guid FollowingUnitId { get; protected set; }

    /// <summary>Çarpan (milyem/lot/saflık) — Total = Miktar × FollowingFactor. Pozitif; varsayılan 1.</summary>
    public virtual decimal FollowingFactor { get; protected set; }

    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, FutureConsts.NameMaxLength);
    }

    public virtual void SetFollowingUnit(Guid followingUnitId)
    {
        if (followingUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(FollowingUnitId));
        }

        FollowingUnitId = followingUnitId;
    }

    public virtual void SetFollowingFactor(decimal value)
    {
        if (value <= 0m)
        {
            throw new BusinessException("TradeXpress:Future:FactorMustBePositive");
        }

        FollowingFactor = value;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, FutureConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return Code;
    }

    private void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, FutureConsts.CodeMaxLength);
    }

    #endregion
}
