using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Cashes;

public interface ICashAppService : ICrudAppService<
    CashGetDto,
    CashListDto,
    Guid,
    CashListRequestDto,
    CashCreateDto,
    CashUpdateDto>
{
    Task<List<CashListDto>> GetPickerListAsync();
}
