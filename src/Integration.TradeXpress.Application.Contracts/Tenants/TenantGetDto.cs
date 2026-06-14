using System;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Tenants;

public class TenantGetDto : Volo.Abp.Application.Dtos.EntityDto<Guid>, IGetDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    // Akıllı Zıplama için
    public int PageIndex { get; set; }
}
