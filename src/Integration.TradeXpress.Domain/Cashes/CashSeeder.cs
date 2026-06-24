namespace Integration.TradeXpress.Cashes;

/// <summary>
/// Host (TenantId=null) Cash kataloğunu seed eder. Kaynak = host <see cref="CurrencyUnit"/> kayıtlarından
/// <see cref="CurrencyUnitType.Cash"/> olanlar — Cash'ler AYNI Code/Name ile oluşturulur ve
/// <see cref="Cash.FollowingUnitId"/> o birime bağlanır. Tek doğruluk kaynağı CurrencyUnit seed listesidir
/// (ayrı bir kod/ad listesi tutulmaz → DRY). Yalnız host; her tenant'a ayrı kayıt AÇILMAZ.
/// TenantId=null garanti (<c>CurrentTenant.Change(null)</c>). Tekrar çalıştırılabilir (var olan kodu atlar).
/// </summary>
public class CashSeeder(
    IRepository<Cash, Guid> cashRepository,
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IDataFilter dataFilter,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<Cash, Guid> _cashRepository = cashRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository = currencyUnitRepository;
    private readonly IDataFilter _dataFilter = dataFilter;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;

    #endregion

    #region Seeding

    /// <summary>Host'taki Type=Cash para birimleri için eksik Cash kayıtlarını ekler. Yalnız host (TenantId=null).</summary>
    public async Task SeedAsync()
    {
        // TenantId=null GARANTİ: yazma host kapsamında olsun. Filter disable → host birimlerini (null) görebil.
        using (_currentTenant.Change(null))
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var cashUnits = await GetHostCashCurrencyUnits();
            var existing = await GetExistingHostCashCodes();

            foreach (var unit in cashUnits)
            {
                if (existing.Contains(unit.Code) == false)
                {
                    await AddCash(unit);
                }
            }

            await SaveAsync();
        }

        // Host (TenantId=null) + Type=Cash para birimleri — Cash kataloğunun kaynağı.
        async Task<List<CurrencyUnit>> GetHostCashCurrencyUnits()
        {
            return [.. (await _currencyUnitRepository.GetQueryableAsync())
                .Where(u => u.TenantId == null && u.Type == CurrencyUnitType.Cash)];
        }

        // Host'ta zaten kayıtlı Cash kodları — tekrar eklememek için (harf-duyarsız).
        async Task<HashSet<string>> GetExistingHostCashCodes()
        {
            return (await _cashRepository.GetQueryableAsync())
                .Where(c => c.TenantId == null)
                .Select(c => c.Code)
                .ToList()
                .ToHashSet(StringComparer.OrdinalIgnoreCase); // [.. ] kullanılmadı: özel karşılaştırıcı gerekiyor
        }

        // Birimle AYNI Code/Name'le Cash oluşturur (henüz kaydetmeden); FollowingUnitId = birimin Id'si.
        async Task AddCash(CurrencyUnit unit)
        {
            await _cashRepository.InsertAsync(
                new Cash(code: unit.Code, name: unit.Name, followingUnitId: unit.Id),
                autoSave: false); // toplu ekle; kayıt sonda tek SaveAsync'te
        }
    }

    #endregion

    #region Helpers

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar.
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync(); // seed daima bir UoW içinde çalışır (Current! güvenli)
    }

    #endregion
}
