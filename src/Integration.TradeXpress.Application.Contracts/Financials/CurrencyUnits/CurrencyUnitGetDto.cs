using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// CurrencyUnit detay/edit DTO'su. Margin VO'ları düzleştirilmiş; takip (follow)
/// alanları nullable. Edit formu buna bağlanır (agnostic Form GetDto'yu doğrular; Create/Update ile aynı kurallar).
/// </summary>
public class CurrencyUnitGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(CurrencyConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CurrencyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;
    public CurrencyUnitType Type { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(CurrencyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Bakiye listesinde her zaman gösterilsin mi.</summary>
    public bool AlwaysShowInBalance { get; set; }

    public Guid? FollowingUnitId { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }

    public bool IsGlobal { get; set; }
}
