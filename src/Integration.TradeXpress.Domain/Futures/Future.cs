using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Futures;

/// <summary>
/// Future = bir <b>vadeli enstrüman</b> tanımı (katalog). Bir ana birimi (<see cref="FollowingUnitId"/>,
/// ZORUNLU) takip eder + bir <see cref="FollowingFactor"/> çarpanı taşır (milyem/lot/saflık; varsayılan 1, &gt;0).
/// Voucher/VoucherLine'da commodity olarak seçilir; ana bacak Total = Miktar × FollowingFactor.
///
/// <para><b>Şirkete AİTTİR</b> (<see cref="ICompanyOwned"/> — güvenlik sınırı, görev #4): katalog tenant-geneli
/// DEĞİL şirket kapsamlıdır; bir şirketin kullanıcısının düzenlemesi kardeş şirketleri etkilemez.
/// <see cref="CompanyId"/> ZORUNLU — sahipsiz ("holding") kayıt üretilemez; sahiplik client'tan değil aktif
/// working company'den <c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c> ile yazılır.</para>
/// </summary>
public class Future : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Future()
    {
    }

    public Future(
        string code,
        string name,
        Guid followingUnitId,
        Guid companyId,
        decimal followingFactor = 1m,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        CompanyId = companyId;
        SetFollowingFactor(followingFactor);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — GÜVENLİK SINIRI (ICompanyOwned, ZORUNLU). Görev #4 ile eklendi: vadeli kataloğu
    /// eskiden TENANT-GENELİydi (bir şirketin düzenlemesi kardeş şirketleri etkiliyordu).</summary>
    public virtual Guid CompanyId { get; protected set; }
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
            code, nameof(Code), EntityFieldConsts.CodeMinLength, FutureConsts.CodeMaxLength);
    }

    #endregion
}
