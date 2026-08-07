namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Para birimlerini ve marjlarını veritabanına ilk kez yazan kurulum kodu. İki iş yapar:
/// (1) host'taki para birimi listesi + Türk Lirası'nın başlangıç fiyatı,
/// (2) her firmaya (host dahil) gördüğü her birim için varsayılan marj.
/// Tekrar çalıştırılabilir — zaten var olanı yeniden eklemez.
/// </summary>
public class CurrencyUnitSeeder(
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IRepository<ExchangeRate, Guid> exchangeRateRepository,
    IRepository<CurrencyUnitMargin, Guid> marginRepository,
    IDataFilter dataFilter,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager) 
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository = currencyUnitRepository;
    private readonly IRepository<ExchangeRate, Guid> _exchangeRateRepository = exchangeRateRepository;
    private readonly IRepository<CurrencyUnitMargin, Guid> _marginRepository = marginRepository;
    private readonly IDataFilter _dataFilter = dataFilter;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;

    #endregion

    #region Seeding

    /// <summary>Eksik para birimlerini ekler; sonra Türk Lirası'na başlangıç fiyatını (1) verir. Yalnız host.</summary>
    public async Task SeedCatalogAsync()
    {
        var turkishLira = await AddMissingCurrencies();

        await GiveLiraStartingPriceIfMissing(turkishLira);

        // Eksik para birimlerini ekler; (bu çalıştırmada yeni eklendiyse) Lira'yı döndürür.
        async Task<CurrencyUnit?> AddMissingCurrencies()
        {
            var existing = await LoadCurrenciesByCode();

            var newlyAddedLira = await AddEachMissingCurrency(existing);

            await SaveAsync();

            // Lira ya bu çalıştırmada yeni eklendi ya da zaten vardı.
            return newlyAddedLira ?? existing.GetValueOrDefault(CurrencyUnitCode.TRY);

            // Var olan birimleri Code'a göre sözlüğe alır ("zaten var mı?" hızlı kontrolü için).
            async Task<Dictionary<string, CurrencyUnit>> LoadCurrenciesByCode()
            {
                return (await _currencyUnitRepository.GetListAsync())
                    .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase); // kod karşılaştırması harf-duyarsız
            }

            // Listede olup DB'de olmayan her birimi ekler; eklediyse Lira'yı geri verir.
            async Task<CurrencyUnit?> AddEachMissingCurrency(Dictionary<string, CurrencyUnit> alreadyThere)
            {
                CurrencyUnit? lira = null;

                foreach (var spec in Units)
                {
                    if (alreadyThere.ContainsKey(spec.Code) == false)
                    {
                        var currency = CreateCurrency(spec);

                        if (IsTry(spec.Code))
                        {
                            lira = currency;
                        }

                        await AddCurrency(currency);
                    }
                }

                return lira;
            }

            // Tek bir birimi (henüz kaydetmeden) ekler.
            async Task AddCurrency(CurrencyUnit currency)
            {
                await _currencyUnitRepository.InsertAsync(currency, autoSave: false); // toplu ekle; kayıt sonda tek SaveAsync'te
            }
        }

        // Lira'nın başlangıç kuru (1/1) yoksa ekler.
        async Task GiveLiraStartingPriceIfMissing(CurrencyUnit? lira)
        {
            if (lira is not null && await HasPrice(lira) == false)
            {
                await AddStartingPrice(lira);
            }

            // Bu birimin DB'de kayıtlı bir kuru var mı?
            async Task<bool> HasPrice(CurrencyUnit currency)
            {
                return await _exchangeRateRepository.FindAsync(r => r.CurrencyUnitId == currency.Id) is not null;
            }

            // Birime başlangıç kuru satırını ekler ve kaydeder.
            async Task AddStartingPrice(CurrencyUnit currency)
            {
                await _exchangeRateRepository.InsertAsync(CreateStartingPrice(currency), autoSave: true); // tek kayıt, hemen yaz
            }
        }

        // Spec satırından bir CurrencyUnit nesnesi kurar (bakiye-gösterim bayrağıyla birlikte).
        CurrencyUnit CreateCurrency(CurrencySpec spec)
        {
            var currency = new CurrencyUnit(
                code: spec.Code,
                name: spec.Name,
                type: spec.Type,
                displayOrder: spec.Order);

            currency.SetAlwaysShowInBalance(spec.AlwaysShow);

            return currency;
        }

        // Birim için 1/1 başlangıç kuru nesnesini kurar (feed daha fiyat vermeden TRY kendine = 1 kalsın).
        ExchangeRate CreateStartingPrice(CurrencyUnit currency)
        {
            return new ExchangeRate(
                currencyUnitId: currency.Id,
                marketPriceOnBuy: 1m,                        // ham fiyat 1 (TRY = TRY)
                marketPriceOnSell: 1m,
                appliedMarginOnBuy: MarginSetting.Fixed(1m), // marj uygulanınca da 1
                appliedMarginOnSell: MarginSetting.Fixed(1m),
                source: "Seed",                              // feed değil, kurulum kaynağı
                rateDate: DateTime.UtcNow);
        }
    }

    /// <summary>Bir firmanın (host=null dahil) gördüğü her birime, henüz yoksa varsayılan marj ekler.</summary>
    public async Task SeedMarginsAsync(Guid? tenantId)
    {
        using (_currentTenant.Change(tenantId))      // yazma bu tenant kapsamında olsun (host = null)
        using (_dataFilter.Disable<IMultiTenant>())   // global + diğer tenant satırlarını da görebilmek için
        {
            await AddMissingMargins(
                currencies: await CurrenciesVisibleTo(tenantId),
                alreadyHaveMargin: await CurrencyIdsWithMargin(tenantId));

            await SaveAsync();
        }

        // Bu sahibin (host=null) gördüğü birimler: global + kendi birimleri.
        async Task<List<CurrencyUnit>> CurrenciesVisibleTo(Guid? owner)
        {
            return [.. (await _currencyUnitRepository.GetQueryableAsync())
                .Where(c => c.TenantId == null || c.TenantId == owner)]; // null = global, owner = firmanın kendi birimleri
        }

        // Bu sahip için zaten marjı olan birimlerin id'leri.
        async Task<HashSet<Guid>> CurrencyIdsWithMargin(Guid? owner)
        {
            return [.. (await _marginRepository.GetQueryableAsync())
                .Where(m => m.TenantId == owner)
                .Select(m => m.CurrencyUnitId)];
        }

        // Marjı olmayan her birime varsayılan marj ekler.
        async Task AddMissingMargins(List<CurrencyUnit> currencies, HashSet<Guid> alreadyHaveMargin)
        {
            foreach (var currency in currencies)
            {
                if (alreadyHaveMargin.Contains(currency.Id) == false)
                {
                    await AddMargin(currency);
                }
            }
        }

        // Tek bir birime varsayılan marjı (henüz kaydetmeden) ekler.
        async Task AddMargin(CurrencyUnit currency)
        {
            await _marginRepository.InsertAsync(CreateDefaultMargin(currency), autoSave: false);
        }

        // Birim için varsayılan marj nesnesi: TRY=Fixed(1), diğerleri Passthrough.
        // Alış ve satış AYRI nesne olmalı — EF her birini ayrı sahipli değer olarak saklar.
        CurrencyUnitMargin CreateDefaultMargin(CurrencyUnit currency)
        {
            return IsTry(currency.Code)
                ? new CurrencyUnitMargin(                       // TRY: alış/satış sabit 1 (kendine oranı)
                    currencyUnitId: currency.Id,
                    companyId: null,                           // host taban (global); company bazlı değil
                    marginOnBuy: MarginSetting.Fixed(1m),
                    marginOnSell: MarginSetting.Fixed(1m))
                : new CurrencyUnitMargin(                       // diğerleri: piyasayı aynen geçir (marj uygulama)
                    currencyUnitId: currency.Id,
                    companyId: null,                           // host taban (global); company bazlı değil
                    marginOnBuy: MarginSetting.Passthrough,
                    marginOnSell: MarginSetting.Passthrough);
        }
    }

    #endregion

    #region Helpers

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar.
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync(); // seed daima bir UoW içinde çalışır (Current! güvenli)
    }

    // Verilen kod Türk Lirası mı?
    private static bool IsTry(string code)
    {
        return string.Equals(code, CurrencyUnitCode.TRY, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Seed Data

    // Seed satırı: bir para biriminin tanımı (Type=Cash, AlwaysShow=false varsayılan).
    private sealed record CurrencySpec(
        string Code,
        string Name,
        int Order,
        CurrencyUnitType Type = CurrencyUnitType.Cash,
        bool AlwaysShow = false);

    // Feed'in (Harem) güvenilir verdiği birimler + Türk Lirası. JPY/KWD (bayat) ve RUB/AZN/CNY/RON/AED
    // (Harem vermez) bilerek listede yok.
    private static readonly CurrencySpec[] Units =
    [
        new(CurrencyUnitCode.HAS, "Has Altın",              1,  CurrencyUnitType.Metal, true),
        new(CurrencyUnitCode.TRY, "Türk Lirası",            2,  AlwaysShow: true),
        new(CurrencyUnitCode.USD, "Amerikan Doları",        3,  AlwaysShow: true),
        new(CurrencyUnitCode.EUR, "Euro",                   4,  AlwaysShow: true),
        new(CurrencyUnitCode.GBP, "İngiliz Sterlini",       5),
        new(CurrencyUnitCode.CHF, "İsviçre Frangı",         6),
        new(CurrencyUnitCode.SAR, "Suudi Arabistan Riyali", 7),
        new(CurrencyUnitCode.AUD, "Avustralya Doları",      8),
        new(CurrencyUnitCode.CAD, "Kanada Doları",          9),
        new(CurrencyUnitCode.GUM, "Has Gümüş",              10, CurrencyUnitType.Metal),
        new(CurrencyUnitCode.PLT, "Has Platin",             11, CurrencyUnitType.Metal),
        new(CurrencyUnitCode.PLD, "Has Paladyum",           12, CurrencyUnitType.Metal),
        // SAYIM birimi (2026-08-06 Hakan isteği): adet-bazlı stok/reçete satırları için hazır gelsin —
        // kullanıcı elle "AD" açamıyordu (eski 3-harf alt sınırı; CurrencyConsts.CodeMinLength=2 ile açıldı).
        new(CurrencyUnitCode.AD,  "Adet",                   13, CurrencyUnitType.Quantity, AlwaysShow: true),
    ];

    #endregion
}
