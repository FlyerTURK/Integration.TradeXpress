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
    /// id-only referans (nav YOK; HQ base önerisi). Opsiyonel: birim alan zengin ctor'da zorunludur
    /// (desteklediğimiz ülke); referans-katalog ctor'unda (ISO 3166-1 tam liste) null kalır — birimi
    /// eşlenmemiş ülkeler için beklenen durum.</summary>
    public virtual Guid? DefaultCurrencyUnitId { get; protected set; }

    /// <summary>ISO 3166-1 alpha-3 (TUR, USA...). Opsiyonel — referans-katalog zenginleştirmesi (fatura/UBL kimliği).</summary>
    public virtual string? Alpha3Code { get; protected set; }

    /// <summary>ISO 3166-1 numeric (3 hane string, ör 792/840). Opsiyonel.</summary>
    public virtual string? NumericCode { get; protected set; }

    /// <summary>Ülke adres modelinde idari-alan (il/eyalet — ISO 3166-2) seviyesi kullanır mı. Varsayılan true (çoğu ülke).</summary>
    public virtual bool UsesAdministrativeArea { get; protected set; } = true;

    /// <summary>Ülke adres modelinde alt-yerellik (mahalle) seviyesi kullanır mı. Varsayılan false (yalnız TR gibi).</summary>
    public virtual bool UsesSubLocality { get; protected set; }

    /// <summary>İdari-alan adres etiketi tipi (libaddressinput — TR→İl, US→Eyalet). Picker il/eyalet başlığını buna göre uyarlar.
    /// Varsayılan <see cref="Countries.AdministrativeAreaType.Province"/> (generic).</summary>
    public virtual AdministrativeAreaType AdministrativeAreaType { get; protected set; }

    /// <summary>Yerellik adres etiketi tipi (TR→İlçe, US→Şehir). Varsayılan <see cref="Countries.LocalityType.City"/> (generic).</summary>
    public virtual LocalityType LocalityType { get; protected set; }

    /// <summary>Alt-yerellik adres etiketi tipi (TR→Mahalle). Varsayılan <see cref="Countries.SubLocalityType.Neighborhood"/> (generic).</summary>
    public virtual SubLocalityType SubLocalityType { get; protected set; }

    /// <summary>Posta kodu adres etiketi tipi (US→ZIP, IN→PIN, IE→Eircode). Varsayılan <see cref="Countries.PostalCodeType.PostalCode"/> (generic).</summary>
    public virtual PostalCodeType PostalCodeType { get; protected set; }

    /// <summary>On-demand İDARİ ALAN (il/eyalet) importu işareti (UTC) — null = bu ülkenin il/eyalet verisi henüz
    /// çekilmedi; dolu = idari alan importu (ya da TR/US seed'i) tamamlandı. İki-seviyeli lazy import'un ÜST katmanı:
    /// bu alan yalnız EYALET seviyesini kapsar (şehir DEĞİL — şehirler eyalet seçilince
    /// <see cref="Geography.AdministrativeArea.LocalitiesImportedAt"/> ile per-state çekilir). Lazy tetik
    /// (GeographyAppService) ve import idempotency guard'ı bu alana bakar.</summary>
    public virtual DateTime? GeographyImportedAt { get; protected set; }

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

    /// <summary>Referans-katalog ctor'u (ISO 3166-1 tam liste) — para birimi eşlemesi OLMADAN ülke oluşturur:
    /// <see cref="DefaultCurrencyUnitId"/> null kalır (opsiyonel HQ base önerisi; çoğu ülkede eşleme yok).
    /// Desteklediğimiz birimi olan ülke için birim alan zengin ctor tercih edilir.</summary>
    public Country(string code, string name, int displayOrder = 0)
    {
        SetCode(code);
        // ISO 3166-1 referans adı (önceden-formatlı) — TitleCase'siz ham yol (yalnız seeder çağırır; kullanıcı DEĞİL).
        SetReferenceName(name);
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
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase, ardından zorunlu/min/max doğrulaması. Kullanıcı girdisi yolu.
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            CountryConsts.NameMaxLength);
    }

    /// <summary>Güvenilir REFERANS adı (ISO 3166-1 katalog adı — önceden-formatlı otoriter veri). TitleCase
    /// UYGULAMAZ → kaynak casing korunur (ör. "United States of America", "Congo, Democratic Republic of the";
    /// bağlaçlar küçük kalır). Guard'lar KORUNUR: Trim + zorunlu-boş-değil + min/max (normalize≠validation).
    /// Kullanıcı GİRDİSİ için değildir — o <see cref="SetName"/> (TitleCase) yolundan geçer; yalnız seeder çağırır.</summary>
    public virtual void SetReferenceName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(
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

    public virtual void SetAlpha3Code(string? alpha3Code)
    {
        // ISO 3166-1 alpha-3: kültür-bağımsız UPPER, doluysa tam 3 harf (opsiyonel — boşsa null).
        Alpha3Code = StringFieldGuard.EnsureOptionalText(
            alpha3Code?.ToUpperInvariant(),
            nameof(Alpha3Code),
            CountryConsts.Alpha3CodeLength,
            CountryConsts.Alpha3CodeLength);
    }

    public virtual void SetNumericCode(string? numericCode)
    {
        // ISO 3166-1 numeric: doluysa tam 3 hane string (opsiyonel — boşsa null).
        NumericCode = StringFieldGuard.EnsureOptionalText(
            numericCode,
            nameof(NumericCode),
            CountryConsts.NumericCodeLength,
            CountryConsts.NumericCodeLength);
    }

    public virtual void SetUsesAdministrativeArea(bool value)
    {
        UsesAdministrativeArea = value;
    }

    public virtual void SetUsesSubLocality(bool value)
    {
        UsesSubLocality = value;
    }

    /// <summary>Adres-format metadatasını (libaddressinput etiket tipleri) tek atomik işlemde ayarlar —
    /// picker etiketleri (İl/Eyalet · İlçe/Şehir · Mahalle · Posta Kodu/ZIP) bu tiplere göre uyarlanır. Seed yönetir.</summary>
    public virtual void SetAddressFormat(
        AdministrativeAreaType administrativeAreaType,
        LocalityType localityType,
        SubLocalityType subLocalityType,
        PostalCodeType postalCodeType)
    {
        AdministrativeAreaType = administrativeAreaType;
        LocalityType = localityType;
        SubLocalityType = subLocalityType;
        PostalCodeType = postalCodeType;
    }

    /// <summary>İdari alan (il/eyalet) verisinin çekildiğini işaretler — on-demand ÜST-katman importun idempotency
    /// anahtarı (şehir DEĞİL; şehir per-state <see cref="Geography.AdministrativeArea.MarkLocalitiesImported"/>).
    /// Saat çağırandan gelir (ABP <c>IClock.Now</c>; doğrudan DateTime.Now KULLANILMAZ).</summary>
    public virtual void MarkGeographyImported(DateTime importedAt)
    {
        GeographyImportedAt = importedAt;
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
