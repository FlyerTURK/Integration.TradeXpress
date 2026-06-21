using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

public interface ICurrencyUnitAppService : ICrudAppService<
    CurrencyUnitGetDto,
    CurrencyUnitListDto,
    Guid,
    CurrencyUnitListRequestDto,
    CurrencyUnitCreateDto,
    CurrencyUnitUpdateDto>
{
}
