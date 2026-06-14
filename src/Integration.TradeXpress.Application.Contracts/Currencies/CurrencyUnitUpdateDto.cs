using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// CurrencyUnit güncelleme. Code burada YOK — sistem birimlerinde değiştirilemez,
/// kullanıcı birimlerinde de kimlik sabit tutulur (gerekirse ayrı uç eklenir).
/// </summary>
public class CurrencyUnitUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(CurrencyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(CurrencyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    public Guid? FollowingUnitId { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }
}
