namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu seed'i (tek sorumluluk, host-global). Yalnız desteklediğimiz birime sahip ülkeler;
/// <see cref="Country.DefaultCurrencyUnitId"/> HQ base önerisidir ve zorunludur (id-only referans —
/// katalogdaki birim kodları host <c>CurrencyUnit</c> kayıtlarından id'ye çözülür; birim henüz yoksa
/// o ülke atlanır, sonraki koşu tamamlar). Tekrar çalıştırılabilir.
/// </summary>
public class CountrySeeder(
    IRepository<Country, Guid> countryRepository,
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<Country, Guid> _countryRepository = countryRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository = currencyUnitRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;

    #endregion

    #region Seeding

    /// <summary>Katalogdaki eksik ülkeleri ekler. Yalnız host (global).</summary>
    public async Task SeedAsync()
    {
        var existing = await GetExistingHostCountryCodes();
        var unitIdByCode = await GetHostUnitIdsByCode();

        var order = 1;
        foreach (var spec in CountryCatalog)
        {
            if (existing.Contains(spec.Code) == false
                && unitIdByCode.TryGetValue(spec.Currency, out var unitId))
            {
                await AddCountry(spec, unitId, order);
            }

            order++; // DisplayOrder katalog konumuyla hizalı kalsın (atlanan da arttırır)
        }

        await SaveAsync();

        // Host'ta zaten kayıtlı ülke kodları — tekrar eklememek için (harf-duyarsız).
        async Task<HashSet<string>> GetExistingHostCountryCodes()
        {
            return (await _countryRepository.GetQueryableAsync())
                .Where(c => c.TenantId == null)
                .Select(c => c.Code)
                .ToList()
                .ToHashSet(StringComparer.OrdinalIgnoreCase); // [.. ] kullanılmadı: özel karşılaştırıcı gerekiyor
        }

        // Host birimlerinin kod→id haritası (katalogdaki para birimi kodlarını id'ye çözmek için).
        async Task<Dictionary<string, Guid>> GetHostUnitIdsByCode()
        {
            var units = (await _currencyUnitRepository.GetQueryableAsync())
                .Where(u => u.TenantId == null)
                .Select(u => new { u.Code, u.Id })
                .ToList();

            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in units)
            {
                map[unit.Code] = unit.Id;
            }

            return map;
        }

        // Tek bir ülkeyi (henüz kaydetmeden) ekler.
        async Task AddCountry(CountrySpec spec, Guid defaultCurrencyUnitId, int displayOrder)
        {
            await _countryRepository.InsertAsync(
                new Country(
                    code: spec.Code,
                    name: spec.Name,
                    defaultCurrencyUnitId: defaultCurrencyUnitId,
                    displayOrder: displayOrder),
                autoSave: false); // toplu ekle; kayıt sonda tek SaveAsync'te
        }
    }

    #endregion

    #region Helpers

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar.
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    #endregion

    #region Seed Data

    // Seed satırı: bir ülkenin tanımı (kod, ad, varsayılan para birimi KODU — seed anında id'ye çözülür).
    private sealed record CountrySpec(string Code, string Name, string Currency);

    // Yalnız desteklediğimiz birime sahip ülkeler — birimi olmayan ülke seed edilmez (DefaultCurrencyUnitId zorunlu).
    private static readonly CountrySpec[] CountryCatalog =
    [
        new("TR", "Türkiye",                        CurrencyUnitCode.TRY),
        new("US", "Amerika Birleşik Devletleri",    CurrencyUnitCode.USD),
        new("DE", "Almanya",                        CurrencyUnitCode.EUR),
        new("FR", "Fransa",                         CurrencyUnitCode.EUR),
        new("IT", "İtalya",                         CurrencyUnitCode.EUR),
        new("NL", "Hollanda",                       CurrencyUnitCode.EUR),
        new("ES", "İspanya",                        CurrencyUnitCode.EUR),
        new("GB", "Birleşik Krallık",               CurrencyUnitCode.GBP),
        new("CH", "İsviçre",                        CurrencyUnitCode.CHF),
        new("SA", "Suudi Arabistan",                CurrencyUnitCode.SAR),
        new("AU", "Avustralya",                     CurrencyUnitCode.AUD),
        new("CA", "Kanada",                         CurrencyUnitCode.CAD),
    ];

    #endregion
}
