using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Çekirdek kargo firması (<see cref="Carrier"/>) — <b>host-global SALT-OKUMA</b> referans servisi (N11City deseni).
/// Kargo şablonu formundaki kargo-firması picker'ını besler; CRUD YOK (katalog N11 firmalarından türetilir —
/// <c>CarrierSeeder</c>). Tüm tenant'lar tek host kataloğunu paylaşır.
/// </summary>
public interface ICarrierAppService : IApplicationService
{
    /// <summary>Tüm kargo firmaları (host-global, DB'den; Name sıralı) — combo/picker verisi.</summary>
    Task<List<CarrierListDto>> GetListAsync();

    /// <summary>Tek kargo firması (host-global) — id'den restore.</summary>
    Task<CarrierDto> GetAsync(Guid id);
}
