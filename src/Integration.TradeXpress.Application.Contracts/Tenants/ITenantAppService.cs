using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Tenants;

public interface ITenantAppService : ICrudAppService<
    TenantGetDto, 
    TenantListDto, 
    Guid, 
    TenantListRequestDto, 
    TenantCreateDto, 
    TenantUpdateDto>
{
}
