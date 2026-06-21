using System;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir birimin <b>viewer'a göre güncel efektif fiyatı</b>: ham pivot (host feed) →
/// kademe (host marjı → viewer marjı) sonucu. Id = CurrencyUnitId.
/// </summary>
public class CurrentPriceDto : EntityDto<Guid>
{
    public string CurrencyUnitCode { get; set; } = string.Empty;
    public string CurrencyUnitName { get; set; } = string.Empty;
    public CurrencyUnitType UnitType { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Viewer'ın efektif alış/satışı (kademe uygulanmış).</summary>
    public decimal Buy { get; set; }
    public decimal Sell { get; set; }

    /// <summary>Host ham piyasa fiyatı (pivot TRY) — referans (baz fiyat).</summary>
    public decimal RawBuy { get; set; }
    public decimal RawSell { get; set; }

    /// <summary>Baz fiyata uygulanan (en üst/yapılandırılmış) marj — alış/satış ayrı. Yoksa Passthrough (Multiply 1).</summary>
    public MarginType MarginOnBuyType { get; set; }
    public decimal MarginOnBuyValue { get; set; }
    public MarginType MarginOnSellType { get; set; }
    public decimal MarginOnSellValue { get; set; }

    /// <summary>Kademede bir yerde felaket guard'ı (alış>satış→takas) tetiklendi mi.</summary>
    public bool GuardFired { get; set; }

    /// <summary>Ham fiyatın ait olduğu pencere (host feed).</summary>
    public DateTime RateDate { get; set; }
}
