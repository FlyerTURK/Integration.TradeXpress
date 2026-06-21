using System;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Tenants;

public class TenantGetDto : Volo.Abp.Application.Dtos.EntityDto<Guid>, IGetDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    
    // Yalnızca Create işleminde UI binding için kullanılır.
    public string AdminEmailAddress { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;

    // Akıllı Zıplama için
    public int PageIndex { get; set; }
}
