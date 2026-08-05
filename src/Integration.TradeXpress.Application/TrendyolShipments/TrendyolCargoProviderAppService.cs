using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.TrendyolShipments;

/// <summary>
/// Trendyol kargo firması AppService — host-global SALT OKUMA.
///
/// <para>Okuma <c>CurrentTenant.Change(null)</c> ile host'a sabitlenir: tablo <c>IMultiTenant</c> değil ama
/// db-per-tenant kurulumunda merkezî kaynağa bakıldığından emin olmak için N11 kargo/kategori okumalarıyla
/// AYNI sabitleme uygulanır.</para>
///
/// <para>Yazma ucu YOK — liste resmî statik kaynaktan seed edilir (bkz. <c>TrendyolCargoProviderSeeder</c>).
/// Kullanıcının düzenleyebileceği bir referans değildir: <c>ExternalId</c> Trendyol'un kimliğidir, elle
/// değiştirilirse ürün gövdesi pazaryerinde reddedilir.</para>
/// </summary>
public class TrendyolCargoProviderAppService : TradeXpressAppService, ITrendyolCargoProviderAppService
{
    private readonly IRepository<TrendyolCargoProvider, Guid> _repository;

    public TrendyolCargoProviderAppService(IRepository<TrendyolCargoProvider, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<List<TrendyolCargoProviderDto>> GetListAsync(bool includeInactive = false)
    {
        using (CurrentTenant.Change(null))
        {
            var query = await _repository.GetQueryableAsync();
            if (!includeInactive)
            {
                query = query.Where(p => p.IsActive);
            }

            var items = await AsyncExecuter.ToListAsync(query.OrderBy(p => p.Name));
            return items
                .Select(p => new TrendyolCargoProviderDto
                {
                    Id = p.Id,
                    ExternalId = p.ExternalId,
                    Code = p.Code,
                    Name = p.Name,
                    TaxNumber = p.TaxNumber,
                    IsActive = p.IsActive,
                })
                .ToList();
        }
    }
}
