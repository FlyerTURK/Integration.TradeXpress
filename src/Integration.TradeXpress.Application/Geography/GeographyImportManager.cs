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
/// On-demand ülke coğrafyası importu — bir ülkenin il/eyalet + şehir verisi İLK ihtiyaçta dr5hn dataset'inden
/// çekilip host-global (<c>CurrentTenant.Change(null)</c>) coğrafya tablolarına yazılır; UI hep DB'den okur.
/// AppService DEĞİL (<see cref="EtsyTaxonomies.EtsyTaxonomySyncManager"/> ikizi): lazy tetik dışında ileride
/// worker/açılış da çağırabilsin. UoW deseni de aynı: kısa read-UoW (guard'lar) → dataset okuma (DbContext'i
/// TUTMADAN; ilk seferde ~31MB indirme) → tek toplu write-UoW (upsert + import işareti).
///
/// <para>Guard'lar: <see cref="Country.GeographyImportedAt"/> dolu → no-op (idempotent). Kod "TR" → no-op:
/// TR il/ilçe N11-kaynaklı + N11 köprülü (operasyonel gerçek, <see cref="GeographySeeder"/> doldurur) —
/// dataset'in TR'nin üstüne yazması YASAK. Dataset'te alt-bölümü olmayan ülkede tek SEMBOLİK ana alan
/// (Code=MAIN) oluşturulur ve <see cref="Country.UsesAdministrativeArea"/>=false yapılır (UI state katmanını
/// gizler — kullanıcı kararı).</para>
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

    /// <summary>Ülkenin coğrafyasını (il/eyalet + şehir) dataset'ten içe aktarır. İdempotent: import işareti
    /// doluysa ve TR için no-op. Upsert anahtarları: idari alan = Iso3166_2Code, yerellik = alan + ad.</summary>
    public virtual async Task ImportCountryAsync(Guid countryId, CancellationToken cancellationToken = default)
    {
        // 1) Guard'lar KISA read-UoW'da — dataset indirme/okuma DbContext'i tutmadan yapılsın (Etsy manager deseni).
        string countryCode;
        using (var readUow = _uowManager.Begin(requiresNew: true))
        {
            var country = await FindCountryAsync(countryId);
            if (country.GeographyImportedAt != null)
            {
                // İdempotent no-op: veri zaten çekilmiş (ya da TR/US seed'i işaretlemiş).
                await readUow.CompleteAsync(cancellationToken);
                return;
            }

            if (IsTurkey(country.Code))
            {
                // TR guard: il/ilçe N11-kaynaklı ve N11 köprü kolonlarıyla bağlı (GeographySeeder). Dataset importu
                // TR'ye DOKUNMAZ — işaretleme de seeder'ın işi (N11 verisi dolunca kendisi set eder).
                Logger.LogInformation("Coğrafya importu: TR atlandı (N11-kaynaklı; seeder yönetir).");
                await readUow.CompleteAsync(cancellationToken);
                return;
            }

            countryCode = country.Code;
            await readUow.CompleteAsync(cancellationToken);
        }

        // 2) Dataset okuma (ilk seferde indirir; sonrası yerel önbellek) — ülke-filtreli, şehirler akışla.
        var states = await _datasetProvider.GetStatesForCountryAsync(countryCode, cancellationToken);
        var cities = await _datasetProvider.GetCitiesForCountryAsync(countryCode, cancellationToken);

        // 3) Tek toplu write-UoW: alan upsert → yerellik upsert → import işareti. Coğrafya HOST-GLOBAL yazılır.
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
            var addedLocalities = await UpsertLocalitiesAsync(country, cities, areaBySuffix);

            country.MarkGeographyImported(_clock.Now);
            await _countryRepository.UpdateAsync(country, autoSave: false);

            await SaveAsync();
            await writeUow.CompleteAsync(cancellationToken);

            Logger.LogInformation(
                "Coğrafya importu [{Country}]: {States} il/eyalet + {Cities} şehir içe aktarıldı (dataset: {DatasetStates} eyalet / {DatasetCities} şehir satırı).",
                countryCode, areaBySuffix.Values.Distinct().Count(), addedLocalities, states.Count, cities.Count);
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

    // cities → Locality upsert. Alan eşlemesi state_code (alt-bölüm kısaltması) ile; eşleşmeyen şehir sembolik
    // ana alana bağlanır (yoksa oluşturulur). Var-mı anahtarı AdministrativeAreaId + Name (dataset'te şehir için
    // kalıcı iş kodu yok; Code = dataset satır id'si yalnız kaynak izi).
    private async Task<int> UpsertLocalitiesAsync(
        Country country,
        IReadOnlyList<GeographyCityRecord> cities,
        Dictionary<string, AdministrativeArea> areaBySuffix)
    {
        if (cities.Count == 0)
        {
            return 0;
        }

        var existingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var locality in await GetLocalitiesOfAsync(country.Id))
        {
            existingKeys.Add(LocalityKey(locality.AdministrativeAreaId, locality.Name));
        }

        AdministrativeArea? symbolicMain =
            areaBySuffix.TryGetValue(GeographyConsts.SymbolicMainAreaCode, out var preset) ? preset : null;

        var added = 0;
        var orphans = 0;
        foreach (var city in cities)
        {
            AdministrativeArea? area = null;
            if (city.StateCode != null)
            {
                areaBySuffix.TryGetValue(city.StateCode, out area);
            }

            if (area == null)
            {
                // Eyaleti eşleşmeyen şehir (dataset tutarsızlığı) → sembolik ana alana bağla (kaybetme).
                orphans++;
                if (symbolicMain == null)
                {
                    symbolicMain = await GetOrCreateSymbolicMainAreaAsync(country, await GetAreasOfAsync(country.Id));
                    await SaveAsync(); // yeni alanın Id'si yerellik FK'sinden önce kesinleşsin
                }

                area = symbolicMain;
            }

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

        if (orphans > 0)
        {
            Logger.LogWarning(
                "Coğrafya importu [{Country}]: {Orphans} şehrin eyalet kodu eşleşmedi — sembolik ana alana bağlandı.",
                country.Code, orphans);
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

    private async Task<List<AdministrativeArea>> GetAreasOfAsync(Guid countryId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.ToListAsync(
                (await _administrativeAreaRepository.GetQueryableAsync()).Where(a => a.CountryId == countryId));
        }
    }

    private async Task<List<Locality>> GetLocalitiesOfAsync(Guid countryId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.ToListAsync(
                (await _localityRepository.GetQueryableAsync()).Where(l => l.CountryId == countryId));
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
