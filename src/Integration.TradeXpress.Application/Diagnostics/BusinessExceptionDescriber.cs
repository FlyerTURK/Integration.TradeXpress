using System;
using System.Globalization;
using Integration.TradeXpress.Localization;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;

namespace Integration.TradeXpress.Diagnostics;

/// <summary>
/// KODLU HATAYI İNSANIN OKUYACAĞI METNE ÇEVİRİR — kalıcı hata alanlarına (<c>LastError</c>, rezervasyon
/// gerekçesi, gelen kutusu notu) yazılacak metnin TEK kaynağı.
///
/// <para><b>Neden ham <c>ex.Message</c> yetmiyor:</b> kodlu bir <c>BusinessException</c>'ın mesajı, kod
/// çözülmediğinde <c>"TradeXpress:SalesChannel:Product:PriceOutOfBand"</c> gibi bir ANAHTAR olur. Kullanıcı için
/// hiçbir anlamı yoktur; üstelik hatanın ne olduğunu değil NEREDE tanımlandığını söyler.</para>
///
/// <para><b>Neden yalnız lokalize etmek de yetmiyor:</b> guard'lar teşhis verisini <c>WithData</c> ile taşır
/// (hangi SKU, hangi fiyat, hangi sınır). Lokalize metin bu değerleri <c>{StockCode}</c> gibi yer tutucularla
/// bekler; ABP bunları yalnız HTTP hata dönüşümünde doldurur. Kalıcı alana yazarken doldurmazsak operatör
/// "fiyat bandın dışında ({Price})" gibi işe yaramaz bir cümle görür — yani guard'ın tüm teşhis emeği çöpe gider.
/// Bu sınıf yer tutucuları <c>Data</c>'dan doldurur.</para>
///
/// <para><b>Kültür SABİTLENİR (tr):</b> arka plan işlerinin kültürü belirsizdir ve bazı çağıranlar gerekçeyi
/// ORDINAL karşılaştırır (aynı hata tekrar tekrar yazılmasın diye). Kültür turlar arasında değişseydi aynı hata
/// her turda "değişmiş" görünürdü.</para>
/// </summary>
public class BusinessExceptionDescriber : ITransientDependency
{
    private readonly IStringLocalizer<TradeXpressResource> _l;

    public BusinessExceptionDescriber(IStringLocalizer<TradeXpressResource> l)
    {
        _l = l;
    }

    public virtual string Describe(Exception ex)
    {
        if (ex is not BusinessException { Code: { Length: > 0 } code })
        {
            return ex.Message;
        }

        using (CultureHelper.Use("tr"))
        {
            var localized = _l[code];
            if (localized.ResourceNotFound)
            {
                return ex.Message;
            }

            return FillPlaceholders(localized.Value, ex);
        }
    }

    /// <summary><c>{Ad}</c> yer tutucularını istisnanın <c>Data</c> sözlüğünden doldurur. Karşılığı olmayan
    /// yer tutucu OLDUĞU GİBİ bırakılır — silmek, cümleyi sessizce eksik ama "tamam görünen" hâle sokardı.</summary>
    private static string FillPlaceholders(string template, Exception ex)
    {
        if (ex.Data.Count == 0)
        {
            return template;
        }

        var result = template;
        foreach (var key in ex.Data.Keys)
        {
            if (key is not string name)
            {
                continue;
            }

            var value = ex.Data[key];
            result = result.Replace(
                "{" + name + "}",
                Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
                StringComparison.Ordinal);
        }

        return result;
    }
}
