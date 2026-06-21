using System;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// CurrencyUnit detay/edit DTO'su. Margin VO'ları düzleştirilmiş; takip (follow)
/// alanları nullable. Edit formu buna bağlanır.
/// </summary>
public class CurrencyUnitGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CurrencyUnitType Type { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Bakiye listesinde her zaman gösterilsin mi.</summary>
    public bool AlwaysShowInBalance { get; set; }

    public Guid? FollowingUnitId { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }

    public bool IsGlobal { get; set; }

    // Akıllı Zıplama için
    public int PageIndex { get; set; }
}
