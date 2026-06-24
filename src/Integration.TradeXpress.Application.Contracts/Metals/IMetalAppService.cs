using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Metals;

public interface IMetalAppService : ICrudAppService<
    MetalGetDto,
    MetalListDto,
    Guid,
    MetalListRequestDto,
    MetalCreateDto,
    MetalUpdateDto>
{
    /// <summary>Maden süreç paneli combo'su için host‖own kayıtlar (birim düzeni + Factor desc + Code asc).</summary>
    Task<List<MetalListDto>> GetPickerListAsync();
}
