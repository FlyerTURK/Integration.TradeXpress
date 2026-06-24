using System;
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
}
