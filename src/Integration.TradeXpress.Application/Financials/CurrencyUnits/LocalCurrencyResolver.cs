using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Çalışılan (working) şirketin YEREL para birimi kodunu çözer: Company.CountryId →
/// Country.DefaultCurrencyUnitId → CurrencyUnit.Code (TR→TRY, US→USD; id-only zincir).
/// Kur görüntüsü buna re-base edilir; ayrıca <b>yerel paraya marj YASAKTIR</b>
/// (re-base identity koruması — yerel daima 1.00). Working şirket/ülke yoksa null (host → çağıran
/// pivot TRY varsayar). Tek kaynak: hem <see cref="EffectivePriceAppService"/> hem marj guard kullanır (DRY).
/// </summary>
public class LocalCurrencyResolver : ITransientDependency
{
    private readonly ICurrentCompany _currentCompany;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public LocalCurrencyResolver(
        ICurrentCompany currentCompany,
        IRepository<Company, Guid> companyRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _currentCompany = currentCompany;
        _companyRepository = companyRepository;
        _countryRepository = countryRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _dataFilter = dataFilter;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Working şirketin yerel para birimi kodu (TR→TRY, US→USD); şirket/ülke/birim yoksa null.</summary>
    public async Task<string?> ResolveCodeAsync()
    {
        if (_currentCompany.Id is not { } companyId)
            return null;

        var company = await _companyRepository.FindAsync(companyId);
        if (company?.CountryId is not { } countryId)
            return null;

        using (_dataFilter.Disable<IMultiTenant>())
        {
            // id-only zincir: Country.DefaultCurrencyUnitId → CurrencyUnit.Code (tek sorgu, join).
            var countries = await _countryRepository.GetQueryableAsync();
            var units = await _currencyUnitRepository.GetQueryableAsync();
            return await _asyncExecuter.FirstOrDefaultAsync(
                countries
                    .Where(ct => ct.Id == countryId)
                    .Join(units, ct => ct.DefaultCurrencyUnitId, u => (Guid?)u.Id, (ct, u) => u.Code));
        }
    }
}
