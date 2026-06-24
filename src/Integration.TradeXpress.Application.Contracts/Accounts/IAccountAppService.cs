using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Accounts;

public interface IAccountAppService : ICrudAppService<
    AccountGetDto,
    AccountListDto,
    Guid,
    AccountListRequestDto,
    AccountCreateDto,
    AccountUpdateDto>
{
}
