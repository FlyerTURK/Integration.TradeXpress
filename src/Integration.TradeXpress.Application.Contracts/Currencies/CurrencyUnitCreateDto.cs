using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Yeni CurrencyUnit oluşturma. Entity zengin ctor + domain metotlarıyla kurulur
/// (AppService'te), bu yüzden düz alanlar taşır. Margin'i AppService VO'ya çevirir.
/// </summary>
public class CurrencyUnitCreateDto : ICreateDto
{
    [Required]
    [StringLength(CurrencyConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CurrencyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public CurrencyUnitType Type { get; set; } = CurrencyUnitType.Cash;

    [StringLength(CurrencyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool AlwaysShowInBalance { get; set; }

    public Guid? FollowingUnitId { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }
}
