namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>Pazaryeri anlaşmalı kargo tarifesi (host-global) alan sınırları ve sayısal kesinlikler.</summary>
public static class MarketplaceShipmentTariffConsts
{
    /// <summary>Tarifenin KENDİ nötr taşıyıcı kodu (ARAS/SURAT/PTT/YURTICI/KOLAYGELSIN/DHL).
    /// Pazaryerinin firma kimliğinden BAĞIMSIZDIR — o kimlik gevşek köprüde tutulur.</summary>
    public const int CarrierCodeMaxLength = 32;

    public const int CarrierNameMaxLength = 128;

    /// <summary>Pazaryerindeki kargo firması kimliği (ör. N11ShipmentCompany.ExternalId) — gevşek köprü.</summary>
    public const int ChannelCompanyExternalIdMaxLength = 16;

    /// <summary>Tarife sürüm etiketi (ör. "2026-07-26") — hangi yayından geldiği izlenebilsin.</summary>
    public const int SourceVersionMaxLength = 32;

    /// <summary>Para tutarları: 18,2 (repo geneli para deseni).</summary>
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;

    /// <summary>Oranlar (KDV 0,20 · posta hizmet bedeli 0,0235 · başarısız teslimat 0,50): 9,4.</summary>
    public const int RatePrecision = 9;
    public const int RateScale = 4;

    /// <summary>Tarife tablosunun son satırı; üstü <c>OverflowIncrementAmount</c> ile doğrusal uzatılır.
    /// N11 yayınında 100 (desi 0 = "Dosya" satırı).</summary>
    public const int TabulatedMaxDesi = 100;

    /// <summary>Desi 0 = pazaryerinin "Dosya" satırı (ağırlıksız/küçük gönderi).</summary>
    public const int DocumentDesi = 0;
}
