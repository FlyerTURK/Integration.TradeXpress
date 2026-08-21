using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Countries;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Geography;

/// <summary>
/// On-demand ülke coğrafyası importu — İKİ SEVİYELİ lazy: (1) ülke seçilince yalnız il/EYALET verisi
/// (<see cref="ImportCountryAreasAsync"/>), (2) eyalet seçilince yalnız O EYALETİN şehirleri
/// (<see cref="ImportAreaLocalitiesAsync"/>) dr5hn dataset'inden çekilip host-global (<c>CurrentTenant.Change(null)</c>)
/// coğrafya tablolarına yazılır; UI hep DB'den okur. Böylece US gibi ülkelerde 19k şehrin tamamı değil, yalnız
/// seçilen eyaletin ~300 şehri iner. AppService DEĞİL (<see cref="EtsyTaxonomies.EtsyTaxonomySyncManager"/> ikizi):
/// lazy tetik dışında ileride worker/açılış da çağırabilsin. UoW deseni: kısa read-UoW (guard'lar) → dataset okuma
/// (DbContext'i TUTMADAN) → tek toplu write-UoW (upsert + import işareti).
///
/// <para>Guard'lar (eyalet): <see cref="Country.GeographyImportedAt"/> dolu → no-op (idempotent). Kod "TR" → no-op:
/// TR il/ilçe N11-kaynaklı + N11 id-only kolonlarıyla bağlı (operasyonel gerçek, <see cref="GeographySeeder"/> doldurur) —
/// dataset'in TR'nin üstüne yazması YASAK. Dataset'te alt-bölümü olmayan ülkede tek SEMBOLİK ana alan
/// (Code=MAIN) oluşturulur ve <see cref="Country.UsesAdministrativeArea"/>=false yapılır (UI state katmanını
/// gizler — kullanıcı kararı).</para>
/// <para>Guard'lar (şehir): <see cref="AdministrativeArea.LocalitiesImportedAt"/> dolu → no-op. TR → dataset
/// denenmez (ilçe N11-seed'li) ama işaret set edilir (tekrar tetiklenmesin). Sembolik ana alan → ülkenin TÜM
/// şehirleri (filtresiz); normal eyalet → yalnız o eyaletin şehirleri (state_code == alan kodu süzmesi).</para>
/// </summary>
public class GeographyImportManager : DomainService
{
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<AdministrativeArea, Guid> _administrativeAreaRepository;
    private readonly IRepository<Locality, Guid> _localityRepository;
    private readonly GeographyDatasetProvider _datasetProvider;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IClock _clock;

    public GeographyImportManager(
        IRepository<Country, Guid> countryRepository,
        IRepository<AdministrativeArea, Guid> administrativeAreaRepository,
        IRepository<Locality, Guid> localityRepository,
        GeographyDatasetProvider datasetProvider,
        IDataFilter dataFilter,
        IUnitOfWorkManager uowManager,
        IClock clock)
    {
        _countryRepository = countryRepository;
        _administrativeAreaRepository = administrativeAreaRepository;
        _localityRepository = localityRepository;
        _datasetProvider = datasetProvider;
        _dataFilter = dataFilter;
        _uowManager = uowManager;
        _clock = clock;
    }

