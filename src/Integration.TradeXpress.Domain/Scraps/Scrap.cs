using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Scraps;

/// <summary>
/// Scrap = bir <b>hurda maden</b> tanımı (katalog). Bir ana birimi (<see cref="FollowingUnitId"/>, ZORUNLU;
/// hurdanın saflaştığı birim, ör. HAS) takip eder + bir <see cref="Factor"/> (milyem/saflık, 0..1) taşır.
/// VoucherLine'da commodity olarak seçilir; Has = Miktar × Factor (ana bacak).
/// <see cref="FactorChange"/> fişte milyemin editlenebilirliğini belirler.
///
/// <para><b>Şirkete AİTTİR</b> (<see cref="ICompanyOwned"/> — güvenlik sınırı, görev #4): katalog tenant-geneli
/// DEĞİL şirket kapsamlıdır; bir şirketin kullanıcısının düzenlemesi kardeş şirketleri etkilemez.
/// <see cref="CompanyId"/> ZORUNLU — sahipsiz ("holding") kayıt üretilemez; sahiplik client'tan değil aktif
/// working company'den <c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c> ile yazılır.</para>
/// </summary>
public class Scrap : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Scrap()
    {
    }

    public Scrap(
        string code,
        string name,
        Guid followingUnitId,
        Guid companyId,
        decimal factor = ScrapConsts.DefaultFactor,
        bool factorChange = true,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        CompanyId = companyId;
        SetFactor(factor);
        FactorChange = factorChange;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — GÜVENLİK SINIRI (ICompanyOwned, ZORUNLU). Görev #4 ile eklendi: hurda kataloğu
    /// eskiden TENANT-GENELİydi, yani bir şirketin kullanıcısının düzenlemesi kardeş şirketleri de etkiliyordu
    /// (cross-company manipülasyon). Artık her şirket kendi kataloğuna sahiptir.</summary>
    public virtual Guid CompanyId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;

    /// <summary>Takip edilen ana para birimi (FK, ZORUNLU) — hurdanın saflaştığı birim (ör. HAS).</summary>
    public virtual Guid FollowingUnitId { get; protected set; }

    /// <summary>Saflık/milyem (0..1) — Has = Miktar × Factor. Varsayılan 0.570.</summary>
    public virtual decimal Factor { get; protected set; }

    /// <summary>Milyem fişte editlenebilir mi? false=kilitli, true=serbest.</summary>
    public virtual bool FactorChange { get; protected set; }

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

    public virtual void SetFactor(decimal value)
    {
        if (value <= 0m || value > 1m)
        {
            throw new BusinessException("TradeXpress:Scrap:FactorOutOfRange");
        }

        Factor = value;
    }

    public virtual void SetFactorChange(bool value)
    {
        FactorChange = value;
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
            code, nameof(Code), EntityFieldConsts.CodeMinLength, ScrapConsts.CodeMaxLength);
    }

    #endregion
}
