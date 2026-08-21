namespace Integration.TradeXpress.Services;

/// <summary>
/// Service = bir <b>hizmet</b> (işçilik/rafinaj/komisyon gibi gider-gelir) tanımı. Stok/bakiye taşımaz;
/// Voucher/VoucherLine'da <b>commodity</b> olarak seçilir. Birim (para birimi) işlem anında belirlenir
/// (Normal → seçili para birimi, Peşin → kasanın FollowingUnit'i) — entity'de birim YOKTUR.
///
/// <para><b>Şirkete AİTTİR</b> (<see cref="ICompanyOwned"/> — güvenlik sınırı, görev #4): katalog tenant-geneli
/// DEĞİL şirket kapsamlıdır; bir şirketin kullanıcısının düzenlemesi kardeş şirketleri etkilemez.
/// <see cref="CompanyId"/> ZORUNLU — sahipsiz ("holding") kayıt üretilemez; sahiplik client'tan değil aktif
/// working company'den <c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c> ile yazılır.</para>
/// </summary>
public class Service : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Service()
    {
    }

    public Service(
        string code,
        string name,
        Guid companyId,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        CompanyId = companyId;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — GÜVENLİK SINIRI (ICompanyOwned, ZORUNLU). Görev #4 kararı (Hakan): Hizmet EMTİA
    /// sayılır (VoucherLine + ServiceBalancePoster kanıtı) → per-company. Eskiden tenant-geneliydi.</summary>
    public virtual Guid CompanyId { get; protected set; }
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

    /// <summary>Tek seferlik geçiş backfill'i (migration sonrası): <see cref="CompanyId"/> yalnız BOŞSA
    /// doldurulur. Emtianın SubAccount/Vault gibi bir PARENT'ı YOKTUR (sahibi kanıtlayan yapısal bağ yok) →
    /// sahip POLİTİKA ile seçilir: tenant'ın merkez (HQ) şirketi (bkz. <c>CompanyOwnedBackfiller</c>).
    /// Zaten doluysa DOKUNMAZ (idempotent no-op; set-once invariant korunur — Empty→değer geçişi mümkün,
    /// yeniden atama DEĞİL).</summary>
    public virtual void BackfillCompanyIfMissing(Guid companyId)
    {
        if (CompanyId != Guid.Empty)
        {
            return;
        }

        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            ServiceConsts.CodeMaxLength);
    }

    #endregion
}
