namespace Integration.TradeXpress.Geography;

/// <summary>
/// İdari alan (il/eyalet — ISO 3166-2 alt-bölüm) — <b>core HOST-GLOBAL</b> coğrafya referansı (IMultiTenant
/// DEĞİL; TenantId yok → tüm tenant'lar paylaşır; N11City deseniyle hizalı). Ülkeye id-only bağlı
/// (<see cref="CountryId"/>, nav YOK; aggregate sınırı). ISO kodu (<see cref="Iso3166_2Code"/>, ör TR-34/US-AL)
/// e-Fatura/UBL kimliğidir; <see cref="Code"/> kaynak kod (N11 il kodu 1–81 ya da ISO alt-bölüm kısaltması).
/// Alt seviye (ilçe) <see cref="Locality"/>'de bağlıdır.
/// </summary>
public class AdministrativeArea : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected AdministrativeArea()
    {
    }

    public AdministrativeArea(
        Guid countryId,
        string code,
        string name,
        string? iso3166_2Code = null,
        string? category = null)
    {
        SetCountry(countryId);
        SetCode(code);
        SetName(name);
        SetIso3166_2Code(iso3166_2Code);
        SetCategory(category);
    }

    #endregion

    #region Properties

    /// <summary>Üst ülke — <see cref="Countries.Country"/>'ye id-only referans (nav YOK). ZORUNLU.</summary>
    public virtual Guid CountryId { get; protected set; }

    /// <summary>ISO 3166-2 alt-bölüm kodu (ör. TR-34, US-AL). Opsiyonel — ISO eşlemesi olmayan alan null olabilir.</summary>
    public virtual string? Iso3166_2Code { get; protected set; }

    /// <summary>Kaynak kod (N11 il kodu 1–81 ya da ISO alt-bölüm kısaltması). ZORUNLU.</summary>
    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>İdari-alan sınıfı (ör. province/state). Opsiyonel.</summary>
    public virtual string? Category { get; protected set; }

    /// <summary>On-demand yerellik (ilçe/şehir) importu işareti (UTC) — null = bu idari alanın şehirleri henüz
    /// çekilmedi; dolu = dataset importu (ya da TR ilçe N11-seed'i) tamamlandı. İki-seviyeli lazy import'un alt
    /// katmanı: ülke seçilince yalnız il/eyaletler iner, EYALET seçilince o eyaletin şehirleri per-state çekilir
    /// (böylece US gibi ülkelerde 19k şehrin tamamı değil, yalnız seçilen eyaletin ~300 şehri DB'ye iner).</summary>
    public virtual DateTime? LocalitiesImportedAt { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCountry(Guid countryId)
    {
        if (countryId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:AdministrativeArea:CountryRequired");
        }

        CountryId = countryId;
    }

    public virtual void SetCode(string code)
    {
        // Kaynak kod (N11 numerik il kodu / ISO kısaltma): kültür-bağımsız UPPER, boşluk yok. Min 1 (tek haneli "1").
        Code = StringFieldGuard.NormalizeInvariantCode(code, nameof(Code), 1, GeographyConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // N11/ISO kaynak adı olduğu gibi korunur (Trim + zorunlu); TitleCase YOK (Türkçe karakter kaçağı riski).
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, GeographyConsts.NameMaxLength);
    }

    public virtual void SetIso3166_2Code(string? iso3166_2Code)
    {
        Iso3166_2Code = StringFieldGuard.EnsureOptionalText(
            iso3166_2Code,
            nameof(Iso3166_2Code),
            2,
            GeographyConsts.Iso3166_2CodeMaxLength);
    }

    public virtual void SetCategory(string? category)
    {
        Category = StringFieldGuard.EnsureOptionalText(
            category,
            nameof(Category),
            2,
            GeographyConsts.CategoryMaxLength);
    }

    /// <summary>Bu idari alanın yerelliklerinin (ilçe/şehir) çekildiğini işaretler — per-state on-demand importun
    /// idempotency anahtarı. Saat çağırandan gelir (ABP <c>IClock.Now</c>; doğrudan DateTime.Now KULLANILMAZ).</summary>
    public virtual void MarkLocalitiesImported(DateTime importedAt)
    {
        LocalitiesImportedAt = importedAt;
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
