using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Currencies;

public interface ICurrencyUnitAppService : ICrudAppService<
    CurrencyUnitGetDto,
    CurrencyUnitListDto,
    Guid,
    CurrencyUnitListRequestDto,
    CurrencyUnitCreateDto,
    CurrencyUnitUpdateDto>
{
}
