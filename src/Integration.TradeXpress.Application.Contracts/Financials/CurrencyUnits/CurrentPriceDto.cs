using System;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

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

    /// <summary>Birimin HİÇBİR kur bağlantısı yok (ne canlı tick, ne DB kuru, ne takip zinciri).
    /// <b>true ise <see cref="Buy"/>/<see cref="Sell"/> 1/1 YER TUTUCUdur, kur DEĞİLDİR</b> — ekranda sayı
    /// yerine "kur yok" yazılır. Eksik kuru gerçek 1:1 kurundan ayırt edebilmek için (2026-08-05: bayrak
    /// yokken HAS kursuz olduğu halde "1" görünüyordu ve 7 gram has altın 7 TRY'ye fiyatlanıyordu).</summary>
    public bool RateMissing { get; set; }
}
