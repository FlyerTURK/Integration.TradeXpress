using System;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir birimin <b>aktif şirketin base para birimi</b> cinsinden değeri (re-base/değerleme).
/// Base=USD ise USD=1, TRY≈0.0277… Bu DEĞERLEME görünümüdür — parite panosu (forex yönü) AYRIDIR.
/// Id = CurrencyUnitId.
/// </summary>
public class ValuationPriceDto : EntityDto<Guid>
{
    public string CurrencyUnitCode { get; set; } = string.Empty;
    public string CurrencyUnitName { get; set; } = string.Empty;
    public CurrencyUnitType UnitType { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Birimin base cinsinden değeri (1 birim = X base).</summary>
    public decimal Buy { get; set; }
    public decimal Sell { get; set; }

    /// <summary>Değerleme para birimi (aktif şirketin base'i).</summary>
    public string BaseCurrencyCode { get; set; } = string.Empty;

    public bool GuardFired { get; set; }
}
