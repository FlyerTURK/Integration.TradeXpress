namespace Integration.TradeXpress.Cashes;

/// <summary>
/// Cash = işaretçi (pointer) bir <b>emtia</b> tipi. Banknot/Kasa/Money DEĞİL; hiçbir stok/bakiye bilgisi
/// taşımaz. İleride Voucher/VoucherLine'da cari hesap işlemlerinde "hangi nakit kaydı ile işlem yapılacağını"
/// belirten <b>commodity</b> olarak seçilir. Her Cash bir <see cref="CurrencyUnit"/>'i takip eder
/// (<see cref="FollowingUnitId"/>, ZORUNLU) — bu, nakit kaydının cinsidir.
///
/// <para>Host + tenant scoped (CurrencyUnit gibi): host kataloğu (TenantId=null) HERKESE görünür ama tenant
/// tarafından düzenlenemez/silinemez; tenant kendi Cash kayıtlarını ekleyebilir. "System" saklanmaz —
/// TenantId==null zaten global'dir (DTO'da hesaplanır, koruma AppService'te).</para>
/// </summary>
public class Cash : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Cash()
    {
    }

    public Cash(
        string code,
        string name,
        Guid followingUnitId,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;

    /// <summary>Takip edilen para birimi — nakit kaydının cinsi (FK, ZORUNLU).</summary>
    public virtual Guid FollowingUnitId { get; protected set; }


    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            CashConsts.NameMaxLength);
    }

    /// <summary>Takip edilen para birimini ayarlar. Boş Guid kabul edilmez (zorunlu referans).</summary>
    public virtual void SetFollowingUnit(Guid followingUnitId)
    {
        if (followingUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(FollowingUnitId));
        }

        FollowingUnitId = followingUnitId;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description,
            nameof(Description),
            EntityFieldConsts.DescriptionMinLength,
            CashConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return Code;
    }

    // Code immutable (public mutator YOK) → yalnız ctor için private normalize+validate.
    private void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            CashConsts.CodeMaxLength);
    }

    #endregion
}
