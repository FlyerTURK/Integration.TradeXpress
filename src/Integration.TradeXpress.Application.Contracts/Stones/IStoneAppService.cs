using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Stones;

public interface IStoneAppService : ICrudAppService<
    StoneGetDto,
    StoneListDto,
    Guid,
    StoneListRequestDto,
    StoneCreateDto,
    StoneUpdateDto>
{
    /// <summary>Taş süreç paneli combo'su için host + çalışılan şirkete-özel kayıtlar (koda göre sıralı).</summary>
    Task<List<StoneListDto>> GetPickerListAsync(Guid? companyId = null);
}
