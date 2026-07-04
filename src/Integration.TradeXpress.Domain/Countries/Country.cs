namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu — merkezi referans verisi (host yönetir, tenant'lar seçer). Tenant'ın merkez
/// (HQ) şirketi bu katalogdan ülke seçer; <see cref="DefaultCurrencyUnitId"/> seçilen ülkeye göre
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

    /// <summary>Ülkenin varsayılan para birimi — <see cref="Financials.CurrencyUnits.CurrencyUnit"/>'e
    /// id-only referans (nav YOK; HQ base önerisi). OTORİTER alan; legacy satırlarda backfill tamamlanana
    /// dek null olabilir (yeni kayıtta zorunlu, ctor doğrular — birimi olmayan ülkeye izin verilmez).</summary>
    public virtual Guid? DefaultCurrencyUnitId { get; protected set; }

    /// <summary>Varsayılan para birimi KODU — ESKİ string referans. Id-only geçişiyle yerini
    /// <see cref="DefaultCurrencyUnitId"/> aldı; yalnız backfill (kod→id eşleştirme) kaynağıdır, yeni kod yolu yazmaz.</summary>
    [Obsolete("Country id-only geçişi; backfill sonrası kaldırılacak — DefaultCurrencyUnitId kullan.")]
    public virtual string? DefaultCurrencyCode { get; protected set; }

    public virtual bool IsActive { get; protected set; }
    public virtual int DisplayOrder { get; protected set; }

    protected Country() { }

    public Country(
        string code,
        string name,
        Guid defaultCurrencyUnitId,
        int displayOrder = 0)
    {
        SetCode(code);
        SetName(name);
        SetDefaultCurrencyUnit(defaultCurrencyUnitId);
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public virtual void SetCode(string code)
    {
        // ISO-3166 alpha-2 sabit uzunluk (min = max = 2). Kültür-BAĞIMSIZ UPPER (tr-TR 'i'→'İ' tuzağı yok);
        // NormalizeCode KULLANILMAZ (evrensel CodeMinLength=3 iki harfli ISO koduna uymaz).
        Code = StringFieldGuard.NormalizeInvariantCode(
            code,
            nameof(Code),
            CountryConsts.CodeMaxLength,
            CountryConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase, ardından zorunlu/min/max doğrulaması.
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            CountryConsts.NameMaxLength);
    }

    public virtual void SetDefaultCurrencyUnit(Guid defaultCurrencyUnitId)
    {
        if (defaultCurrencyUnitId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Country:DefaultCurrencyRequired");
        }

        DefaultCurrencyUnitId = defaultCurrencyUnitId;
    }

    /// <summary>Geçiş backfill'i: yalnız <see cref="DefaultCurrencyUnitId"/> boşsa doldurur (idempotent;
    /// dolu satıra dokunmaz — CompanyOwnedBackfiller deseniyle hizalı).</summary>
    public virtual void BackfillDefaultCurrencyUnitIfMissing(Guid defaultCurrencyUnitId)
    {
        if (DefaultCurrencyUnitId == null)
        {
            SetDefaultCurrencyUnit(defaultCurrencyUnitId);
        }
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
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
