using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Türk pazaryerlerinin (N11/Trendyol) para birimi olan <b>TRY</b>'nin <see cref="CurrencyUnit"/> kimliğini çözer.
///
/// <para><b>Neden ayrı tip:</b> <see cref="LocalCurrencyResolver"/> şirketin ÜLKESİNDEN türeyen yerel para birimini
/// verir — kur görüntüsünün re-base tabanı. Buradaki soru başka: pazaryeri fiyatı <b>her koşulda TRY</b>'dir
/// (satıcının ülkesi ne olursa olsun), o yüzden sabit TRY kodu aranır. İkisini karıştırmak, yurt dışı şirketin
/// N11 fiyatını yanlış birimde etiketlerdi.</para>
///
/// <para><b>Multi-tenant filtresi KAPALI okunur:</b> TRY tipik kurulumda HOST kaydıdır (CurrencyUnit host‖tenant
/// çapraz katalog) ve tenant data-filter'ı host satırını gizleyince fiyatlar para-birimsiz yazılıyordu (canlıda
/// yaşandı, 2026-07-11). Tenant kendi TRY'sini tanımlamışsa o tercih edilir.</para>
///
/// <para><b>null dönebilir</b> — TRY hiç tanımlı değilse. Çağıran bunu SESSİZ geçmemeli: fiyat para-birimsiz
/// (yerel birim semantiğiyle) yazılır, bu finansal olarak riskli bir düşüştür → rapora uyarı düşülmelidir.</para>
/// </summary>
public class MarketplaceCurrencyResolver : ITransientDependency
{
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public MarketplaceCurrencyResolver(
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _currencyUnitRepository = currencyUnitRepository;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>TRY para biriminin id'si — önce tenant'ın kendi kaydı, yoksa host kaydı; hiçbiri yoksa null.</summary>
    public async Task<Guid?> ResolveTryUnitIdAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var candidates = await _asyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(c => c.Code == CurrencyUnitCode.TRY));

            var preferred = candidates.FirstOrDefault(c => c.TenantId == _currentTenant.Id)
                            ?? candidates.FirstOrDefault(c => c.TenantId == null);
            return preferred?.Id;
        }
    }
}
