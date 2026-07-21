using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Çekirdek kargo firması (<see cref="Carrier"/>) — <b>host-global SALT-OKUMA</b> servisi (N11City deseni).
/// Kargo şablonu formundaki firma picker'ını besler. CRUD YOK (katalog <c>CarrierSeeder</c> ile N11 firmalarından
/// türetilir). Carrier IMultiTenant DEĞİL → tenant filtresi yok; okuma yine de host'a sabitlenir
/// (<see cref="ICurrentTenant.Change(Guid?)"/> null; db-per-tenant'a karşı merkezilik garantisi). Picker şablon
/// bağlamında kullanıldığından yetki <see cref="TradeXpressPermissions.ShipmentTemplates.Default"/> (yeni izin YOK).
/// </summary>
[Authorize(TradeXpressPermissions.ShipmentTemplates.Default)]
public class CarrierAppService : TradeXpressAppService, ICarrierAppService
{
    private readonly IRepository<Carrier, Guid> _repository;

    public CarrierAppService(IRepository<Carrier, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<List<CarrierListDto>> GetListAsync()
    {
        // Host-global okuma → host'a sabitle (N11CityAppService deseni).
        using (CurrentTenant.Change(null))
        {
            var items = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync()).OrderBy(x => x.Name));
            return items.Select(x => ObjectMapper.Map<Carrier, CarrierListDto>(x)).ToList();
        }
    }

    public virtual async Task<CarrierDto> GetAsync(Guid id)
    {
        using (CurrentTenant.Change(null))
        {
            return ObjectMapper.Map<Carrier, CarrierDto>(await _repository.GetAsync(id));
        }
    }
}
