using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Yeni parite oluşturma. Çift = base/quote; aynı çiftin ters yönü (USDTRY varken TRYUSD)
/// AppService/ParityManager'da reddedilir. Oran taşımaz — birim fiyatından türetilir.
/// </summary>
public class ParityCreateDto : ICreateDto
{
    [Required]
    public Guid BaseCurrencyUnitId { get; set; }

    [Required]
    public Guid QuoteCurrencyUnitId { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
