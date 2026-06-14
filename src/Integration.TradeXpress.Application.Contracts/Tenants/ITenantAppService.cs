using System;
using Integration.Framework.Base.Dtos.Interfaces;
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
