using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Vaults;

public interface IVaultAppService : ICrudAppService<
    VaultGetDto,
    VaultListDto,
    Guid,
    VaultListRequestDto,
    VaultCreateDto,
    VaultUpdateDto>
{
}
