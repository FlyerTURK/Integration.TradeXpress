namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite seed'i (tek sorumluluk): host birimlerinin uyumlu tüm çiftleri (C(n,2)), öncelik diziyle
/// yönlü (base = yüksek öncelikli). Host-only; birimler kurulduktan SONRA çağrılır (orchestrator sırası).
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

    /// <summary>Host birimlerinin uyumlu tüm çiftlerini (C(n,2)) ekler. Yalnız host.</summary>
    public async Task SeedAsync()
    {
        var hostUnits = await GetHostUnits();
        var idByCode = hostUnits.ToDictionary(u => u.Code, u => u.Id, StringComparer.OrdinalIgnoreCase); // koda göre id, harf-duyarsız
        var existingPairs = await GetExistingHostPairs();

        var order = 1;
        foreach (var (baseCode, quoteCode) in CompatiblePairs(hostUnits))
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

    // Birimlerin uyumlu tüm ikilileri (C(n,2)); her biri öncelik diziyle yönlü (Base = güçlü olan).
    private static IEnumerable<(string Base, string Quote)> CompatiblePairs(IReadOnlyList<CurrencyUnit> units)
    {
        var codes = units.Select(u => u.Code).ToList();

        for (var i = 0; i < codes.Count; i++)
        {
            for (var j = i + 1; j < codes.Count; j++)
            {
                yield return CurrencyUnitPriority.Direct(codes[i], codes[j]);
            }
        }
    }

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar.
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    #endregion
}
