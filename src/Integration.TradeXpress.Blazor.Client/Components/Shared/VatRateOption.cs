using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>KDV oranı combo satırı — pazaryeri ve ürün formlarının ORTAK seçenek tipi.
///
/// <para><b>Neden ham int listesi değil:</b> combo "20" değil "%20" göstersin diye. Değer yine <c>int</c> kalır.</para>
///
/// <para><b>Neden serbest sayı kutusu değil:</b> KDV oranı serbest bir yüzde DEĞİLDİR — mevzuatta (ve
/// pazaryerlerinde) kapalı bir kümedir. Serbest kutu, uydurma oranın kaydedilip push'ta reddedilmesine
/// ya da daha kötüsü YANLIŞ FATURA kesilmesine yol açar. Kuyumda kritik: kıymetli maden teslimi %0 (istisna
/// faturası), işçilik %20 — "hep %20" varsayımı yanlıştır.</para>
///
/// <para>Küme çağıran tarafından verilir (<c>ProductConsts.AllowedVatRates</c> = mevzuat,
/// <c>N11ProductConsts.AllowedVatRates</c> = pazaryeri kuralı) — bu tip kümeyi TANIMLAMAZ, yalnız gösterir.</para></summary>
public sealed record VatRateOption(int Rate, string DisplayText)
{
    /// <summary>Verilen orandan seçenek listesi kurar (artan sıralı). Küme SSOT'u çağıranındır.</summary>
    public static List<VatRateOption> From(IReadOnlyCollection<int> allowedRates)
    {
        return allowedRates
            .OrderBy(rate => rate)
            .Select(rate => new VatRateOption(rate, "%" + rate.ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }
}
