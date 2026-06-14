using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Countries;

public interface ICountryAppService : ICrudAppService<
    CountryGetDto,
    CountryListDto,
    Guid,
    CountryListRequestDto,
    CountryCreateDto,
    CountryUpdateDto>
{
}
