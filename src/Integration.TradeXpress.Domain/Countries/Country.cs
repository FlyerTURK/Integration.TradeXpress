namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu — merkezi referans verisi (host yönetir, tenant'lar seçer). Tenant'ın merkez
/// (HQ) şirketi bu katalogdan ülke seçer; <see cref="DefaultCurrencyCode"/> seçilen ülkeye göre
/// HQ base para birimini önerir (TR→TRY, US→USD…).
///
/// <para>IMultiTenant (host null + null‖own görünürlük, CurrencyUnit gibi): host global listeyi
/// seed'ler, tenant okur. Host = merkezi operasyon/referans; şirket/şube tenant'a aittir.</para>
/// </summary>
public class Country : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>ISO-3166 alpha-2 (TR, US, ...). Tekil (host kataloğunda).</summary>
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;

    /// <summary>Ülkenin varsayılan para birimi kodu (CurrencyUnitCode; HQ base önerisi). ZORUNLU —
    /// birimi olmayan ülkeye izin verilmez.</summary>
    public virtual string DefaultCurrencyCode { get; protected set; } = null!;

    public virtual bool IsActive { get; protected set; }
    public virtual int DisplayOrder { get; protected set; }

    protected Country() { }

    public Country(
        string code,
        string name,
        string defaultCurrencyCode,
        int displayOrder = 0,
        Guid? tenantId = null)
    {
        SetCode(code);
        SetName(name);
        SetDefaultCurrencyCode(defaultCurrencyCode);
        DisplayOrder = displayOrder;
        TenantId = tenantId;
        IsActive = true;
    }

    public virtual void SetCode(string code)
        => Code = Check.NotNullOrWhiteSpace(code, nameof(code), CountryConsts.CodeMaxLength).ToUpperInvariant();

    public virtual void SetName(string name)
        => Name = Check.NotNullOrWhiteSpace(name, nameof(name), CountryConsts.NameMaxLength);

    public virtual void SetDefaultCurrencyCode(string code)
        => DefaultCurrencyCode = Check.NotNullOrWhiteSpace(code, nameof(code), CurrencyConsts.CodeMaxLength).ToUpperInvariant();
    public virtual void Activate() => IsActive = true;
    public virtual void Deactivate() => IsActive = false;
    public virtual void SetDisplayOrder(int order) => DisplayOrder = order;
}
