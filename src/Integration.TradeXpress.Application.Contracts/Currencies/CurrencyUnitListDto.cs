using System;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// CurrencyUnit grid satırı (KİMLİK). Alış/satış marjı burada DEĞİL — per-tenant
/// <see cref="CurrencyUnitMargin"/>'de. <see cref="IsGlobal"/>: host kataloğu (TenantId=null) mu.
/// </summary>
public class CurrencyUnitListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CurrencyUnitType Type { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Yapısal/global takip (varsa parent'tan türetilir).</summary>
    public Guid? FollowingUnitId { get; set; }
    
    public string? FollowingUnitCode { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }

    /// <summary>Host kataloğu (TenantId=null) mu? Tenant bunu düzenleyemez; salt-okur.</summary>
    public bool IsGlobal { get; set; }
}
