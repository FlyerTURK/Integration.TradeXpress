namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 REST yazma uçlarının değişmez sınırları — TEK KAYNAK (resmî doküman v9.0, §3.4/3.5/3.6).
/// Sınırlar N11 tarafında tanımlıdır; kanal/kiracı başına DEĞİŞMEZ, bu yüzden entity'de değil sabitte durur.
/// </summary>
public static class N11RestConsts
{
    /// <summary>
    /// <c>integrator</c> alanı — "API Kullanıcı/Entegratör Firma ismini yazınız. <b>Tüm gönderimlerinizde aynı ismi
    /// iletiniz.</b>" (doküman). Entegratör kimliğimizdir; ürün/kanal başına değişmez, bu yüzden sabittir.
    /// </summary>
    public const string Integrator = "TradeXpress";

    /// <summary>Tek istekte gönderilebilecek maksimum SKU sayısı ("Tek seferde maximum 1000 sku"). Üç yazma ucu için de aynı.</summary>
    public const int MaxSkusPerRequest = 1000;

    /// <summary><c>stockCode</c> maksimum uzunluğu ("Maksimum değeri 255").</summary>
    public const int MaxStockCodeLength = 255;

    /// <summary><c>quantity</c> maksimum değeri ("Maksimum değer 999.999").</summary>
    public const int MaxQuantity = 999_999;

    /// <summary>Fiyat alanlarında zorunlu ondalık hane sayısı — N11 aksi hâlde isteği REJECT eder.</summary>
    public const int PriceDecimals = 2;
}
