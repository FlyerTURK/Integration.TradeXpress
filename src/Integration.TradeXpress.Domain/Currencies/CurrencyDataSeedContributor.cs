using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Organization;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Döviz/maden seed'i. İki sorumluluk, tek contributor (sıra garantisi için birlikte):
/// <list type="number">
/// <item><b>Kimlik kataloğu</b> (yalnız host, TenantId=null, IsSystem=true): Harem-güvenilir
/// birimler + pivot TRY. TRY için ilk ExchangeRate (raw fiyat 1).</item>
/// <item><b>Per-tenant marj</b> (host=null dahil HER tenant): görünür her birime varsayılan
/// <see cref="CurrencyUnitMargin"/> (Multiply 1; TRY=FinalPrice 1).</item>
/// </list>
/// Idempotent. Tenant oluşturulunca ABP bu contributor'ı o tenant scope'unda çağırır →
/// (1) atlanır (host-only), (2) o tenant'ın marjları seed'lenir.
/// </summary>
public class CurrencyDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly IRepository<CurrencyUnitMargin, Guid> _marginRepository;
    private readonly IRepository<ExchangeRate, Guid> _exchangeRateRepository;
    private readonly IRepository<Parity, Guid> _parityRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly OrgTreeManager _orgTree;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public CurrencyDataSeedContributor(
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        IRepository<CurrencyUnitMargin, Guid> marginRepository,
        IRepository<ExchangeRate, Guid> exchangeRateRepository,
        IRepository<Parity, Guid> parityRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<Country, Guid> countryRepository,
        OrgTreeManager orgTree,
        IGuidGenerator guidGenerator,
        IDataFilter dataFilter,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _currencyUnitRepository = currencyUnitRepository;
        _marginRepository = marginRepository;
        _exchangeRateRepository = exchangeRateRepository;
        _parityRepository = parityRepository;
        _companyRepository = companyRepository;
        _countryRepository = countryRepository;
        _orgTree = orgTree;
        _guidGenerator = guidGenerator;
        _dataFilter = dataFilter;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    // Harem-güvenilir birimler + TRY. JPY/KWD (Harem bayat) ve RUB/AZN/CNY/RON/AED
    // (Harem vermez, Altınkaynak-only) BİLİNÇLİ olarak dışarıda.
    private static readonly (string Code, string Name, CurrencyUnitType Type, int Order)[] Units =
    {
        (CurrencyUnitCode.HAS, "Has Altın",              CurrencyUnitType.Metal, 1),
        (CurrencyUnitCode.TRY, "Türk Lirası",            CurrencyUnitType.Cash,  2),
        (CurrencyUnitCode.USD, "Amerikan Doları",        CurrencyUnitType.Cash,  3),
        (CurrencyUnitCode.EUR, "Euro",                   CurrencyUnitType.Cash,  4),
        (CurrencyUnitCode.GBP, "İngiliz Sterlini",       CurrencyUnitType.Cash,  5),
        (CurrencyUnitCode.CHF, "İsviçre Frangı",         CurrencyUnitType.Cash,  6),
        (CurrencyUnitCode.SAR, "Suudi Arabistan Riyali", CurrencyUnitType.Cash,  7),
        (CurrencyUnitCode.AUD, "Avustralya Doları",      CurrencyUnitType.Cash,  8),
        (CurrencyUnitCode.CAD, "Kanada Doları",          CurrencyUnitType.Cash,  9),
        (CurrencyUnitCode.GUM, "Has Gümüş",              CurrencyUnitType.Metal, 10),
        (CurrencyUnitCode.PLT, "Has Platin",             CurrencyUnitType.Metal, 11),
        (CurrencyUnitCode.PLD, "Has Paladyum",           CurrencyUnitType.Metal, 12),
    };

    public async Task SeedAsync(DataSeedContext context)
    {
        // (1) Merkezi referans yalnız host'ta; tenant'lar paylaşır (null‖own).
        if (context.TenantId == null)
        {
            await SeedIdentityCatalogAsync();
            await SeedParitiesAsync();   // host-global pariteler
            await SeedCountriesAsync();  // host-global ülke kataloğu
        }

        // (2) Marjlar her tenant'ta (host dahil) — host'un merkezi ① düzeltme marjı da burada.
        await SeedMarginsAsync(context.TenantId);

        // (3) Şirket/şube TENANT'a aittir — host'ta company YOK (host: tenant + merkezi operasyon).
        //     Tenant için varsayılan HQ (Merkez/TR/TRY) fallback; ülke seçimi UI'dan güncellenir.
        if (context.TenantId != null)
            await SeedHqCompanyAsync(context.TenantId);
    }

    private async Task SeedHqCompanyAsync(Guid? tenantId)
    {
        using (_currentTenant.Change(tenantId))
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var hasHq = (await _companyRepository.GetQueryableAsync())
                .Any(c => c.TenantId == tenantId && c.IsHeadquarters);

            if (!hasHq)
            {
                // Base = global TRY (pivot). Yoksa (units henüz yok) atla — sonraki run kurar.
                var tryUnit = (await _currencyUnitRepository.GetQueryableAsync())
                    .FirstOrDefault(u => u.TenantId == null && u.Code == CurrencyUnitCode.TRY);
                if (tryUnit == null)
                    return;

                var company = new Company(
                    _guidGenerator.Create(),
                    code: "MRK",
                    name: "Merkez",
                    countryCode: "TR",
                    baseCurrencyUnitId: tryUnit.Id,
                    isHeadquarters: true,
                    displayOrder: 1,
                    tenantId: tenantId);

                await _companyRepository.InsertAsync(company, autoSave: true);
            }

            // Her şirket en az bir HQ şube + varsayılan kasayla yaşamalı (mevcutların backfill'i dahil).
            var companies = (await _companyRepository.GetQueryableAsync())
                .Where(c => c.TenantId == tenantId)
                .ToList();
            foreach (var company in companies)
                await _orgTree.EnsureHeadquartersBranchAsync(company);
        }
    }

    private async Task SeedIdentityCatalogAsync()
    {
        var existing = (await _currencyUnitRepository.GetListAsync())
            .ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);

        CurrencyUnit? tryUnit = existing.GetValueOrDefault(CurrencyUnitCode.TRY);

        foreach (var spec in Units)
        {
            if (existing.ContainsKey(spec.Code))
                continue;

            var unit = new CurrencyUnit(
                _guidGenerator.Create(),
                spec.Code,
                spec.Name,
                spec.Type,
                isSystem: true,
                displayOrder: spec.Order);

            if (string.Equals(spec.Code, CurrencyUnitCode.TRY, StringComparison.OrdinalIgnoreCase))
                tryUnit = unit;

            await _currencyUnitRepository.InsertAsync(unit, autoSave: false);
        }

        await _unitOfWorkManager.Current!.SaveChangesAsync();

        // TRY ilk raw fiyatı (feed vermez → pivot önlemi, daima 1).
        if (tryUnit != null &&
            await _exchangeRateRepository.FindAsync(r => r.CurrencyUnitId == tryUnit.Id) is null)
        {
            var rate = new ExchangeRate(
                _guidGenerator.Create(),
                tryUnit.Id,
                marketPriceOnBuy: 1m,
                marketPriceOnSell: 1m,
                appliedMarginOnBuy: MarginSetting.Fixed(1m),
                appliedMarginOnSell: MarginSetting.Fixed(1m),
                source: "Seed",
                rateDate: DateTime.UtcNow);

            await _exchangeRateRepository.InsertAsync(rate, autoSave: true);
        }
    }

    private async Task SeedParitiesAsync()
    {
        // Host birimleri → uyumlu tüm çiftler (C(n,2)), öncelik diziyle yönlü (base=güçlü).
        var units = (await _currencyUnitRepository.GetQueryableAsync())
            .Where(u => u.TenantId == null)
            .ToList();
        var idByCode = units.ToDictionary(u => u.Code, u => u.Id, StringComparer.OrdinalIgnoreCase);

        var existing = (await _parityRepository.GetQueryableAsync())
            .Where(p => p.TenantId == null)
            .Select(p => new { p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId })
            .ToList()
            .Select(x => (x.BaseCurrencyUnitId, x.QuoteCurrencyUnitId))
            .ToHashSet();

        var codes = units.Select(u => u.Code).ToList();
        var order = 1;
        for (var i = 0; i < codes.Count; i++)
        {
            for (var j = i + 1; j < codes.Count; j++)
            {
                var (baseCode, quoteCode) = CurrencyUnitPriority.Direct(codes[i], codes[j]);
                var baseId = idByCode[baseCode];
                var quoteId = idByCode[quoteCode];

                if (existing.Contains((baseId, quoteId)))
                    continue;

                await _parityRepository.InsertAsync(
                    new Parity(_guidGenerator.Create(), baseId, quoteId,
                        isSystem: true, isActive: true, displayOrder: order++),
                    autoSave: false);
            }
        }

        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    // Ülke kataloğu (host-global). DefaultCurrency yalnız desteklediğimiz birimlerde (HQ base önerisi).
    private static readonly (string Code, string Name, string? Currency)[] CountryCatalog =
    {
        ("TR", "Türkiye",                        CurrencyUnitCode.TRY),
        ("US", "Amerika Birleşik Devletleri",    CurrencyUnitCode.USD),
        ("DE", "Almanya",                        CurrencyUnitCode.EUR),
        ("FR", "Fransa",                         CurrencyUnitCode.EUR),
        ("IT", "İtalya",                         CurrencyUnitCode.EUR),
        ("NL", "Hollanda",                       CurrencyUnitCode.EUR),
        ("ES", "İspanya",                        CurrencyUnitCode.EUR),
        ("GB", "Birleşik Krallık",               CurrencyUnitCode.GBP),
        ("CH", "İsviçre",                        CurrencyUnitCode.CHF),
        ("SA", "Suudi Arabistan",                CurrencyUnitCode.SAR),
        ("AU", "Avustralya",                     CurrencyUnitCode.AUD),
        ("CA", "Kanada",                         CurrencyUnitCode.CAD),
        ("AE", "Birleşik Arap Emirlikleri",      null),
        ("JP", "Japonya",                        null),
        ("RU", "Rusya",                          null),
        ("CN", "Çin",                            null),
        ("AZ", "Azerbaycan",                     null),
    };

    private async Task SeedCountriesAsync()
    {
        var existing = (await _countryRepository.GetQueryableAsync())
            .Where(c => c.TenantId == null)
            .Select(c => c.Code)
            .ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var order = 1;
        foreach (var (code, name, ccy) in CountryCatalog)
        {
            if (existing.Contains(code)) { order++; continue; }
            await _countryRepository.InsertAsync(
                new Country(_guidGenerator.Create(), code, name, ccy, displayOrder: order++),
                autoSave: false);
        }

        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    private async Task SeedMarginsAsync(Guid? tenantId)
    {
        using (_currentTenant.Change(tenantId))
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // Görünür birimler: global + bu tenant'ın kendi birimleri.
            var units = (await _currencyUnitRepository.GetQueryableAsync())
                .Where(u => u.TenantId == null || u.TenantId == tenantId)
                .ToList();

            var existing = (await _marginRepository.GetQueryableAsync())
                .Where(m => m.TenantId == tenantId)
                .Select(m => m.CurrencyUnitId)
                .ToHashSet();

            foreach (var unit in units)
            {
                if (existing.Contains(unit.Id))
                    continue;

                var isTry = string.Equals(unit.Code, CurrencyUnitCode.TRY, StringComparison.OrdinalIgnoreCase);
                var margin = isTry ? MarginSetting.Fixed(1m) : MarginSetting.Passthrough;

                var row = new CurrencyUnitMargin(
                    _guidGenerator.Create(),
                    unit.Id,
                    marginOnBuy: margin,
                    marginOnSell: isTry ? MarginSetting.Fixed(1m) : MarginSetting.Passthrough,
                    tenantId: tenantId);

                await _marginRepository.InsertAsync(row, autoSave: false);
            }

            await _unitOfWorkManager.Current!.SaveChangesAsync();
        }
    }
}
