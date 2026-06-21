using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits.Models;

/// <summary>
/// CurrencyUnit düzenleme ekranı view-model'i. Margin VO'ları düz alanlara açılmış
/// (UI binding kolaylığı); AppService bunları yeniden VO'ya çevirir.
/// </summary>
public class CurrencyUnitViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }

    [Display(Name = "Code")]
    [Required]
    [StringLength(CurrencyConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Name")]
    [Required]
    [StringLength(CurrencyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public CurrencyUnitType Type { get; set; } = CurrencyUnitType.Cash;

    [Display(Name = "Description")]
    [StringLength(CurrencyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; } = 99;

    /// <summary>Sistem birimi mi (Code kilitli, silinemez).</summary>
    public bool IsSystem { get; set; }

    /// <summary>Host kataloğu mu (tenant düzenleyemez).</summary>
    public bool IsGlobal { get; set; }

    // Alış/satış marjı burada DEĞİL (per-tenant CurrencyUnitMargin ekranında). Yapısal following kalır.
    public Guid? FollowingUnitId { get; set; }
    public MarginType? FollowingMarginType { get; set; }
    public decimal? FollowingMarginValue { get; set; }
}
