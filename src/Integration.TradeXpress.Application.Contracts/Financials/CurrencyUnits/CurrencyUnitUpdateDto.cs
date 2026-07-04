using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// CurrencyUnit güncelleme. Code TENANT birimlerinde DÜZENLENEBİLİR (ürün kuralı 2026-07-04);
/// HOST (global) birimin kodu DEĞİŞTİRİLEMEZ — sunucu <c>HostCodeLocked</c> ile reddeder.
/// </summary>
public class CurrencyUnitUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(CurrencyConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CurrencyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(CurrencyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    public bool AlwaysShowInBalance { get; set; }

    public Guid? FollowingUnitId { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }
}
