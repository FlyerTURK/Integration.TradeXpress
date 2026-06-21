namespace Integration.TradeXpress.Companies;

/// <summary>
/// Bir tenant'ın bir ülkedeki şirketi (OrgScope'un üst seviyesi). <b>Base currency</b>'si
/// fonksiyonel/değerleme para birimidir: aktif şirket ABD ise USD=1 ve her şey USD cinsinden
/// değerlenir (parite panosu yönü DEĞİŞMEZ — base yalnız değerleme merceğidir).
///
/// <para>Per-tenant (IMultiTenant). Her tenant en az bir <see cref="IsHeadquarters"/> şirketle
/// doğar (onboarding); başka ülkelerde de şirket açabilir. Branch (alt seviye) sonraki increment.
/// <see cref="BaseCurrencyUnitId"/> global CurrencyUnit'e id-only referans (nav YOK).</para>
/// </summary>
public class Company : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>ISO-3166 alpha-2 ülke kodu (TR, US, ...). Fiyatlar bu ülkenin değil — pivot
    /// global; ülke yalnız kimlik/varsayılan base içindir.</summary>
    public virtual string CountryCode { get; protected set; } = null!;

    /// <summary>Değerleme (fonksiyonel) para birimi — global CurrencyUnit'e id-only referans.</summary>
    public virtual Guid BaseCurrencyUnitId { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Tenant'ın merkez (HQ) şirketi mi. Tenant başına tek HQ (AppService doğrular).</summary>
    public virtual bool IsHeadquarters { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }
    public virtual string? Description { get; protected set; }

    protected Company() { }

    public Company(
        string code,
        string name,
        string countryCode,
        Guid baseCurrencyUnitId,
        bool isHeadquarters = false,
        int displayOrder = 0,
        Guid? tenantId = null)
    {
        SetCode(code);
        SetName(name);
        SetCountryCode(countryCode);
        BaseCurrencyUnitId = baseCurrencyUnitId;
        IsHeadquarters = isHeadquarters;
        DisplayOrder = displayOrder;
        TenantId = tenantId;
        IsActive = true;
    }

    public virtual void SetCode(string code)
        => Code = Check.NotNullOrWhiteSpace(code, nameof(code), CompanyConsts.CodeMaxLength).ToUpperInvariant();

    public virtual void SetName(string name)
        => Name = Check.NotNullOrWhiteSpace(name, nameof(name), CompanyConsts.NameMaxLength);

    public virtual void SetCountryCode(string countryCode)
        => CountryCode = Check.NotNullOrWhiteSpace(countryCode, nameof(countryCode), CompanyConsts.CountryCodeMaxLength)
            .ToUpperInvariant();

    public virtual void SetBaseCurrency(Guid baseCurrencyUnitId)
    {
        if (baseCurrencyUnitId == Guid.Empty)
            throw new ArgumentException("Base currency unit is required.", nameof(baseCurrencyUnitId));
        BaseCurrencyUnitId = baseCurrencyUnitId;
    }

    public virtual void SetDescription(string? description)
    {
        if (description is { Length: > CompanyConsts.DescriptionMaxLength })
            throw new ArgumentException(
                $"Description length must be at most {CompanyConsts.DescriptionMaxLength}.", nameof(description));
        Description = description;
    }

    public virtual void Activate() => IsActive = true;
    public virtual void Deactivate() => IsActive = false;
    public virtual void SetAsHeadquarters(bool isHeadquarters) => IsHeadquarters = isHeadquarters;
    public virtual void SetDisplayOrder(int order) => DisplayOrder = order;
}
