using System;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Parite panosu satırı: <c>1 Base = X Quote</c> canlı çapraz kuru (birimlerin efektif
/// fiyatının bid/ask çaprazından). Id = Parity id. Yön çift konvansiyonudur (forex).
/// </summary>
public class ParityBoardDto : EntityDto<Guid>
{
    /// <summary>Çift kodu, ör. "USDTRY", "EURUSD".</summary>
    public string Code { get; set; } = string.Empty;

    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCode { get; set; } = string.Empty;
    public Guid QuoteCurrencyUnitId { get; set; }
    public string QuoteCode { get; set; } = string.Empty;

    /// <summary>Çapraz kur (1 base = X quote).</summary>
    public decimal Buy { get; set; }
    public decimal Sell { get; set; }

    public bool GuardFired { get; set; }
    public int DisplayOrder { get; set; }
}
