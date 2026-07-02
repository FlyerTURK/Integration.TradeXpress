using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Efektif fiyat okuma — ham ExchangeRate (host pivot) + marj olaylarını kademeyle birleştirir.
/// Viewer'a göre (host/tenant) hesaplar; gizlilik ve parite forex yönü panoda uygulanır.
/// </summary>
public interface IEffectivePriceAppService : IApplicationService
{
    /// <summary>Görünür her birim için bu scope'un GÜNCEL efektif fiyatı, çalışılan şirketin YEREL para birimine
    /// (ülke parası: TR→TRY, US→USD) re-base'li — kurlar TEK ELDEN re-base alınır (TR'de yerel=TRY → ÷1 = pivot).
    /// "1 birim = X yerel", yerel satır 1.00. Bilanço birimi (BaseCurrencyUnitId) DEĞİL. Yerel çözülemezse pivot (TRY).</summary>
    Task<List<CurrentPriceDto>> GetCurrentPricesAsync();

    /// <summary>Çalışılan (working) şirketin YEREL para birimi unit Id'si (CountryCode→Country.DefaultCurrencyCode→CurrencyUnit);
    /// şirket/ülke/birim yoksa null. Peşin nakit + Bedelli combo default'u için (takip birimi = yerel para).</summary>
    Task<Guid?> GetWorkingLocalCurrencyUnitIdAsync();

    /// <summary>
    /// Efektif fiyatları aktif şirketin <b>base para birimine</b> re-base eder (DEĞERLEME görünümü;
    /// base=USD → USD=1). <paramref name="companyId"/> null ise scope'un HQ şirketi. Parite panosu
    /// (forex yönü) bundan AYRIDIR.
    /// </summary>
    Task<List<ValuationPriceDto>> GetValuationAsync(Guid? companyId = null);

    /// <summary>
    /// Efektifleri VERİLEN base birime re-base eder (şube bilanço birimi şirket base'inden farklı
    /// olabilir → pozisyon/bilanço raporu bunu kullanır). Boş id ya da base efektifi yoksa boş liste.
    /// <paramref name="asOf"/> verilirse (ve geçmiş bir günse) değerleme O TARİHTEKİ kurla yapılır
    /// (canlı tick atlanır, ham kur RateDate≤asOf'tan son bilinen seçilir = ERPPRO GetLastKur semantiği);
    /// null/bugün ise canlı/güncel davranış. Tarihsel bilanço bugünün kuruyla değerlenmesin diye.
    /// </summary>
    Task<List<ValuationPriceDto>> GetValuationByBaseAsync(Guid baseCurrencyUnitId, DateTime? asOf = null);
}
