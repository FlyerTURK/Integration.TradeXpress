using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Efektif fiyat okuma — ham ExchangeRate (host pivot) + marj olaylarını kademeyle birleştirir.
/// Viewer'a göre (host/tenant) hesaplar; gizlilik ve parite forex yönü panoda uygulanır.
/// </summary>
public interface IEffectivePriceAppService : IApplicationService
{
    /// <summary>Görünür her birim için bu scope'un GÜNCEL efektif fiyatı (en son ham × güncel kademe).</summary>
    Task<List<CurrentPriceDto>> GetCurrentPricesAsync();

    /// <summary>
    /// Efektif fiyatları aktif şirketin <b>base para birimine</b> re-base eder (DEĞERLEME görünümü;
    /// base=USD → USD=1). <paramref name="companyId"/> null ise scope'un HQ şirketi. Parite panosu
    /// (forex yönü) bundan AYRIDIR.
    /// </summary>
    Task<List<ValuationPriceDto>> GetValuationAsync(Guid? companyId = null);
}
