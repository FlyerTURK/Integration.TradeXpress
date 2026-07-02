namespace Integration.TradeXpress.Accounts;

/// <summary>
/// Alt hesap — bir <see cref="Account"/>'a bağlı (AccountId, ZORUNLU, oluşturmadan sonra değişmez),
/// <b>branch-scoped</b> (BranchId, OPSİYONEL — null olabilir). <see cref="BranchId"/> oluşturmadan sonra
/// DEĞİŞMEZ; null olarak kaydedilmiş alt hesabın bile şubesi sonradan atanamaz/değiştirilemez (set-once).
/// Per-tenant. Şu an standart kimlik alanları yeterli (Code/Name/Description/IsActive); bakiye/limit ileride.
/// Parent/branch id-only referans (nav YOK).
/// </summary>
public class SubAccount : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected SubAccount()
    {
    }

    public SubAccount(
        Guid accountId,
        Guid? branchId,
        string code,
        string name,
        bool isActive = true)
    {
        SetAccount(accountId);
        SetBranch(branchId);
        SetCode(code);
        SetName(name);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Üst hesap — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid AccountId { get; protected set; }

    /// <summary>Sahip şube — id-only referans (branch-scoped), OPSİYONEL. Oluşturmadan sonra değişmez (null dahil).</summary>
    public virtual Guid? BranchId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, AccountConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, AccountConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    // Parent (AccountId) set-once → public mutator YOK.
    private void SetAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(AccountId));
        }

        AccountId = accountId;
    }

    // Branch set-once → public mutator YOK (oluşturmada sabit; null dahil sonradan değiştirilemez).
    // Boş Guid'i normalize et: null kabul edilir ama Guid.Empty değil (anlamsız FK).
    private void SetBranch(Guid? branchId)
    {
        BranchId = (branchId is { } b && b != Guid.Empty) ? b : null;
    }

    public override string ToString()
    {
        return Code;
    }

    // Code immutable → yalnız ctor.
    private void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, AccountConsts.CodeMaxLength);
    }

    #endregion
}
