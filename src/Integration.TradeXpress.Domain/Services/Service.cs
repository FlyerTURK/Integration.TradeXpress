namespace Integration.TradeXpress.Services;

/// <summary>
/// Service = bir <b>hizmet</b> (işçilik/rafinaj/komisyon gibi gider-gelir) tanımı. Stok/bakiye taşımaz;
/// Voucher/VoucherLine'da <b>commodity</b> olarak seçilir. Birim (para birimi) işlem anında belirlenir
/// (Normal → seçili para birimi, Peşin → kasanın FollowingUnit'i) — entity'de birim YOKTUR.
///
/// <para>Host + tenant scoped (Cash gibi): host kataloğu (TenantId=null) herkese görünür, tenant
/// düzenleyemez/silemez; tenant kendi kayıtlarını ekleyebilir.</para>
/// </summary>
public class Service : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Service()
    {
    }

    public Service(
        string code,
        string name,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
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
            ServiceConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description,
            nameof(Description),
            EntityFieldConsts.DescriptionMinLength,
            ServiceConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    // Code immutable (public mutator YOK) → yalnız ctor için private normalize+validate.
    private void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            ServiceConsts.CodeMaxLength);
    }

    #endregion
}
