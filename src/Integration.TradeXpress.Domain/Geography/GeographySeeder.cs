using Microsoft.Extensions.Logging;
using Integration.TradeXpress.N11Cities;

namespace Integration.TradeXpress.Geography;

/// <summary>
/// Core coğrafya seed'i (host-global, idempotent). İki iş:
/// <list type="number">
/// <item>ISO 3166-1 TAM ülke kataloğunu (249) upsert eder — mevcut ülkeye yalnız ISO alan (Alpha3/Numeric) +
/// adres-model bayrakları ekler (<c>Name</c>/<c>DefaultCurrencyUnitId</c>/<c>DisplayOrder</c> KORUNUR); eksik
/// ülkeyi para birimSİZ (<c>DefaultCurrencyUnitId=null</c>) ekler.</item>
/// <item>TR il/ilçe'yi N11'den (idempotent — ISO kodu var mı bak), US eyaletlerini sabit ISO katalogdan türetir;
/// N11 il/ilçe id-only eşleme kolonlarını (<see cref="N11City.CoreAdministrativeAreaId"/> /
/// <see cref="N11District.CoreLocalityId"/>) doldurur.</item>
/// </list>
/// <para>N11 il/ilçe verisi BOŞSA (DbMigrator seed'i N11 sync'ten önce koşabilir) TR türetme ATLANIR ve loglanır —
/// N11 sync sonrası tekrar çalışınca dolar (idempotent). Host bağlamı <see cref="ICurrentTenant.Change(Guid?)"/>
/// (null) ile garanti edilir.</para>
/// <para>Kaynak: ISO 3166-1 (stefangabos/world_countries, en+tr) + ISO 3166-2 (TR il / US eyalet).</para>
/// </summary>
public class GeographySeeder(
    IRepository<Country, Guid> countryRepository,
    IRepository<AdministrativeArea, Guid> administrativeAreaRepository,
    IRepository<Locality, Guid> localityRepository,
    IRepository<N11City, Guid> n11CityRepository,
    IRepository<N11District, Guid> n11DistrictRepository,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager,
    IClock clock,
    ILogger<GeographySeeder> logger)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<Country, Guid> _countryRepository = countryRepository;
    private readonly IRepository<AdministrativeArea, Guid> _administrativeAreaRepository = administrativeAreaRepository;
    private readonly IRepository<Locality, Guid> _localityRepository = localityRepository;
    private readonly IRepository<N11City, Guid> _n11CityRepository = n11CityRepository;
    private readonly IRepository<N11District, Guid> _n11DistrictRepository = n11DistrictRepository;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;
    private readonly IClock _clock = clock;
    private readonly ILogger<GeographySeeder> _logger = logger;

    #endregion

    #region Seeding

    /// <summary>Host-global coğrafya kataloğunu upsert eder. Yalnız host (TenantId=null) bağlamında çağrılmalı.</summary>
    public async Task SeedAsync()
    {
        // Tüm core coğrafya host-global (TenantId=null). Change(null) hem sorguyu (host satırları) hem
        // insert'i (TenantId=null atanır) garanti eder.
        using (_currentTenant.Change(null))
        {
            var countryByCode = await UpsertIsoCountriesAsync();
            await SeedTurkeyGeographyAsync(countryByCode);
            await SeedUnitedStatesGeographyAsync(countryByCode);
        }
    }

    // ISO 3166-1 tam listeyi upsert eder; kod→entity haritası döner (TR/US türetmesi tekrar kullanır).
    private async Task<Dictionary<string, Country>> UpsertIsoCountriesAsync()
    {
        var byCode = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in await GetHostCountries())
        {
            byCode[country.Code] = country;
        }

        var added = 0;
        var enriched = 0;
        foreach (var spec in IsoCountryCatalog)
        {
            if (byCode.TryGetValue(spec.Alpha2, out var existing))
            {
                // Mevcut ülke: ISO alan + adres bayraklarını zenginleştir (DefaultCurrency/DisplayOrder KORUNUR).
                existing.SetAlpha3Code(spec.Alpha3);
                existing.SetNumericCode(spec.Numeric);
                existing.SetUsesAdministrativeArea(true);
                existing.SetUsesSubLocality(false);
                // Ad: yalnız HÂLÂ Türkçe-seed olan (kullanıcı özelleştirmemiş) satırı İngilizce'ye çevir. Stored Name =
                // NormalizeAsName(NameTr) ürünüdür (ham NameTr değil) → karşılaştırma da normalize edilmişe göre yapılır.
                // Kullanıcı elle değiştirdiyse (Name farklı) DOKUNMA. SetReferenceName ham casing'i korur (TitleCase'siz).
                if (existing.Name == spec.NameTr.NormalizeAsName())
                {
                    existing.SetReferenceName(spec.NameEn);
                }
                await _countryRepository.UpdateAsync(existing, autoSave: false);
                enriched++;
            }
            else
            {
                // Yeni referans ülke: para birimSİZ (DefaultCurrencyUnitId=null). Ad = İngilizce ISO adı (referans ctor
                // ham casing'i korur — TitleCase uygulanmaz, "United States of America" bozulmaz).
                var created = new Country(spec.Alpha2, spec.NameEn);
                created.SetAlpha3Code(spec.Alpha3);
                created.SetNumericCode(spec.Numeric);
                // UsesAdministrativeArea=true (field default), UsesSubLocality=false (default) — TR/US aşağıda ayarlanır.
                await _countryRepository.InsertAsync(created, autoSave: false);
                byCode[created.Code] = created;
                added++;
            }
        }

        await SaveAsync();
        _logger.LogInformation(
            "Coğrafya seed [Ülke]: {Added} eklendi, {Enriched} zenginleştirildi (ISO 3166-1 tam liste, katalog {Total}).",
            added, enriched, IsoCountryCatalog.Length);
        return byCode;
    }

    // TR: mahalle seviyesi + il/ilçe'yi N11'den türet (idempotent; N11 boşsa atla).
    private async Task SeedTurkeyGeographyAsync(Dictionary<string, Country> countryByCode)
    {
        if (countryByCode.TryGetValue("TR", out var turkey) == false)
        {
            _logger.LogWarning("Coğrafya seed [TR]: Türkiye ülkesi bulunamadı — il/ilçe türetme atlandı.");
            return;
        }

        // TR mahalle (SubLocality) seviyesini kullanır — diğer ülkelerden farkı.
        turkey.SetUsesSubLocality(true);
        // TR adres etiketleri: İl / İlçe / Mahalle / Posta Kodu. Yalnız LocalityType default'tan (City) sapar → District.
        // Deterministik değer (her seed'de aynı) → SetUsesSubLocality gibi koşulsuz idempotent.
        turkey.SetAddressFormat(
            AdministrativeAreaType.Province,
            LocalityType.District,
            SubLocalityType.Neighborhood,
            PostalCodeType.PostalCode);
        await _countryRepository.UpdateAsync(turkey, autoSave: false);

        var areaByIso = new Dictionary<string, AdministrativeArea>(StringComparer.OrdinalIgnoreCase);
        var areaByCityCode = new Dictionary<string, AdministrativeArea>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in await GetAreasOf(turkey.Id))
        {
            if (area.Iso3166_2Code != null)
            {
                areaByIso[area.Iso3166_2Code] = area;
            }

            areaByCityCode[area.Code] = area;
        }

        // İL: STATİK ISO 3166-2:TR kataloğundan — N11'den BAĞIMSIZ. Eskiden iller N11 il verisinden türetiliyordu,
        // yani N11 kanalı kurulmadan TR adres girilemiyordu (ABD eyaletleri ise sabit katalogdan geliyordu — asimetri).
        // İl seti 1999'dan beri (81 il, Düzce sonuncusu) değişmedi; pazaryerine bağlamak için sebep yok.
        // İlçe/mahalle N11'de KALIR: ilçe adları ve N11'in kendi ID'leri zaten API'den gelmek zorunda.
        var addedAreas = 0;
        foreach (var province in TurkishProvinceCatalog)
        {
            var provinceIso = TurkishProvinceIso(province.Code);
            if (areaByIso.ContainsKey(provinceIso))
            {
                continue;   // idempotent — kullanıcının düzenlediği ada DOKUNMA
            }

            var seeded = new AdministrativeArea(
                countryId: turkey.Id,
                code: province.Code,
                name: province.Name,
                iso3166_2Code: provinceIso,
                category: GeographyConsts.CategoryProvince);
            await _administrativeAreaRepository.InsertAsync(seeded, autoSave: false);
            areaByIso[provinceIso] = seeded;
            areaByCityCode[seeded.Code] = seeded;
            addedAreas++;
        }

        await SaveAsync();   // il Id'leri kesinleşsin (N11 CoreAdministrativeAreaId + ilçe FK'si için)

        var cities = (await _n11CityRepository.GetQueryableAsync()).ToList();
        if (cities.Count == 0)
        {
            _logger.LogInformation(
                "Coğrafya seed [TR]: {Areas} il eklendi (statik ISO 3166-2 kataloğu). N11 il verisi boş — "
                + "N11 köprüsü ve ilçe türetme atlandı (N11 sync sonrası tekrar çalışınca dolar).", addedAreas);
            return;
        }

        foreach (var city in cities)
        {
            var iso = TurkishProvinceIso(city.CityCode);
            if (areaByIso.TryGetValue(iso, out var area) == false)
            {
                // Statik katalogda OLMAYAN bir N11 ili (katalog bayatlarsa ya da N11 farklı kodlarsa) — yine de al.
                // Kod KANONİK yazılır (sol-sıfırlı) → aşağıdaki ilçe eşleşmesi statik katalogla aynı anahtarı kullanır.
                area = new AdministrativeArea(
                    countryId: turkey.Id,
                    code: NormalizeProvinceCode(city.CityCode),
                    name: city.Name,
                    iso3166_2Code: iso,
                    category: GeographyConsts.CategoryProvince);
                await _administrativeAreaRepository.InsertAsync(area, autoSave: false);
                areaByIso[iso] = area;
                areaByCityCode[area.Code] = area;
                addedAreas++;
                _logger.LogWarning(
                    "Coğrafya seed [TR]: N11'de olup statik katalogda OLMAYAN il — {Iso} {Name}.", iso, city.Name);
            }

            // N11 eşlemesi — CoreAdministrativeAreaId (idempotent — zaten doğruysa dokunma).
            if (city.CoreAdministrativeAreaId != area.Id)
            {
                city.SetCoreAdministrativeArea(area.Id);
                await _n11CityRepository.UpdateAsync(city, autoSave: false);
            }

            // TR ili → ilçeleri N11-seed'li (aşağıda) → per-state yerellik import işaretini backfill et: lazy tetik
            // (GeographyAppService.GetLocalitiesAsync) TR eyaletinde dataset DENEMESİN. Manager'ın ayrıca TR guard'ı
            // da var. İdempotent — yalnız null olanı işaretle (mevcut TR alanları için de backfill; ilk seed korunur).
            if (area.LocalitiesImportedAt == null)
            {
                area.MarkLocalitiesImported(_clock.Now);
                await _administrativeAreaRepository.UpdateAsync(area, autoSave: false);
            }
        }

        // TR il verisi N11'den türedi → on-demand import işareti (lazy tetik TR'yi tekrar ÇEKMESİN; import
        // manager'ın ayrıca kod-bazlı TR guard'ı da var). Yalnız ilk kez set edilir (ilk seed zamanı korunur).
        if (turkey.GeographyImportedAt == null)
        {
            turkey.MarkGeographyImported(_clock.Now);
            await _countryRepository.UpdateAsync(turkey, autoSave: false);
        }

        await SaveAsync(); // idari alan Id'leri kesinleşsin (ilçe FK'si için)

        var districts = (await _n11DistrictRepository.GetQueryableAsync()).ToList();
        if (districts.Count == 0)
        {
            _logger.LogInformation(
                "Coğrafya seed [TR]: {Areas} il eklendi; N11 ilçe verisi boş — ilçe türetme atlandı.", addedAreas);
            return;
        }

        var localityByCode = new Dictionary<string, Locality>(StringComparer.OrdinalIgnoreCase);
        foreach (var locality in await GetLocalitiesOf(turkey.Id))
        {
            localityByCode[locality.Code] = locality;
        }

        var addedLocalities = 0;
        var orphanDistricts = 0;
        foreach (var district in districts)
        {
            // N11 ilçe kaydının il kodu sol-sıfırsız gelir ("1") — sözlük ise kanonik ("01") anahtarlı.
            // Aramayı da kanonikleştirmezsek 1–9 arası illerin ilçeleri toptan ıskalanır (bkz. NormalizeProvinceCode).
            if (areaByCityCode.TryGetValue(NormalizeProvinceCode(district.CityCode), out var area) == false)
            {
                orphanDistricts++; // il eşleşmedi (N11 tutarsızlığı) — atla, sonda logla
                continue;
            }

            if (localityByCode.TryGetValue(district.DistrictId, out var locality) == false)
            {
                locality = new Locality(
                    administrativeAreaId: area.Id,
                    countryId: turkey.Id,
                    code: district.DistrictId,
                    name: district.Name);
                await _localityRepository.InsertAsync(locality, autoSave: false);
                localityByCode[locality.Code] = locality;
                addedLocalities++;
            }

            if (district.CoreLocalityId != locality.Id)
            {
                district.SetCoreLocality(locality.Id);
                await _n11DistrictRepository.UpdateAsync(district, autoSave: false);
            }
        }

        await SaveAsync();
        _logger.LogInformation(
            "Coğrafya seed [TR]: {Areas} il + {Localities} ilçe eklendi (eşleşmeyen ilçe: {Orphans}).",
            addedAreas, addedLocalities, orphanDistricts);
    }

    // US: mahalle seviyesi YOK + 50 eyalet (sabit ISO 3166-2 katalog).
    private async Task SeedUnitedStatesGeographyAsync(Dictionary<string, Country> countryByCode)
    {
        if (countryByCode.TryGetValue("US", out var usa) == false)
        {
            _logger.LogWarning("Coğrafya seed [US]: ABD ülkesi bulunamadı — eyalet türetme atlandı.");
            return;
        }

        // US alt-yerellik (mahalle) kullanmaz — upsert'te false set edildi; açıkça garanti et.
        usa.SetUsesSubLocality(false);
        // US adres etiketleri: Eyalet (State) / Şehir (City=default) / — / ZIP. Deterministik → koşulsuz idempotent.
        usa.SetAddressFormat(
            AdministrativeAreaType.State,
            LocalityType.City,
            SubLocalityType.Neighborhood,
            PostalCodeType.Zip);
        await _countryRepository.UpdateAsync(usa, autoSave: false);

        var existingIso = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in await GetAreasOf(usa.Id))
        {
            if (area.Iso3166_2Code != null)
            {
                existingIso.Add(area.Iso3166_2Code);
            }
        }

        var added = 0;
        foreach (var state in UnitedStatesStateCatalog)
        {
            var iso = "US-" + state.Code;
            if (existingIso.Contains(iso) == false)
            {
                await _administrativeAreaRepository.InsertAsync(
                    new AdministrativeArea(
                        countryId: usa.Id,
                        code: state.Code,
                        name: state.Name,
                        iso3166_2Code: iso,
                        category: GeographyConsts.CategoryState),
                    autoSave: false);
                existingIso.Add(iso);
                added++;
            }
        }

        // US eyaletleri sabit katalogdan dolduruldu → İDARİ ALAN (üst katman) import işareti: lazy tetik US
        // eyaletlerini dataset'ten TEKRAR çekmesin. ŞEHİR işareti (AdministrativeArea.LocalitiesImportedAt) burada
        // set EDİLMEZ → NULL kalır: her eyaletin şehirleri kullanıcı o eyaleti seçince per-state lazy iner (19k'nın
        // tamamı değil ~300). İki-seviyeli lazy'nin özü budur.
        if (usa.GeographyImportedAt == null)
        {
            usa.MarkGeographyImported(_clock.Now);
            await _countryRepository.UpdateAsync(usa, autoSave: false);
        }

        await SaveAsync();
        _logger.LogInformation(
            "Coğrafya seed [US]: {Added} eyalet eklendi (katalog {Total}).", added, UnitedStatesStateCatalog.Length);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// TR il kodunu KANONİK hâle getirir: 2-hane sol-sıfır pad, kültür-bağımsız ("1" → "01", "34" → "34").
    ///
    /// <para><b>Neden ŞART:</b> N11 il kodlarını sol-sıfırsız döner ("1", "9", "81") ama statik ISO 3166-2:TR
    /// kataloğu plaka kodunu 2-hane tutar ("01"). İki taraf farklı yazımda olursa il↔ilçe eşleşmesi
    /// (<c>areaByCityCode</c>) 1–9 arası DOKUZ ilde sessizce ıskalar ve o illerin ilçeleri hiç kurulmaz
    /// (Ankara/Adana/Antalya dahil). Bu yüzden hem KAYIT hem ARAMA tek bir kanonik biçimden geçer.</para>
    /// </summary>
    private static string NormalizeProvinceCode(string cityCode)
    {
        return cityCode.Trim().PadLeft(2, '0');
    }

    // Kanonik il kodu → ISO 3166-2 alt-bölüm kodu (TR-01 .. TR-81).
    private static string TurkishProvinceIso(string cityCode)
    {
        return "TR-" + NormalizeProvinceCode(cityCode);
    }

    // Host (TenantId=null) ülkeleri — Change(null) altında filtre zaten host'a daraltır; açık koşul savunmacı.
    private async Task<List<Country>> GetHostCountries()
    {
        return (await _countryRepository.GetQueryableAsync())
            .Where(c => c.TenantId == null)
            .ToList();
    }

    private async Task<List<AdministrativeArea>> GetAreasOf(Guid countryId)
    {
        return (await _administrativeAreaRepository.GetQueryableAsync())
            .Where(a => a.CountryId == countryId)
            .ToList();
    }

    private async Task<List<Locality>> GetLocalitiesOf(Guid countryId)
    {
        return (await _localityRepository.GetQueryableAsync())
            .Where(l => l.CountryId == countryId)
            .ToList();
    }

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar (CountrySeeder deseniyle hizalı).
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    #endregion

    #region Seed Data

    // ISO 3166-1 tam liste (249) — stefangabos/world_countries (en+tr). name_en → Country.Name (görüntü adı;
    // her iki UI dilinde de İngilizce). name_tr yalnız mevcut Türkçe-seed satırları tespit edip İngilizce'ye çevirmek
    // için karşılaştırma anahtarı (kullanıcı özelleştirmesini korur). ISO alpha kodları fatura kimliği. Katalog en altta.
    private static readonly (string Alpha2, string Alpha3, string Numeric, string NameTr, string NameEn)[] IsoCountryCatalog =
    [
        ("AD", "AND", "020", "Andorra", "Andorra"),
        ("AE", "ARE", "784", "Birleşik Arap Emirlikleri", "United Arab Emirates"),
        ("AF", "AFG", "004", "Afganistan", "Afghanistan"),
        ("AG", "ATG", "028", "Antigua ve Barbuda", "Antigua and Barbuda"),
        ("AI", "AIA", "660", "Anguilla", "Anguilla"),
        ("AL", "ALB", "008", "Arnavutluk", "Albania"),
        ("AM", "ARM", "051", "Ermenistan", "Armenia"),
        ("AO", "AGO", "024", "Angola", "Angola"),
        ("AQ", "ATA", "010", "Antarktika", "Antarctica"),
        ("AR", "ARG", "032", "Arjantin", "Argentina"),
        ("AS", "ASM", "016", "Amerikan Samoası", "American Samoa"),
        ("AT", "AUT", "040", "Avusturya", "Austria"),
        ("AU", "AUS", "036", "Avustralya", "Australia"),
        ("AW", "ABW", "533", "Aruba", "Aruba"),
        ("AX", "ALA", "248", "Åland Adaları", "Åland Islands"),
        ("AZ", "AZE", "031", "Azerbaycan", "Azerbaijan"),
        ("BA", "BIH", "070", "Bosna-Hersek", "Bosnia and Herzegovina"),
        ("BB", "BRB", "052", "Barbados", "Barbados"),
        ("BD", "BGD", "050", "Bangladeş", "Bangladesh"),
        ("BE", "BEL", "056", "Belçika", "Belgium"),
        ("BF", "BFA", "854", "Burkina Faso", "Burkina Faso"),
        ("BG", "BGR", "100", "Bulgaristan", "Bulgaria"),
        ("BH", "BHR", "048", "Bahreyn", "Bahrain"),
        ("BI", "BDI", "108", "Burundi", "Burundi"),
        ("BJ", "BEN", "204", "Benin", "Benin"),
        ("BL", "BLM", "652", "Saint Barthélemy", "Saint Barthélemy"),
        ("BM", "BMU", "060", "Bermuda", "Bermuda"),
        ("BN", "BRN", "096", "Brunei Darüsselam", "Brunei Darussalam"),
        ("BO", "BOL", "068", "Bolivya", "Bolivia, Plurinational State of"),
        ("BQ", "BES", "535", "Bonaire Sint Eustatius Saba", "Bonaire, Sint Eustatius and Saba"),
        ("BR", "BRA", "076", "Brezilya", "Brazil"),
        ("BS", "BHS", "044", "Bahamalar", "Bahamas"),
        ("BT", "BTN", "064", "Bhutan", "Bhutan"),
        ("BV", "BVT", "074", "Bouvet Adası", "Bouvet Island"),
        ("BW", "BWA", "072", "Botsvana", "Botswana"),
        ("BY", "BLR", "112", "Belarus", "Belarus"),
        ("BZ", "BLZ", "084", "Belize", "Belize"),
        ("CA", "CAN", "124", "Kanada", "Canada"),
        ("CC", "CCK", "166", "Cocos (Keeling) Adaları", "Cocos (Keeling) Islands"),
        ("CD", "COD", "180", "Kongo Demokratik Cumhuriyeti", "Congo, Democratic Republic of the"),
        ("CF", "CAF", "140", "Orta Afrika Cumhuriyeti", "Central African Republic"),
        ("CG", "COG", "178", "Kongo Cumhuriyeti", "Congo"),
        ("CH", "CHE", "756", "İsviçre", "Switzerland"),
        ("CI", "CIV", "384", "Fildişi Sahili", "Côte d'Ivoire"),
        ("CK", "COK", "184", "Cook Adaları", "Cook Islands"),
        ("CL", "CHL", "152", "Şili", "Chile"),
        ("CM", "CMR", "120", "Kamerun", "Cameroon"),
        ("CN", "CHN", "156", "Çin", "China"),
        ("CO", "COL", "170", "Kolombiya", "Colombia"),
        ("CR", "CRI", "188", "Kosta Rika", "Costa Rica"),
        ("CU", "CUB", "192", "Küba", "Cuba"),
        ("CV", "CPV", "132", "Yeşil Burun Adaları", "Cabo Verde"),
        ("CW", "CUW", "531", "Curaçao", "Curaçao"),
        ("CX", "CXR", "162", "Christmas Adası", "Christmas Island"),
        ("CY", "CYP", "196", "Kıbrıs Cumhuriyeti", "Cyprus"),
        ("CZ", "CZE", "203", "Çekya", "Czechia"),
        ("DE", "DEU", "276", "Almanya", "Germany"),
        ("DJ", "DJI", "262", "Cibuti", "Djibouti"),
        ("DK", "DNK", "208", "Danimarka", "Denmark"),
        ("DM", "DMA", "212", "Dominika", "Dominica"),
        ("DO", "DOM", "214", "Dominik Cumhuriyeti", "Dominican Republic"),
        ("DZ", "DZA", "012", "Cezayir", "Algeria"),
        ("EC", "ECU", "218", "Ekvador", "Ecuador"),
        ("EE", "EST", "233", "Estonya", "Estonia"),
        ("EG", "EGY", "818", "Mısır", "Egypt"),
        ("EH", "ESH", "732", "Batı Sahra", "Western Sahara"),
        ("ER", "ERI", "232", "Eritre", "Eritrea"),
        ("ES", "ESP", "724", "İspanya", "Spain"),
        ("ET", "ETH", "231", "Etiyopya", "Ethiopia"),
        ("FI", "FIN", "246", "Finlandiya", "Finland"),
        ("FJ", "FJI", "242", "Fiji", "Fiji"),
        ("FK", "FLK", "238", "Falkland Adaları [Malvinas]", "Falkland Islands (Malvinas)"),
        ("FM", "FSM", "583", "Mikronezya Federal Devletleri", "Micronesia, Federated States of"),
        ("FO", "FRO", "234", "Faroe Adaları", "Faroe Islands"),
        ("FR", "FRA", "250", "Fransa", "France"),
        ("GA", "GAB", "266", "Gabon", "Gabon"),
        ("GB", "GBR", "826", "Birleşik Krallık", "United Kingdom of Great Britain and Northern Ireland"),
        ("GD", "GRD", "308", "Grenada", "Grenada"),
        ("GE", "GEO", "268", "Gürcistan", "Georgia"),
        ("GF", "GUF", "254", "Fransız Guyanası", "French Guiana"),
        ("GG", "GGY", "831", "Guernsey", "Guernsey"),
        ("GH", "GHA", "288", "Gana", "Ghana"),
        ("GI", "GIB", "292", "Cebelitarık", "Gibraltar"),
        ("GL", "GRL", "304", "Grönland", "Greenland"),
        ("GM", "GMB", "270", "Gambiya", "Gambia"),
        ("GN", "GIN", "324", "Gine", "Guinea"),
        ("GP", "GLP", "312", "Guadeloupe", "Guadeloupe"),
        ("GQ", "GNQ", "226", "Ekvator Ginesi", "Equatorial Guinea"),
        ("GR", "GRC", "300", "Yunanistan", "Greece"),
        ("GS", "SGS", "239", "Güney Georgia ve Güney Sandwich Adaları", "South Georgia and the South Sandwich Islands"),
        ("GT", "GTM", "320", "Guatemala", "Guatemala"),
        ("GU", "GUM", "316", "Guam", "Guam"),
        ("GW", "GNB", "624", "Gine-Bissau", "Guinea-Bissau"),
        ("GY", "GUY", "328", "Guyana", "Guyana"),
        ("HK", "HKG", "344", "Hong Kong", "Hong Kong"),
        ("HM", "HMD", "334", "Heard Adası ve McDonald Adaları", "Heard Island and McDonald Islands"),
        ("HN", "HND", "340", "Honduras", "Honduras"),
        ("HR", "HRV", "191", "Hırvatistan", "Croatia"),
        ("HT", "HTI", "332", "Haiti", "Haiti"),
        ("HU", "HUN", "348", "Macaristan", "Hungary"),
        ("ID", "IDN", "360", "Endonezya", "Indonesia"),
        ("IE", "IRL", "372", "İrlanda", "Ireland"),
        ("IL", "ISR", "376", "İsrail", "Israel"),
        ("IM", "IMN", "833", "Man Adası", "Isle of Man"),
        ("IN", "IND", "356", "Hindistan", "India"),
        ("IO", "IOT", "086", "Britanya Hint Okyanusu Toprakları", "British Indian Ocean Territory"),
        ("IQ", "IRQ", "368", "Irak", "Iraq"),
        ("IR", "IRN", "364", "İran", "Iran, Islamic Republic of"),
        ("IS", "ISL", "352", "İzlanda", "Iceland"),
        ("IT", "ITA", "380", "İtalya", "Italy"),
        ("JE", "JEY", "832", "Jersey", "Jersey"),
        ("JM", "JAM", "388", "Jamaika", "Jamaica"),
        ("JO", "JOR", "400", "Ürdün", "Jordan"),
        ("JP", "JPN", "392", "Japonya", "Japan"),
        ("KE", "KEN", "404", "Kenya", "Kenya"),
        ("KG", "KGZ", "417", "Kırgızistan", "Kyrgyzstan"),
        ("KH", "KHM", "116", "Kamboçya", "Cambodia"),
        ("KI", "KIR", "296", "Kiribati", "Kiribati"),
        ("KM", "COM", "174", "Komorlar", "Comoros"),
        ("KN", "KNA", "659", "Saint Kitts ve Nevis", "Saint Kitts and Nevis"),
        ("KP", "PRK", "408", "Kore Demokratik Halk Cumhuriyeti", "Korea, Democratic People's Republic of"),
        ("KR", "KOR", "410", "Kore Cumhuriyeti", "Korea, Republic of"),
        ("KW", "KWT", "414", "Kuveyt", "Kuwait"),
        ("KY", "CYM", "136", "Cayman Adaları", "Cayman Islands"),
        ("KZ", "KAZ", "398", "Kazakistan", "Kazakhstan"),
        ("LA", "LAO", "418", "Lao Demokratik Halk Cumhuriyeti", "Lao People's Democratic Republic"),
        ("LB", "LBN", "422", "Lübnan", "Lebanon"),
        ("LC", "LCA", "662", "Saint Lucia", "Saint Lucia"),
        ("LI", "LIE", "438", "Lihtenştayn", "Liechtenstein"),
        ("LK", "LKA", "144", "Sri Lanka", "Sri Lanka"),
        ("LR", "LBR", "430", "Liberya", "Liberia"),
        ("LS", "LSO", "426", "Lesotho", "Lesotho"),
        ("LT", "LTU", "440", "Litvanya", "Lithuania"),
        ("LU", "LUX", "442", "Lüksemburg", "Luxembourg"),
        ("LV", "LVA", "428", "Letonya", "Latvia"),
        ("LY", "LBY", "434", "Libya", "Libya"),
        ("MA", "MAR", "504", "Fas", "Morocco"),
        ("MC", "MCO", "492", "Monako", "Monaco"),
        ("MD", "MDA", "498", "Moldova", "Moldova, Republic of"),
        ("ME", "MNE", "499", "Karadağ", "Montenegro"),
        ("MF", "MAF", "663", "Saint Martin (Fransız bölümü)", "Saint Martin (French part)"),
        ("MG", "MDG", "450", "Madagaskar", "Madagascar"),
        ("MH", "MHL", "584", "Marshall Adaları", "Marshall Islands"),
        ("MK", "MKD", "807", "Kuzey Makedonya", "North Macedonia"),
        ("ML", "MLI", "466", "Mali", "Mali"),
        ("MM", "MMR", "104", "Myanmar", "Myanmar"),
        ("MN", "MNG", "496", "Moğolistan", "Mongolia"),
        ("MO", "MAC", "446", "Makao", "Macao"),
        ("MP", "MNP", "580", "Kuzey Mariana Adaları", "Northern Mariana Islands"),
        ("MQ", "MTQ", "474", "Martinik", "Martinique"),
        ("MR", "MRT", "478", "Moritanya", "Mauritania"),
        ("MS", "MSR", "500", "Montserrat", "Montserrat"),
        ("MT", "MLT", "470", "Malta", "Malta"),
        ("MU", "MUS", "480", "Mauritius", "Mauritius"),
        ("MV", "MDV", "462", "Maldivler", "Maldives"),
        ("MW", "MWI", "454", "Malavi", "Malawi"),
        ("MX", "MEX", "484", "Meksika", "Mexico"),
        ("MY", "MYS", "458", "Malezya", "Malaysia"),
        ("MZ", "MOZ", "508", "Mozambik", "Mozambique"),
        ("NA", "NAM", "516", "Namibya", "Namibia"),
        ("NC", "NCL", "540", "Yeni Kaledonya", "New Caledonia"),
        ("NE", "NER", "562", "Nijer", "Niger"),
        ("NF", "NFK", "574", "Norfolk Adası", "Norfolk Island"),
        ("NG", "NGA", "566", "Nijerya", "Nigeria"),
        ("NI", "NIC", "558", "Nikaragua", "Nicaragua"),
        ("NL", "NLD", "528", "Hollanda", "Netherlands"),
        ("NO", "NOR", "578", "Norveç", "Norway"),
        ("NP", "NPL", "524", "Nepal", "Nepal"),
        ("NR", "NRU", "520", "Nauru", "Nauru"),
        ("NU", "NIU", "570", "Niue", "Niue"),
        ("NZ", "NZL", "554", "Yeni Zelanda", "New Zealand"),
        ("OM", "OMN", "512", "Umman", "Oman"),
        ("PA", "PAN", "591", "Panama", "Panama"),
        ("PE", "PER", "604", "Peru", "Peru"),
        ("PF", "PYF", "258", "Fransız Polinezyası", "French Polynesia"),
        ("PG", "PNG", "598", "Papua Yeni Gine", "Papua New Guinea"),
        ("PH", "PHL", "608", "Filipinler", "Philippines"),
        ("PK", "PAK", "586", "Pakistan", "Pakistan"),
        ("PL", "POL", "616", "Polonya", "Poland"),
        ("PM", "SPM", "666", "Saint Pierre ve Miquelon", "Saint Pierre and Miquelon"),
        ("PN", "PCN", "612", "Pitcairn", "Pitcairn"),
        ("PR", "PRI", "630", "Porto Riko", "Puerto Rico"),
        ("PS", "PSE", "275", "Filistin Devleti", "Palestine, State of"),
        ("PT", "PRT", "620", "Portekiz", "Portugal"),
        ("PW", "PLW", "585", "Palau", "Palau"),
        ("PY", "PRY", "600", "Paraguay", "Paraguay"),
        ("QA", "QAT", "634", "Katar", "Qatar"),
        ("RE", "REU", "638", "Réunion", "Réunion"),
        ("RO", "ROU", "642", "Romanya", "Romania"),
        ("RS", "SRB", "688", "Sırbistan", "Serbia"),
        ("RU", "RUS", "643", "Rusya Federasyonu", "Russian Federation"),
        ("RW", "RWA", "646", "Ruanda", "Rwanda"),
        ("SA", "SAU", "682", "Suudi Arabistan", "Saudi Arabia"),
        ("SB", "SLB", "090", "Solomon Adaları", "Solomon Islands"),
        ("SC", "SYC", "690", "Seyşeller", "Seychelles"),
        ("SD", "SDN", "729", "Sudan", "Sudan"),
        ("SE", "SWE", "752", "İsveç", "Sweden"),
        ("SG", "SGP", "702", "Singapur", "Singapore"),
        ("SH", "SHN", "654", "Saint Helena Ascension Adası Tristan da Cunha", "Saint Helena, Ascension and Tristan da Cunha"),
        ("SI", "SVN", "705", "Slovenya", "Slovenia"),
        ("SJ", "SJM", "744", "Svalbard Jan Mayen", "Svalbard and Jan Mayen"),
        ("SK", "SVK", "703", "Slovakya", "Slovakia"),
        ("SL", "SLE", "694", "Sierra Leone", "Sierra Leone"),
        ("SM", "SMR", "674", "San Marino", "San Marino"),
        ("SN", "SEN", "686", "Senegal", "Senegal"),
        ("SO", "SOM", "706", "Somali", "Somalia"),
        ("SR", "SUR", "740", "Surinam", "Suriname"),
        ("SS", "SSD", "728", "Güney Sudan", "South Sudan"),
        ("ST", "STP", "678", "São Tomé ve Príncipe", "Sao Tome and Principe"),
        ("SV", "SLV", "222", "El Salvador", "El Salvador"),
        ("SX", "SXM", "534", "Sint Maarten (Dutch part)", "Sint Maarten (Dutch part)"),
        ("SY", "SYR", "760", "Suriye Arap Cumhuriyeti", "Syrian Arab Republic"),
        ("SZ", "SWZ", "748", "Esvatini", "Eswatini"),
        ("TC", "TCA", "796", "Turks ve Caicos Adaları", "Turks and Caicos Islands"),
        ("TD", "TCD", "148", "Çad", "Chad"),
        ("TF", "ATF", "260", "Fransız Güney ve Antarktika Toprakları", "French Southern Territories"),
        ("TG", "TGO", "768", "Togo", "Togo"),
        ("TH", "THA", "764", "Tayland", "Thailand"),
        ("TJ", "TJK", "762", "Tacikistan", "Tajikistan"),
        ("TK", "TKL", "772", "Tokelau", "Tokelau"),
        ("TL", "TLS", "626", "Timor-Leste", "Timor-Leste"),
        ("TM", "TKM", "795", "Türkmenistan", "Turkmenistan"),
        ("TN", "TUN", "788", "Tunus", "Tunisia"),
        ("TO", "TON", "776", "Tonga", "Tonga"),
        ("TR", "TUR", "792", "Türkiye", "Türkiye"),
        ("TT", "TTO", "780", "Trinidad ve Tobago", "Trinidad and Tobago"),
        ("TV", "TUV", "798", "Tuvalu", "Tuvalu"),
        ("TW", "TWN", "158", "Tayvan (Çin Eyaleti)", "Taiwan, Province of China"),
        ("TZ", "TZA", "834", "Tanzanya Birleşik Cumhuriyeti", "Tanzania, United Republic of"),
        ("UA", "UKR", "804", "Ukrayna", "Ukraine"),
        ("UG", "UGA", "800", "Uganda", "Uganda"),
        ("UM", "UMI", "581", "Amerika Birleşik Devletleri'nin küçük dış adaları", "United States Minor Outlying Islands"),
        ("US", "USA", "840", "Amerika Birleşik Devletleri", "United States of America"),
        ("UY", "URY", "858", "Uruguay", "Uruguay"),
        ("UZ", "UZB", "860", "Özbekistan", "Uzbekistan"),
        ("VA", "VAT", "336", "Kutsal Makam", "Holy See"),
        ("VC", "VCT", "670", "Saint Vincent ve Grenadinler", "Saint Vincent and the Grenadines"),
        ("VE", "VEN", "862", "Venezuela", "Venezuela, Bolivarian Republic of"),
        ("VG", "VGB", "092", "Virjin Adaları (Britanya)", "Virgin Islands (British)"),
        ("VI", "VIR", "850", "Virjin Adaları (ABD)", "Virgin Islands (U.S.)"),
        ("VN", "VNM", "704", "Viet Nam", "Viet Nam"),
        ("VU", "VUT", "548", "Vanuatu", "Vanuatu"),
        ("WF", "WLF", "876", "Wallis ve Futuna", "Wallis and Futuna"),
        ("WS", "WSM", "882", "Samoa", "Samoa"),
        ("YE", "YEM", "887", "Yemen", "Yemen"),
        ("YT", "MYT", "175", "Mayotte", "Mayotte"),
        ("ZA", "ZAF", "710", "Güney Afrika", "South Africa"),
        ("ZM", "ZMB", "894", "Zambiya", "Zambia"),
        ("ZW", "ZWE", "716", "Zimbabve", "Zimbabwe"),
    ];

    /// <summary>
    /// ISO 3166-2:TR — 81 il (plaka kodu + ad). Kod = 2-hane plaka ("01".."81"), ISO alt-bölüm kodu "TR-01"..
    /// Sabit katalog: il seti 1999'da Düzce (81) eklendiğinden beri DEĞİŞMEDİ.
    /// <para>Ad yazımı resmî Türkçe: İstanbul/İzmir baştaki noktalı İ ile; Afyonkarahisar, Kahramanmaraş,
    /// Şanlıurfa, Hakkâri (şapkalı a) tam adlarıyla; 33 Mersin (İçel değil), 46 Kahramanmaraş.</para>
    /// </summary>
    private static readonly (string Code, string Name)[] TurkishProvinceCatalog =
    [
        ("01", "Adana"),          ("02", "Adıyaman"),       ("03", "Afyonkarahisar"), ("04", "Ağrı"),
        ("05", "Amasya"),         ("06", "Ankara"),         ("07", "Antalya"),        ("08", "Artvin"),
        ("09", "Aydın"),          ("10", "Balıkesir"),      ("11", "Bilecik"),        ("12", "Bingöl"),
        ("13", "Bitlis"),         ("14", "Bolu"),           ("15", "Burdur"),         ("16", "Bursa"),
        ("17", "Çanakkale"),      ("18", "Çankırı"),        ("19", "Çorum"),          ("20", "Denizli"),
        ("21", "Diyarbakır"),     ("22", "Edirne"),         ("23", "Elazığ"),         ("24", "Erzincan"),
        ("25", "Erzurum"),        ("26", "Eskişehir"),      ("27", "Gaziantep"),      ("28", "Giresun"),
        ("29", "Gümüşhane"),      ("30", "Hakkâri"),        ("31", "Hatay"),          ("32", "Isparta"),
        ("33", "Mersin"),         ("34", "İstanbul"),       ("35", "İzmir"),          ("36", "Kars"),
        ("37", "Kastamonu"),      ("38", "Kayseri"),        ("39", "Kırklareli"),     ("40", "Kırşehir"),
        ("41", "Kocaeli"),        ("42", "Konya"),          ("43", "Kütahya"),        ("44", "Malatya"),
        ("45", "Manisa"),         ("46", "Kahramanmaraş"),  ("47", "Mardin"),         ("48", "Muğla"),
        ("49", "Muş"),            ("50", "Nevşehir"),       ("51", "Niğde"),          ("52", "Ordu"),
        ("53", "Rize"),           ("54", "Sakarya"),        ("55", "Samsun"),         ("56", "Siirt"),
        ("57", "Sinop"),          ("58", "Sivas"),          ("59", "Tekirdağ"),       ("60", "Tokat"),
        ("61", "Trabzon"),        ("62", "Tunceli"),        ("63", "Şanlıurfa"),      ("64", "Uşak"),
        ("65", "Van"),            ("66", "Yozgat"),         ("67", "Zonguldak"),      ("68", "Aksaray"),
        ("69", "Bayburt"),        ("70", "Karaman"),        ("71", "Kırıkkale"),      ("72", "Batman"),
        ("73", "Şırnak"),         ("74", "Bartın"),         ("75", "Ardahan"),        ("76", "Iğdır"),
        ("77", "Yalova"),         ("78", "Karabük"),        ("79", "Kilis"),          ("80", "Osmaniye"),
        ("81", "Düzce"),
    ];

    // ISO 3166-2:US — 50 eyalet (2-harf kod + ad). Sabit, iyi-bilinen katalog (DC/territory hariç).
    private static readonly (string Code, string Name)[] UnitedStatesStateCatalog =
    [
        ("AL", "Alabama"),
        ("AK", "Alaska"),
        ("AZ", "Arizona"),
        ("AR", "Arkansas"),
        ("CA", "California"),
        ("CO", "Colorado"),
        ("CT", "Connecticut"),
        ("DE", "Delaware"),
        ("FL", "Florida"),
        ("GA", "Georgia"),
        ("HI", "Hawaii"),
        ("ID", "Idaho"),
        ("IL", "Illinois"),
        ("IN", "Indiana"),
        ("IA", "Iowa"),
        ("KS", "Kansas"),
        ("KY", "Kentucky"),
        ("LA", "Louisiana"),
        ("ME", "Maine"),
        ("MD", "Maryland"),
        ("MA", "Massachusetts"),
        ("MI", "Michigan"),
        ("MN", "Minnesota"),
        ("MS", "Mississippi"),
        ("MO", "Missouri"),
        ("MT", "Montana"),
        ("NE", "Nebraska"),
        ("NV", "Nevada"),
        ("NH", "New Hampshire"),
        ("NJ", "New Jersey"),
        ("NM", "New Mexico"),
        ("NY", "New York"),
        ("NC", "North Carolina"),
        ("ND", "North Dakota"),
        ("OH", "Ohio"),
        ("OK", "Oklahoma"),
        ("OR", "Oregon"),
        ("PA", "Pennsylvania"),
        ("RI", "Rhode Island"),
        ("SC", "South Carolina"),
        ("SD", "South Dakota"),
        ("TN", "Tennessee"),
        ("TX", "Texas"),
        ("UT", "Utah"),
        ("VT", "Vermont"),
        ("VA", "Virginia"),
        ("WA", "Washington"),
        ("WV", "West Virginia"),
        ("WI", "Wisconsin"),
        ("WY", "Wyoming"),
    ];

    #endregion
}
