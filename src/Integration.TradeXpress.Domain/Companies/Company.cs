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
        int displayOrder = 0)
    {
        SetCode(code);
        SetName(name);
        SetCountryCode(countryCode);
        BaseCurrencyUnitId = baseCurrencyUnitId;
        IsHeadquarters = isHeadquarters;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public virtual void SetCode(string code)
    {
        // NormalizeCode: Trim + çoklu boşluk→tek + boşluk→'_' + UPPER, ardından zorunlu/min/max doğrulaması.
        // Elle .ToUpperInvariant() gerekmez (NormalizeCode zaten UPPER yapar).
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            CompanyConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase, ardından zorunlu/min/max doğrulaması.
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            CompanyConsts.NameMaxLength);
    }

    public virtual void SetCountryCode(string countryCode)
    {
        // ISO-3166 alpha-2 sabit uzunluk (min = max = 2). Kültür-BAĞIMSIZ UPPER (tr-TR 'i'→'İ' tuzağı yok);
        // NormalizeCode KULLANILMAZ (evrensel CodeMinLength=3 iki harfli ISO koduna uymaz).
        CountryCode = StringFieldGuard.NormalizeInvariantCode(
            countryCode,
            nameof(CountryCode),
            CompanyConsts.CountryCodeMaxLength,
            CompanyConsts.CountryCodeMaxLength);
    }

    public virtual void SetBaseCurrency(Guid baseCurrencyUnitId)
    {
        if (baseCurrencyUnitId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Company:BaseCurrencyRequired");
        }

        BaseCurrencyUnitId = baseCurrencyUnitId;
    }

    public virtual void SetDescription(string? description)
    {
        // Opsiyonel alan: yalnız üst sınır (min yok — mevcut davranış korunur). Aşılırsa tipli Framework exception'ı.
        if (description is { Length: > CompanyConsts.DescriptionMaxLength })
        {
            throw new TooLongPropertyException(nameof(Description), CompanyConsts.DescriptionMaxLength);
        }

        Description = description;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetAsHeadquarters(bool isHeadquarters)
    {
        IsHeadquarters = isHeadquarters;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Code;
    }
}
