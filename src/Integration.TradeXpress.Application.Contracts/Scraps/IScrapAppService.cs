using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Scraps;

public interface IScrapAppService : ICrudAppService<
    ScrapGetDto,
    ScrapListDto,
    Guid,
    ScrapListRequestDto,
    ScrapCreateDto,
    ScrapUpdateDto>
{
    /// <summary>Hurda süreç paneli combo'su için host‖own kayıtlar (birim düzeni + Factor desc + Code asc).</summary>
    Task<List<ScrapListDto>> GetPickerListAsync();
}
