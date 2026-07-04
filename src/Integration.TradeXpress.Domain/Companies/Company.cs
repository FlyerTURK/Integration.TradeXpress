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

    /// <summary>Ülke — <see cref="Countries.Country"/>'ye id-only referans (nav YOK). OTORİTER alan;
    /// legacy satırlarda backfill tamamlanana dek null olabilir (yeni kayıtta zorunlu, ctor doğrular).</summary>
    public virtual Guid? CountryId { get; protected set; }

    /// <summary>ISO-3166 alpha-2 ülke kodu (TR, US, ...) — ESKİ string referans. Country id-only geçişiyle
    /// yerini <see cref="CountryId"/> aldı; yalnız backfill (kod→id eşleştirme) kaynağıdır, yeni kod yolu yazmaz.</summary>
    [Obsolete("Country id-only geçişi; backfill sonrası kaldırılacak — CountryId kullan.")]
    public virtual string? CountryCode { get; protected set; }

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
        Guid countryId,
        Guid baseCurrencyUnitId,
        bool isHeadquarters = false,
        int displayOrder = 0)
    {
        SetCode(code);
        SetName(name);
        SetCountry(countryId);
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

    public virtual void SetCountry(Guid countryId)
    {
        if (countryId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Company:CountryRequired");
        }

        CountryId = countryId;
    }

    /// <summary>Geçiş backfill'i: yalnız <see cref="CountryId"/> boşsa doldurur (idempotent;
    /// dolu satıra dokunmaz — CompanyOwnedBackfiller deseniyle hizalı).</summary>
    public virtual void BackfillCountryIfMissing(Guid countryId)
    {
        if (CountryId == null)
        {
            SetCountry(countryId);
        }
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
