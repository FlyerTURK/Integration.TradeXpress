using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Accounts;

/// <summary>
/// Cari/defter HESABI — <b>company-scoped</b> (bir <see cref="Companies.Company"/>'ye ait), per-tenant
/// (IMultiTenant). Bakiyenin tutulduğu cins <see cref="BalanceCurrencyUnit"/> (ZORUNLU); kredi/risk
/// <see cref="Limit"/> ayrı bir birimde tutulur (<see cref="LimitUnit"/>, ZORUNLU — varsayılan şirketin
/// bilanço/base birimi). Alt hesaplar (<see cref="SubAccount"/>) branch-scoped olarak bu hesaba bağlanır.
///
/// <para>Aggregate sınırı: Company ve para birimleri kullanıcı talebiyle navigation property ile tutulur
/// (BalanceCurrencyUnit/LimitUnit); CurrencyUnit host+tenant scoped olduğundan AppService doğrulama/zenginleştirmede
/// DataFilter.Disable kullanır.</para>
/// </summary>
public class Account : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Account()
    {
    }

    public Account(
        Guid companyId,
        string code,
        string name,
        Guid balanceCurrencyUnitId,
        Guid limitUnitId,
        decimal limit = 0m,
        bool isActive = true)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        SetBalanceCurrencyUnit(balanceCurrencyUnitId);
        SetLimitUnit(limitUnitId);
        SetLimit(limit);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (company-scoped). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;

    /// <summary>Bakiye cinsi (para birimi) — ZORUNLU.</summary>
    public virtual Guid BalanceCurrencyUnitId { get; protected set; }

    /// <summary>Kredi/risk limiti (decimal n2).</summary>
    public virtual decimal Limit { get; protected set; }

    /// <summary>Limit birimi — ZORUNLU (varsayılan: şirketin bilanço/base birimi).</summary>
    public virtual Guid LimitUnitId { get; protected set; }

    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, AccountConsts.NameMaxLength);
    }

    public virtual void SetBalanceCurrencyUnit(Guid balanceCurrencyUnitId)
    {
        if (balanceCurrencyUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(BalanceCurrencyUnitId));
        }

        BalanceCurrencyUnitId = balanceCurrencyUnitId;
    }

    public virtual void SetLimit(decimal limit)
    {
        Limit = limit;
    }

    public virtual void SetLimitUnit(Guid limitUnitId)
    {
        if (limitUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(LimitUnitId));
        }

        LimitUnitId = limitUnitId;
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

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
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

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04: host CurrencyUnit kayıtları dışında tüm entity kodları
    // değiştirilebilir). Normalize + min/max recheck StringFieldGuard'da; benzersizlik kontrolü AppService'te
    // (TenantId+CompanyId scope — DB unique index ile hizalı).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, AccountConsts.CodeMaxLength);
    }

    #endregion
}
