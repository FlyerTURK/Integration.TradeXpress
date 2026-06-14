using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Branches;

public interface IBranchAppService : ICrudAppService<
    BranchGetDto,
    BranchListDto,
    Guid,
    BranchListRequestDto,
    BranchCreateDto,
    BranchUpdateDto>
{
}
