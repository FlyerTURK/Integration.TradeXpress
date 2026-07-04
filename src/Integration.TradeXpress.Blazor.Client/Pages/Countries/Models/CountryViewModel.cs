using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Countries;

namespace Integration.TradeXpress.Blazor.Client.Pages.Countries.Models;

/// <summary>Ülke düzenleme view-model'i.</summary>
public class CountryViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }

    [Display(Name = "Code")]
    [Required]
    [StringLength(CountryConsts.CodeMaxLength, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Name")]
    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Varsayılan para birimi — CurrencyUnit'e id-only referans (otoriter alan).</summary>
    public Guid? DefaultCurrencyUnitId { get; set; }

    /// <summary>Görüntü alanı — id'den çözülen birim kodu (server doldurur).</summary>
    public string? DefaultCurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