    /// <summary>Ülkenin İDARİ ALANLARINI (il/eyalet) dataset'ten içe aktarır — ŞEHİR ÇEKMEZ (şehirler eyalet
    /// seçilince <see cref="ImportAreaLocalitiesAsync"/> ile per-state iner). İdempotent: import işareti doluysa
    /// ve TR için no-op. Upsert anahtarı: idari alan = Iso3166_2Code. Alt-bölümü olmayan ülkede sembolik ana alan.</summary>
    public virtual async Task ImportCountryAreasAsync(Guid countryId, CancellationToken cancellationToken = default)
    {
        // 1) Guard'lar KISA read-UoW'da — dataset indirme/okuma DbContext'i tutmadan yapılsın (Etsy manager deseni).
        string countryCode;
        using (var readUow = _uowManager.Begin(requiresNew: true))
        {
            var country = await FindCountryAsync(countryId);
            if (country.GeographyImportedAt != null)
            {
                // İdempotent no-op: idari alanlar zaten çekilmiş (ya da TR/US seed'i işaretlemiş).
                await readUow.CompleteAsync(cancellationToken);
                return;
            }

            if (IsTurkey(country.Code))
            {
                // TR guard: il/ilçe N11-kaynaklı ve N11 id-only kolonlarıyla bağlı (GeographySeeder). Dataset importu
                // TR'ye DOKUNMAZ — işaretleme de seeder'ın işi (N11 verisi dolunca kendisi set eder).
                Logger.LogInformation("Coğrafya importu: TR idari alanları atlandı (N11-kaynaklı; seeder yönetir).");
                await readUow.CompleteAsync(cancellationToken);
                return;
            }

            countryCode = country.Code;
            await readUow.CompleteAsync(cancellationToken);
        }

        // 2) Dataset okuma (ilk seferde indirir; sonrası yerel önbellek) — ülke-filtreli il/eyalet satırları (küçük).
        var states = await _datasetProvider.GetStatesForCountryAsync(countryCode, cancellationToken);

        // 3) Tek toplu write-UoW: idari alan upsert → import işareti. Coğrafya HOST-GLOBAL yazılır. Şehir YOK.
        using (CurrentTenant.Change(null))
        using (var writeUow = _uowManager.Begin(requiresNew: true))
        {
            var country = await FindCountryAsync(countryId); // taze entity (yeni UoW context'i)
            if (country.GeographyImportedAt != null)
            {
                await writeUow.CompleteAsync(cancellationToken); // yarış guard'ı (eşzamanlı ikinci tetik)
                return;
            }

            var areaBySuffix = await UpsertAdministrativeAreasAsync(country, states);

            country.MarkGeographyImported(_clock.Now);
            await _countryRepository.UpdateAsync(country, autoSave: false);

            await SaveAsync();
            await writeUow.CompleteAsync(cancellationToken);

            Logger.LogInformation(
                "Coğrafya importu [{Country}]: {States} il/eyalet içe aktarıldı (dataset: {DatasetStates} eyalet satırı). Şehirler eyalet seçilince per-state iner.",
                countryCode, areaBySuffix.Values.Distinct().Count(), states.Count);
        }
    }

    /// <summary>Bir idari alanın (eyaletin) ŞEHİRLERİNİ dataset'ten içe aktarır — iki-seviyeli lazy'nin alt katmanı.
    /// İdempotent: alanın <see cref="AdministrativeArea.LocalitiesImportedAt"/> doluysa no-op. TR → dataset denenmez
    /// (ilçe N11-seed'li) ama işaret set edilir (tekrar tetiklenmesin). Sembolik ana alan (Code=MAIN) → ülkenin TÜM
    /// şehirleri; normal eyalet → yalnız o eyaletin şehirleri (state_code == alan kodu). Upsert anahtarı: alan + ad.</summary>
    public virtual async Task ImportAreaLocalitiesAsync(Guid administrativeAreaId, CancellationToken cancellationToken = default)
    {
        // 1) Kimlik + guard'lar KISA read-UoW'da (dataset okuma DbContext tutmadan).
        string countryCode;
        string areaCode;
        bool isTurkey;
        bool isSymbolicMain;
        using (var readUow = _uowManager.Begin(requiresNew: true))
        {
            var area = await FindAreaAsync(administrativeAreaId);
            if (area.LocalitiesImportedAt != null)
            {
                // İdempotent no-op: bu eyaletin şehirleri zaten çekilmiş (ya da TR ilçe seed'i işaretlemiş).
                await readUow.CompleteAsync(cancellationToken);
                return;
            }

            var country = await FindCountryAsync(area.CountryId);
            countryCode = country.Code;
            areaCode = area.Code;
            isTurkey = IsTurkey(countryCode);
            // Sembolik ana alan: kodu MAIN (dataset'te alt-bölümü olmayan ülkede şehirler buraya bağlanır) → ülke-geneli.
            isSymbolicMain = string.Equals(areaCode, GeographyConsts.SymbolicMainAreaCode, StringComparison.OrdinalIgnoreCase);
            await readUow.CompleteAsync(cancellationToken);
        }

        // 2) Dataset okuma — TR'de HİÇ (ilçe N11-seed'li, yalnız işaret set edilir). Sembolik ana alan → ülkenin
        //    TÜM şehirleri (filtresiz); normal eyalet → yalnız o eyaletin şehirleri (per-state süzme).
        IReadOnlyList<GeographyCityRecord> cities = Array.Empty<GeographyCityRecord>();
        if (isTurkey == false)
        {
            cities = isSymbolicMain
                ? await _datasetProvider.GetCitiesForCountryAsync(countryCode, cancellationToken)
                : await _datasetProvider.GetCitiesForStateAsync(countryCode, areaCode, cancellationToken);
        }

        // 3) Tek toplu write-UoW: yerellik upsert (TR'de yok) → alanın import işareti. HOST-GLOBAL yazılır.
        using (CurrentTenant.Change(null))
        using (var writeUow = _uowManager.Begin(requiresNew: true))
        {
            var area = await FindAreaAsync(administrativeAreaId); // taze entity
            if (area.LocalitiesImportedAt != null)
            {
                await writeUow.CompleteAsync(cancellationToken); // yarış guard'ı
                return;
            }

            var added = 0;
            if (isTurkey == false)
            {
                var country = await FindCountryAsync(area.CountryId);
                added = await UpsertAreaLocalitiesAsync(country, area, cities);
            }

            area.MarkLocalitiesImported(_clock.Now);
            await _administrativeAreaRepository.UpdateAsync(area, autoSave: false);

            await SaveAsync();
            await writeUow.CompleteAsync(cancellationToken);

            Logger.LogInformation(
                "Coğrafya importu [{Country}/{Area}]: {Cities} şehir içe aktarıldı ({Mode}).",
                countryCode, areaCode, added,
                isTurkey ? "TR ilçe N11-seed'li — yalnız işaretlendi" : (isSymbolicMain ? "sembolik ana alan (ülke-geneli)" : "per-state"));
        }
    }

