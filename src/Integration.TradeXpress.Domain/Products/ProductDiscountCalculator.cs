using System;
using Volo.Abp;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN İNDİRİMİNİN SATIŞ FİYATINA UYGULANMASI — tek kaynak.
///
/// <para><b>Neden gerekli:</b> N11 indirimi BİRİNCİ SINIF bir alan olarak kabul ediyor (tip + değer + tarih
/// aralığını kendisi yorumluyor). Trendyol'da böyle bir alan YOKTUR — indirim ancak <c>listPrice</c> (üstü
/// çizili) ile <c>salePrice</c> (indirimli) ayrımıyla ifade edilir, yani hesabı BİZ yaparız. İki kanalın aynı
/// indirimi farklı yorumlaması, aynı ürünün iki pazaryerinde farklı fiyata satılması demektir.</para>
///
/// <para><b>TARİH PENCERESİ BURADA UYGULANIR.</b> N11'e tarihleri gönderip yorumu ona bırakıyoruz; Trendyol'da
/// fiyatı biz hesapladığımız için pencereyi de biz gözetmek zorundayız. Aksi hâlde süresi dolmuş bir kampanya
/// Trendyol'da SONSUZA KADAR açık kalırdı ve kimse fark etmezdi (fiyat düşük ama "hatasız" görünür).</para>
///
/// <para><b>SIFIR/EKSİ FİYAT FIRLATIR.</b> Tutar indirimi fiyattan büyükse ortada bir veri hatası vardır ve
/// sessizce 0'a kırpmak kıymetli madeni BEDAVA listelemek olurdu. Push durur, gerekçe kullanıcıya gider.</para>
/// </summary>
public static class ProductDiscountCalculator
{
    /// <summary>İndirim uygulanmış satış fiyatı. İndirim yoksa ya da tarih penceresi dışındaysa
    /// <paramref name="listPrice"/> aynen döner (çağıran <c>listPrice == salePrice</c> görür = indirim yok).</summary>
    /// <param name="today">Karşılaştırma günü — tarihler date-only (wall-clock) semantiktir, bu yüzden
    /// çağıran kullanıcının/işletmenin gününü verir; UTC saat farkı gün kaydırmasın.</param>
    public static decimal ResolveSalePrice(
        decimal listPrice,
        ProductDiscountType type,
        decimal? value,
        DateTime? startDate,
        DateTime? endDate,
        DateTime today)
    {
        if (type == ProductDiscountType.None || value is not { } discount || discount <= 0m)
        {
            return listPrice;
        }

        if (!IsWithinWindow(startDate, endDate, today))
        {
            return listPrice;
        }

        var salePrice = type == ProductDiscountType.Percentage
            ? listPrice - (listPrice * discount / 100m)
            : listPrice - discount;

        salePrice = decimal.Round(salePrice, 2, MidpointRounding.AwayFromZero);

        if (salePrice <= 0m)
        {
            throw new BusinessException("TradeXpress:Product:DiscountExceedsPrice")
                .WithData("ListPrice", listPrice)
                .WithData("Discount", discount);
        }

        return salePrice;
    }

    /// <summary>Tarihler ya İKİSİ dolu ya İKİSİ boştur (<c>Product.SetDiscount</c> bunu zorlar); boşsa
    /// indirim SÜREKLİDİR.</summary>
    private static bool IsWithinWindow(DateTime? startDate, DateTime? endDate, DateTime today)
    {
        if (startDate is not { } start || endDate is not { } end)
        {
            return true;
        }

        var day = today.Date;
        return day >= start.Date && day <= end.Date;
    }
}
