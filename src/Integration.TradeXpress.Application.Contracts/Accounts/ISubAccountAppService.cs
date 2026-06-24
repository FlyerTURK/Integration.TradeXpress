using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Accounts;

public interface ISubAccountAppService : ICrudAppService<
    SubAccountGetDto,
    SubAccountListDto,
    Guid,
    SubAccountListRequestDto,
    SubAccountCreateDto,
    SubAccountUpdateDto>
{
}