    #region İdari alan upsert

    // states → AdministrativeArea upsert (var-mı anahtarı Iso3166_2Code). Dönen sözlük: alt-bölüm kısaltması →
    // alan (şehir bağlama için). Dataset'te hiç state yoksa sembolik ana alan kurulur + UsesAdministrativeArea=false.
    private async Task<Dictionary<string, AdministrativeArea>> UpsertAdministrativeAreasAsync(
        Country country, IReadOnlyList<GeographyStateRecord> states)
    {
        var existing = await GetAreasOfAsync(country.Id);
        var byIso = new Dictionary<string, AdministrativeArea>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in existing)
        {
            if (area.Iso3166_2Code != null)
            {
                byIso[area.Iso3166_2Code] = area;
            }
        }

        var bySuffix = new Dictionary<string, AdministrativeArea>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var state in states)
        {
            var iso = country.Code + "-" + state.SubdivisionCode;
            if (byIso.TryGetValue(iso, out var area) == false)
            {
                area = new AdministrativeArea(
                    countryId: country.Id,
                    code: state.SubdivisionCode,
                    name: state.Name,
                    iso3166_2Code: iso,
                    category: state.Category);
                await _administrativeAreaRepository.InsertAsync(area, autoSave: false);
                byIso[iso] = area;
                added++;
            }

            bySuffix[state.SubdivisionCode] = area;
        }

        if (states.Count == 0)
        {
            // Dataset bu ülke için alt-bölüm vermiyor → şehirler tek SEMBOLİK ana alana bağlanır; UI, ülke
            // bayrağıyla state katmanını gizler (kullanıcı kararı). Alan zaten varsa yeniden kullanılır.
            var main = await GetOrCreateSymbolicMainAreaAsync(country, existing);
            bySuffix[GeographyConsts.SymbolicMainAreaCode] = main;

            if (country.UsesAdministrativeArea)
            {
                country.SetUsesAdministrativeArea(false);
                await _countryRepository.UpdateAsync(country, autoSave: false);
            }
        }

        // Alan Id'leri ABP InsertAsync'te atanır; yine de yerellik FK'lerinden önce kesinleştir (seeder deseni).
        await SaveAsync();

        if (added > 0)
        {
            Logger.LogInformation("Coğrafya importu [{Country}]: {Added} idari alan eklendi.", country.Code, added);
        }

        return bySuffix;
    }

    private async Task<AdministrativeArea> GetOrCreateSymbolicMainAreaAsync(
        Country country, List<AdministrativeArea> existing)
    {
        var main = existing.FirstOrDefault(a => a.Code == GeographyConsts.SymbolicMainAreaCode);
        if (main != null)
        {
            return main;
        }

        main = new AdministrativeArea(
            countryId: country.Id,
            code: GeographyConsts.SymbolicMainAreaCode,
            name: country.Name,
            iso3166_2Code: null,
            category: GeographyConsts.CategoryMain);
        await _administrativeAreaRepository.InsertAsync(main, autoSave: false);
        return main;
    }

    #endregion

    #region Yerellik upsert

    // Tek EYALETİN şehirleri → Locality upsert (per-state; şehirler zaten bu alana süzülü geldi ya da sembolik ana
    // alan için ülke-geneli). Hepsi bu alana bağlanır. Var-mı anahtarı AdministrativeAreaId + Name (dataset'te şehir
    // için kalıcı iş kodu yok; Code = dataset satır id'si yalnız kaynak izi).
    private async Task<int> UpsertAreaLocalitiesAsync(
        Country country,
        AdministrativeArea area,
        IReadOnlyList<GeographyCityRecord> cities)
    {
        if (cities.Count == 0)
        {
            return 0;
        }

        var existingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var locality in await GetLocalitiesOfAreaAsync(area.Id))
        {
            existingKeys.Add(LocalityKey(area.Id, locality.Name));
        }

        var added = 0;
        foreach (var city in cities)
        {
            var key = LocalityKey(area.Id, city.Name);
            if (existingKeys.Add(key) == false)
            {
                continue; // aynı alanda aynı ad zaten var (önceki import ya da dataset dupliği) — idempotent atla
            }

            await _localityRepository.InsertAsync(
                new Locality(
                    administrativeAreaId: area.Id,
                    countryId: country.Id,
                    code: city.Id.ToString(CultureInfo.InvariantCulture),
                    name: city.Name),
                autoSave: false);
            added++;
        }

        return added;
    }

    // Yerellik var-mı anahtarı: alan + normalize ad (kültür-bağımsız upper — TR 'i/İ' tuzağı yok; ad karşılaştırması
    // büyük/küçük harf duyarsız olsun ki dataset'in ad-casing oynaması dupliğe yol açmasın).
    private static string LocalityKey(Guid administrativeAreaId, string name)
    {
        return administrativeAreaId.ToString("N") + "\n" + name.Trim().ToUpperInvariant();
    }

    #endregion

    #region Sorgu yardımcıları

    // Ülke host-global da olabilir tenant-owned da → IMultiTenant filtresi kapatılarak Id ile bulunur
    // (Etsy manager'ın kimlik çözme deseniyle hizalı). Yazım tarafı yine host bağlamında (Change(null)).
    private async Task<Country> FindCountryAsync(Guid countryId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var country = await AsyncExecuter.FirstOrDefaultAsync(
                (await _countryRepository.GetQueryableAsync()).Where(c => c.Id == countryId));
            if (country == null)
            {
                throw new EntityNotFoundException(typeof(Country), countryId);
            }

            return country;
        }
    }

    // İdari alan host-global (IMultiTenant DEĞİL) → filtre kapatma no-op ama diğer helper'larla hizalı; Id ile bulunur.
    private async Task<AdministrativeArea> FindAreaAsync(Guid administrativeAreaId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var area = await AsyncExecuter.FirstOrDefaultAsync(
                (await _administrativeAreaRepository.GetQueryableAsync()).Where(a => a.Id == administrativeAreaId));
            if (area == null)
            {
                throw new EntityNotFoundException(typeof(AdministrativeArea), administrativeAreaId);
            }

            return area;
        }
    }

    private async Task<List<AdministrativeArea>> GetAreasOfAsync(Guid countryId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.ToListAsync(
                (await _administrativeAreaRepository.GetQueryableAsync()).Where(a => a.CountryId == countryId));
        }
    }

    private async Task<List<Locality>> GetLocalitiesOfAreaAsync(Guid administrativeAreaId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.ToListAsync(
                (await _localityRepository.GetQueryableAsync()).Where(l => l.AdministrativeAreaId == administrativeAreaId));
        }
    }

    private static bool IsTurkey(string countryCode)
    {
        return string.Equals(countryCode, "TR", StringComparison.OrdinalIgnoreCase);
    }

    // Bekleyen değişiklikleri UoW içinde topluca yazar (GeographySeeder.SaveAsync deseniyle hizalı).
    private async Task SaveAsync()
    {
        await _uowManager.Current!.SaveChangesAsync();
    }

    #endregion
}
