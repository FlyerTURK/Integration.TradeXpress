namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu seed'i (tek sorumluluk, host-global). Yalnız desteklediğimiz birime sahip ülkeler;
/// <see cref="Country.DefaultCurrencyCode"/> HQ base önerisidir ve zorunludur. Tekrar çalıştırılabilir.
/// </summary>
public class CountrySeeder(
    IRepository<Country, Guid> countryRepository,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<Country, Guid> _countryRepository = countryRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;

    #endregion

    #region Seeding

    /// <summary>Katalogdaki eksik ülkeleri ekler. Yalnız host (global).</summary>
    public async Task SeedAsync()
    {
        var existing = await GetExistingHostCountryCodes();

        var order = 1;
        foreach (var spec in CountryCatalog)
        {
            if (existing.Contains(spec.Code) == false)
            {
                await AddCountry(spec, order);
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

        // Tek bir ülkeyi (henüz kaydetmeden) ekler.
        async Task AddCountry(CountrySpec spec, int displayOrder)
        {
            await _countryRepository.InsertAsync(
                new Country(
                    code: spec.Code,
                    name: spec.Name,
                    defaultCurrencyCode: spec.Currency,
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

    // Seed satırı: bir ülkenin tanımı (kod, ad, varsayılan para birimi).
    private sealed record CountrySpec(string Code, string Name, string Currency);

    // Yalnız desteklediğimiz birime sahip ülkeler — birimi olmayan ülke seed edilmez (DefaultCurrencyCode zorunlu).
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
