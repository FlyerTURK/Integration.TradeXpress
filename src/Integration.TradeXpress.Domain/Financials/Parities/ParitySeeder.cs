namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite seed'i (tek sorumluluk): işletmenin gerçekten kullandığı SEÇİLİ çiftler (aşağıdaki
/// <see cref="CuratedPairs"/> listesi), öncelik diziyle yönlü (base = yüksek öncelikli).
/// Host-only; birimler kurulduktan SONRA çağrılır (orchestrator sırası).
/// Tekrar çalıştırılabilir — zaten var olanı yeniden eklemez.
/// </summary>
public class ParitySeeder(
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IRepository<Parity, Guid> parityRepository,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository = currencyUnitRepository;
    private readonly IRepository<Parity, Guid> _parityRepository = parityRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;

    #endregion

    #region Seeding

    /// <summary>Seçili (curated) host paritelerini ekler. Yalnız host.</summary>
    public async Task SeedAsync()
    {
        var hostUnits = await GetHostUnits();
        var idByCode = hostUnits.ToDictionary(u => u.Code, u => u.Id, StringComparer.OrdinalIgnoreCase); // koda göre id, harf-duyarsız
        var existingPairs = await GetExistingHostPairs();

        var order = 1;
        foreach (var (baseCode, quoteCode) in CuratedPairs(idByCode))
        {
            var pair = (idByCode[baseCode], idByCode[quoteCode]);

            if (existingPairs.Contains(pair) == false)
            {
                await AddParity(pair.Item1, pair.Item2, order++);
            }
        }

        await SaveAsync();

        // Yalnız host (global) birimler — pariteler host kataloğuna ait.
        async Task<List<CurrencyUnit>> GetHostUnits()
        {
            return [.. (await _currencyUnitRepository.GetQueryableAsync())
                .Where(u => u.TenantId == null)];
        }

        // Zaten kurulu host paritelerinin (base, quote) ikilileri — tekrar eklememek için.
        async Task<HashSet<(Guid Base, Guid Quote)>> GetExistingHostPairs()
        {
            return [.. (await _parityRepository.GetQueryableAsync())
                .Where(p => p.TenantId == null)
                .Select(p => new { p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId })
                .ToList() // ValueTuple projeksiyonu EF'te çevrilemez → önce belleğe al
                .Select(x => (x.BaseCurrencyUnitId, x.QuoteCurrencyUnitId))];
        }

        // Tek bir pariteyi (henüz kaydetmeden) ekler.
        async Task AddParity(Guid baseId, Guid quoteId, int displayOrder)
        {
            await _parityRepository.InsertAsync(
                new Parity(
                    baseCurrencyUnitId: baseId,
                    quoteCurrencyUnitId: quoteId,
                    isActive: true,
                    displayOrder: displayOrder),
                autoSave: false); // toplu ekle; kayıt sonda tek SaveAsync'te
        }
    }

    #endregion

    #region Helpers

    // Seçili çiftler — yalnız host kataloğunda VAR OLAN birimlerin ikilileri; her biri öncelik
    // diziyle yönlü (Base = güçlü olan). Eksik birim içeren çift sessizce atlanır (idempotent/güvenli).
    private static IEnumerable<(string Base, string Quote)> CuratedPairs(IReadOnlyDictionary<string, Guid> idByCode)
    {
        foreach (var (a, b) in Pairs)
        {
            if (idByCode.ContainsKey(a) && idByCode.ContainsKey(b))
            {
                yield return CurrencyUnitPriority.Direct(a, b);
            }
        }
    }

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar.
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    #endregion

    #region Seed Data

    // İşletmenin kullandığı seçili pariteler (12). Yön önemsiz — Direct() base/quote'u önceliğe göre
    // belirler; PairKey ters-çifti zaten teke indirir. Yalnız katalogda var olan birim kodları.
    private static readonly (string A, string B)[] Pairs =
    [
        (CurrencyUnitCode.USD, CurrencyUnitCode.TRY),
        (CurrencyUnitCode.HAS, CurrencyUnitCode.TRY),
        (CurrencyUnitCode.HAS, CurrencyUnitCode.USD),
        (CurrencyUnitCode.HAS, CurrencyUnitCode.EUR),
        (CurrencyUnitCode.HAS, CurrencyUnitCode.GUM),
        (CurrencyUnitCode.HAS, CurrencyUnitCode.PLT),
        (CurrencyUnitCode.HAS, CurrencyUnitCode.PLD),
        (CurrencyUnitCode.GUM, CurrencyUnitCode.TRY),
        (CurrencyUnitCode.USD, CurrencyUnitCode.CHF),
        (CurrencyUnitCode.USD, CurrencyUnitCode.CAD),
        (CurrencyUnitCode.USD, CurrencyUnitCode.SAR),
        (CurrencyUnitCode.EUR, CurrencyUnitCode.TRY),
    ];

    #endregion
}
