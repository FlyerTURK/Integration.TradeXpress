namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>Bir birimin sınıfı: nakit döviz mi, kıymetli maden mi, sayım birimi mi.</summary>
public enum CurrencyUnitType
{
    Cash = 0,
    Metal = 1,

    /// <summary>SAYIM birimi (Adet gibi) — 2026-08-06 Hakan isteğiyle eklendi. Ne nakit ne maden: kuru yoktur
    /// (değerleme ağı kursuz birimi zaten eler — <c>EffPrice.RateMissing</c>), <c>CashSeeder</c> ondan kasa
    /// AÇMAZ (filtresi <see cref="Cash"/>) ve döviz picker'larında görünmez.</summary>
    Quantity = 2,
}
