using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Services;

public interface IServiceAppService : ICrudAppService<
    ServiceGetDto,
    ServiceListDto,
    Guid,
    ServiceListRequestDto,
    ServiceCreateDto,
    ServiceUpdateDto>
{
    /// <summary>Hizmet süreç paneli combo'su için host‖own kayıtlar (koda göre sıralı, pasifler dahil).</summary>
    Task<List<ServiceListDto>> GetPickerListAsync();
}
