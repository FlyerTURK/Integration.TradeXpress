using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Futures;

public interface IFutureAppService : ICrudAppService<
    FutureGetDto,
    FutureListDto,
    Guid,
    FutureListRequestDto,
    FutureCreateDto,
    FutureUpdateDto>
{
    /// <summary>Vadeli süreç paneli combo'su için host‖own kayıtlar (koda göre sıralı, pasifler dahil).</summary>
    Task<List<FutureListDto>> GetPickerListAsync();
}
