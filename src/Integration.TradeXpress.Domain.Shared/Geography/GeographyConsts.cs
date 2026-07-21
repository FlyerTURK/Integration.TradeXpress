namespace Integration.TradeXpress.Geography;

/// <summary>
/// Çekirdek coğrafya taksonomisi (idari alan/yerellik/alt-yerellik — ISO 3166-2 hizalı) alan sınırları.
/// N11 il/ilçe id'leri + ISO alt-bölüm kodları buradan boyutlanır (SSOT).
/// </summary>
public static class GeographyConsts
{
    /// <summary>Kaynak kod (N11 il/ilçe id'si ya da ISO alt-bölüm kısaltması) — numerik/kısa string.</summary>
    public const int CodeMaxLength = 32;

    public const int NameMaxLength = 128;

    /// <summary>ISO 3166-2 alt-bölüm kodu (ör. TR-34, US-AL) — ülke alpha-2 + "-" + alt-bölüm.</summary>
    public const int Iso3166_2CodeMaxLength = 16;

    /// <summary>İdari-alan sınıfı (ör. province/state) — serbest metin sınıflandırma.</summary>
    public const int CategoryMaxLength = 64;

    /// <summary>Bilinen idari-alan sınıfları (ISO alt-bölüm kategorileri).</summary>
    public const string CategoryProvince = "province";

    public const string CategoryState = "state";

    /// <summary>Sembolik ana alan sınıfı — dataset'te alt-bölümü olmayan ülkede şehirlerin bağlandığı
    /// tek yapay idari alan (UI, <c>Country.UsesAdministrativeArea=false</c> ile state katmanını gizler).</summary>
    public const string CategoryMain = "main";

    /// <summary>Sembolik ana alanın sabit kodu (ülke başına en fazla bir tane; ISO kodu YOK).</summary>
    public const string SymbolicMainAreaCode = "MAIN";
}
