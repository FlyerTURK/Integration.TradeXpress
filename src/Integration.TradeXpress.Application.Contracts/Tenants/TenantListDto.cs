using System;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Tenants;

public class TenantListDto : Volo.Abp.Application.Dtos.EntityDto<Guid>, IListDto<Guid>
{
    public string Name { get; set; } = string.Empty;
}
